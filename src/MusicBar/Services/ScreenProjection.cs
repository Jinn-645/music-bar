using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using Windows.Devices.Enumeration;
using Windows.UI.ViewManagement;

namespace MusicBar.Services;

/// <summary>How the desktop spreads over the connected displays (what Win+P picks).</summary>
public enum ProjectMode
{
    PcScreenOnly = 1, // DISPLAYCONFIG_TOPOLOGY_INTERNAL
    Duplicate = 2,    // DISPLAYCONFIG_TOPOLOGY_CLONE
    Extend = 4,       // DISPLAYCONFIG_TOPOLOGY_EXTEND
    SecondScreenOnly = 8, // DISPLAYCONFIG_TOPOLOGY_EXTERNAL
}

/// <summary>Wired displays: the Win+P modes, through the documented display configuration API.</summary>
public static class DisplayProjection
{
    const uint QDC_DATABASE_CURRENT = 0x4;
    const uint SDC_APPLY = 0x80;
    const int PathInfoSize = 72, ModeInfoSize = 64;

    /// <summary>The current mode, or null when Windows does not say.</summary>
    public static ProjectMode? Current()
    {
        IntPtr paths = IntPtr.Zero, modes = IntPtr.Zero;
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_DATABASE_CURRENT, out uint pathCount, out uint modeCount) != 0) return null;
            paths = Marshal.AllocHGlobal((int)Math.Max(1, pathCount) * PathInfoSize);
            modes = Marshal.AllocHGlobal((int)Math.Max(1, modeCount) * ModeInfoSize);
            if (QueryDisplayConfig(QDC_DATABASE_CURRENT, ref pathCount, paths, ref modeCount, modes, out int topology) != 0) return null;
            return Enum.IsDefined(typeof(ProjectMode), topology) ? (ProjectMode)topology : null;
        }
        catch (Exception ex)
        {
            Log.Error("Reading the display mode failed", ex);
            return null;
        }
        finally
        {
            if (paths != IntPtr.Zero) Marshal.FreeHGlobal(paths);
            if (modes != IntPtr.Zero) Marshal.FreeHGlobal(modes);
        }
    }

    /// <summary>Switches the mode; false when Windows refuses (for example with a single display).</summary>
    public static bool Set(ProjectMode mode)
    {
        int result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | (uint)mode);
        if (result != 0) Log.Error($"Switching the display mode to {mode} failed: {result}");
        else Log.Info($"Display mode: {mode}");
        return result == 0;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths, ref uint modeCount, IntPtr modes, out int topology);

    [DllImport("user32.dll")]
    static extern int SetDisplayConfig(uint pathCount, IntPtr paths, uint modeCount, IntPtr modes, uint flags);
}

/// <summary>
/// Wireless displays (Miracast): found and connected through WinRT's ProjectionManager,
/// without the Win+K flyout. Windows reports the projection call loosely, so whether a
/// session is up is read from the Miracast control connection (TCP port 7236).
/// </summary>
public static class WirelessDisplays
{
    const int MiracastPort = 7236;

    /// <summary>The device class of wired monitors, which the projection selector lists too.</summary>
    const string MonitorInterface = "{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    /// <summary>Miracast runs over Wi-Fi Direct: without a Wi-Fi adapter plugged in there is nothing to find.</summary>
    public static bool HasWifi =>
        NetworkInterface.GetAllNetworkInterfaces().Any(n =>
            n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus != OperationalStatus.NotPresent);

    public static bool Connected
    {
        get
        {
            try
            {
                return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(c =>
                    c.State == TcpState.Established && (c.LocalEndPoint.Port == MiracastPort || c.RemoteEndPoint.Port == MiracastPort));
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static async Task<IReadOnlyList<DeviceInformation>> FindAsync()
    {
        try
        {
            var found = await DeviceInformation.FindAllAsync(ProjectionManager.GetDeviceSelector());
            // Wired monitors belong to "Màn hình chiếu", not here.
            return found
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && !d.Id.Contains(MonitorInterface, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Looking for wireless displays failed", ex);
            return [];
        }
    }

    /// <summary>Starts casting the desktop to the display; true once the session is up.</summary>
    public static async Task<bool> ConnectAsync(DeviceInformation display)
    {
        try
        {
            await ProjectionManager.StartProjectingAsync(0, 0, display);
        }
        catch (Exception ex)
        {
            // Often thrown even though the connection goes ahead; the session check decides.
            Log.Info($"StartProjectingAsync to '{display.Name}': {ex.GetBaseException().Message}");
        }
        for (int i = 0; i < 40 && !Connected; i++) await Task.Delay(250);
        Log.Info($"Casting to '{display.Name}': {(Connected ? "connected" : "no session")}");
        return Connected;
    }

    public static async Task DisconnectAsync()
    {
        try
        {
            await ProjectionManager.StopProjectingAsync(0, 0);
        }
        catch (Exception ex)
        {
            Log.Info($"StopProjectingAsync: {ex.GetBaseException().Message}");
        }
    }
}
