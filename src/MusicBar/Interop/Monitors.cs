using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MusicBar.Interop;

internal sealed record MonitorInfo(IntPtr Handle, string DeviceName, Native.RECT Bounds, Native.RECT WorkArea, bool IsPrimary, double Scale);

/// <summary>A taskbar window and the monitor it sits on; the main one is Shell_TrayWnd.</summary>
internal sealed record TaskbarWindow(IntPtr Handle, MonitorInfo Monitor, bool IsMain);

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

    /// <summary>Every monitor connected to the desktop, left to right.</summary>
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        MonitorEnumProc callback = (handle, _, _, _) =>
        {
            if (Get(handle) is { } m) list.Add(m);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        list.Sort((a, b) => a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top));
        return list;
    }

    /// <summary>The main taskbar and every visible secondary one ("show my taskbar on all displays").</summary>
    public static List<TaskbarWindow> Taskbars()
    {
        var list = new List<TaskbarWindow>();
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero && On(main) is { } m) list.Add(new TaskbarWindow(main, m, true));
        for (var h = IntPtr.Zero; (h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero;)
            if (Native.IsWindowVisible(h) && On(h) is { } s) list.Add(new TaskbarWindow(h, s, false));
        return list;
    }

    /// <summary>The secondary taskbar on a monitor, by the monitor's device name.</summary>
    public static IntPtr SecondaryTaskbarOn(string deviceName)
    {
        foreach (var t in Taskbars())
            if (!t.IsMain && t.Monitor.DeviceName == deviceName) return t.Handle;
        return IntPtr.Zero;
    }

    /// <summary>"\\.\DISPLAY2" → 2, the number Windows shows for the screen.</summary>
    public static int Number(string deviceName)
    {
        int i = deviceName.Length;
        while (i > 0 && char.IsDigit(deviceName[i - 1])) i--;
        return int.TryParse(deviceName[i..], out int n) ? n : 0;
    }

    static MonitorInfo? On(IntPtr window) => Get(Native.MonitorFromWindow(window, Native.MONITOR_DEFAULTTONEAREST));

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
}
