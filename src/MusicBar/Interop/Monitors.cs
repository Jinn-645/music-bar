using System;
using System.Runtime.InteropServices;

namespace MusicBar.Interop;

internal sealed record MonitorInfo(IntPtr Handle, string DeviceName, Native.RECT Bounds, Native.RECT WorkArea, bool IsPrimary, double Scale);

internal static class Monitors
{
    public static MonitorInfo? Get(IntPtr handle)
    {
        var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>(), szDevice = "" };
        if (!Native.GetMonitorInfo(handle, ref mi)) return null;

        double scale = 1;
        if (Native.GetDpiForMonitor(handle, 0, out var dpiX, out _) == 0 && dpiX > 0) scale = dpiX / 96.0;
        return new MonitorInfo(handle, mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, scale);
    }
}
