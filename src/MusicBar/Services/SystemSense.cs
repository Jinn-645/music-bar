using System;
using System.Runtime.InteropServices;
using MusicBar.Infrastructure;
using Windows.Networking.Connectivity;

namespace MusicBar.Services;

/// <summary>What the face can notice about the computer.</summary>
/// <param name="Cpu">Processor load over the last couple of seconds, 0 to 1.</param>
/// <param name="Online">Connected to the internet; null when Windows can't say.</param>
/// <param name="Charging">On the charger; null on a computer without a battery.</param>
/// <param name="Battery">Charge left in percent, or -1 without a battery.</param>
public readonly record struct SystemState(double Cpu, bool? Online, bool? Charging, int Battery);

/// <summary>
/// Reads the processor load, the internet connection and the power state, at most every couple of
/// seconds however often it is asked (every face window on every screen shares it).
/// </summary>
public static class SystemSense
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    static DateTime _readAt = DateTime.MinValue;
    static SystemState _last = new(0, null, null, -1);
    static ulong _idle, _kernel, _user;
    static bool _networkFailed;

    public static SystemState Read()
    {
        var now = DateTime.Now;
        if (now - _readAt < Every) return _last;
        _readAt = now;
        _last = new SystemState(Cpu(), Online(), Charging(out int battery), battery);
        return _last;
    }

    static double Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong i = idle.Value, k = kernel.Value, u = user.Value;
        ulong di = i - _idle, dk = k - _kernel, du = u - _user;
        bool first = _kernel == 0;
        (_idle, _kernel, _user) = (i, k, u);
        // Kernel time includes idle time.
        ulong busy = dk + du - di, total = dk + du;
        return first || total == 0 ? 0 : Math.Clamp((double)busy / total, 0, 1);
    }

    static bool? Online()
    {
        if (_networkFailed) return null;
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile != null && profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }
        catch (Exception ex)
        {
            _networkFailed = true;
            Log.Error("Reading the network state failed", ex);
            return null;
        }
    }

    static bool? Charging(out int battery)
    {
        battery = -1;
        if (!GetSystemPowerStatus(out var p) || (p.BatteryFlag & 128) != 0 || p.BatteryFlag == 255) return null;
        battery = p.BatteryLifePercent <= 100 ? p.BatteryLifePercent : -1;
        return p.ACLineStatus == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileTime
    {
        public uint Low, High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemPowerStatus(out PowerStatus status);
}
