using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using MusicBar.Interop;

namespace MusicBar.Services;

/// <summary>
/// The screen's brightness, for the control center's slider (the one Windows' Quick Settings has,
/// which a laptop misses once MusicBar's control center takes its place). A laptop's own panel goes
/// through WMI, as Windows does it; otherwise monitors that take DDC/CI commands over their cable,
/// all set alike. Every call runs off the UI thread: DDC/CI takes tens of milliseconds a command and
/// some monitors stall for a while. A drag sends only its latest value.
/// </summary>
internal sealed class ScreenBrightness : IDisposable
{
    readonly SemaphoreSlim _gate = new(1, 1);
    List<IntPtr> _physical = [];
    PhysicalMonitor[] _owned = [];
    bool _builtIn;
    int _pending = -1;
    int _sending;

    /// <summary>What the slider controls: a laptop's panel, monitors over DDC/CI, or nothing.</summary>
    public enum Kind { None, BuiltIn, External }

    public Kind Controls { get; private set; }

    /// <summary>Finds what can be dimmed on <paramref name="monitor"/> and reads its brightness (0 to 100); null when nothing can.</summary>
    public async Task<int?> ReadAsync(IntPtr monitor)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() => Read(monitor));
        }
        catch (Exception ex)
        {
            Log.Error("Reading the screen brightness failed", ex);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    int? Read(IntPtr monitor)
    {
        if (ReadBuiltIn() is int level)
        {
            _builtIn = true;
            Controls = Kind.BuiltIn;
            return level;
        }
        _builtIn = false;
        Release();
        if (monitor == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0)
        {
            Controls = Kind.None;
            return null;
        }
        var all = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, all))
        {
            Controls = Kind.None;
            return null;
        }
        _owned = all;
        int? first = null;
        foreach (var m in all)
        {
            if (ReadDdc(m.Handle) is not int percent) continue;
            _physical.Add(m.Handle);
            first ??= percent;
        }
        Controls = _physical.Count > 0 ? Kind.External : Kind.None;
        return first;
    }

    /// <summary>Sets the brightness (0 to 100); while one is being sent, only the newest waits.</summary>
    public void Set(int percent)
    {
        Interlocked.Exchange(ref _pending, Math.Clamp(percent, 0, 100));
        if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return;
        Task.Run(async () =>
        {
            try
            {
                await _gate.WaitAsync();
                try
                {
                    int value;
                    while ((value = Interlocked.Exchange(ref _pending, -1)) >= 0) Write(value);
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Setting the screen brightness failed", ex);
            }
            finally
            {
                Volatile.Write(ref _sending, 0);
                if (Volatile.Read(ref _pending) >= 0) Set(Volatile.Read(ref _pending));
            }
        });
    }

    void Write(int percent)
    {
        if (_builtIn)
        {
            using var methods = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightnessMethods"));
            foreach (ManagementObject m in methods.Get())
            {
                using (m) m.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
            }
            return;
        }
        foreach (var handle in _physical)
        {
            if (!GetMonitorBrightness(handle, out uint min, out _, out uint max) || max <= min)
            {
                SetVCPFeature(handle, VcpBrightness, (uint)percent);
                continue;
            }
            SetMonitorBrightness(handle, min + (uint)Math.Round((max - min) * percent / 100.0));
        }
    }

    /// <summary>A laptop's own panel, as Windows reads it; null on a PC without one.</summary>
    static int? ReadBuiltIn()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"), new SelectQuery("WmiMonitorBrightness"));
            foreach (ManagementObject m in searcher.Get())
            {
                using (m)
                {
                    if (m["Active"] is bool active && !active) continue;
                    if (m["CurrentBrightness"] is byte level) return level;
                }
            }
        }
        catch (ManagementException)
        {
            // "Not supported": no built-in panel here.
        }
        catch (COMException)
        {
        }
        return null;
    }

    /// <summary>A monitor's brightness over DDC/CI, 0 to 100; null when it doesn't answer.</summary>
    static int? ReadDdc(IntPtr handle)
    {
        if (GetMonitorBrightness(handle, out uint min, out uint current, out uint max) && max > min)
            return (int)Math.Round((current - min) * 100.0 / (max - min));
        if (GetVCPFeatureAndVCPFeatureReply(handle, VcpBrightness, out _, out uint value, out uint top) && top > 0)
            return (int)Math.Round(value * 100.0 / top);
        return null;
    }

    void Release()
    {
        if (_owned.Length > 0) DestroyPhysicalMonitors((uint)_owned.Length, _owned);
        _owned = [];
        _physical = [];
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            Release();
        }
        finally
        {
            _gate.Release();
        }
    }

    const byte VcpBrightness = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
}
