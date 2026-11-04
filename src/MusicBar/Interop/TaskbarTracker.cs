using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>
/// A stretch of the taskbar along its length, in physical screen pixels: x on a
/// horizontal taskbar, y (top to bottom) on a vertical one.
/// </summary>
internal readonly record struct Span(int Start, int End)
{
    public int Width => End - Start;
}

/// <summary>The screen edge the taskbar is docked to.</summary>
internal enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>
/// Follows the Windows taskbar (Shell_TrayWnd): its rectangle, whether it is
/// on screen (auto-hide, fullscreen), and which parts of it are occupied by
/// the Start button, app icons and the notification area, so the widget can
/// sit in the empty space. Positions along the taskbar are <see cref="Span"/>s,
/// so the same layout works for a taskbar docked to any edge.
/// </summary>
internal sealed class TaskbarTracker : IDisposable
{
    static readonly TimeSpan SlowTick = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan FastTick = TimeSpan.FromMilliseconds(30);
    static readonly TimeSpan ScanEvery = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan PartialRetry = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// The notification area's container. Its visible children cover the same
    /// ground, and when the control center folds tray items away its left part
    /// is actually empty.
    /// </summary>
    const string TrayContainer = "TrayNotifyWnd";

    readonly DispatcherTimer _poll = new() { Interval = SlowTick };
    readonly Native.WinEventProc _foregroundProc;
    IntPtr _hook;
    DateTime _fastUntil;
    DateTime _lastScan = DateTime.MinValue;
    bool _scanning;
    int _partialScans;

    // The monitor (device name) whose secondary taskbar this follows; null follows the main taskbar.
    readonly string? _monitorDevice;

    public TaskbarTracker(string? monitorDevice = null)
    {
        _monitorDevice = monitorDevice;
        _foregroundProc = (_, _, _, _, _, _, _) => ZOrderCheck?.Invoke();
        _poll.Tick += (_, _) => Tick();
    }

    public IntPtr Handle { get; private set; }
    public Native.RECT Rect { get; private set; }
    public MonitorInfo? Monitor { get; private set; }
    public bool Shown { get; private set; }
    public IReadOnlyList<Span> Occupied { get; private set; } = [];

    /// <summary>
    /// Windows 11's tray buttons that open Quick Settings (network, volume,
    /// battery), as one span; null on Windows 10 style taskbars.
    /// </summary>
    public Span? QuickSettings { get; private set; }

    /// <summary>At least one UI Automation scan finished for the current taskbar.</summary>
    public bool HasScanned { get; private set; }

    public bool IsHorizontal => Rect.Width >= Rect.Height;

    public bool AtTop => Edge == TaskbarEdge.Top;

    public TaskbarEdge Edge
    {
        get
        {
            if (Monitor == null) return IsHorizontal ? TaskbarEdge.Bottom : TaskbarEdge.Left;
            var b = Monitor.Bounds;
            if (IsHorizontal) return Rect.Top <= b.Top + 2 && Rect.Bottom < b.Bottom - 2 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
            return Rect.Left <= b.Left + 2 && Rect.Right < b.Right - 2 ? TaskbarEdge.Left : TaskbarEdge.Right;
        }
    }

    /// <summary>Where the taskbar begins along its length (its left, or its top when vertical).</summary>
    public int Begin => IsHorizontal ? Rect.Left : Rect.Top;

    public int End => IsHorizontal ? Rect.Right : Rect.Bottom;

    public int Length => End - Begin;

    /// <summary>The taskbar's short side, in physical pixels.</summary>
    public int Thickness => IsHorizontal ? Rect.Height : Rect.Width;

    public double Scale => Monitor?.Scale ?? 1;

    /// <summary>Rectangle, visibility or occupied areas changed.</summary>
    public event Action? LayoutChanged;

    /// <summary>Something may have reordered windows; overlays should re-assert their z-order.</summary>
    public event Action? ZOrderCheck;

    public void Start()
    {
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        _poll.Start();
        Tick();
    }

    /// <summary>Something rearranged the taskbar's items: look again on the next tick.</summary>
    public void Rescan() => _lastScan = DateTime.MinValue;

    public void Dispose()
    {
        _poll.Stop();
        if (_hook != IntPtr.Zero) Native.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    void Tick()
    {
        bool changed = false;
        // A secondary taskbar can end up on another monitor when screens are rearranged: find ours again.
        bool lost = Handle == IntPtr.Zero || !Native.IsWindow(Handle)
                    || (_monitorDevice != null && Monitor != null && Monitor.DeviceName != _monitorDevice);
        if (lost)
        {
            Handle = _monitorDevice == null ? Native.FindWindow("Shell_TrayWnd", null) : Monitors.SecondaryTaskbarOn(_monitorDevice);
            Rect = default;
            Occupied = [];
            HasScanned = false;
            changed = true;
        }

        if (Handle == IntPtr.Zero)
        {
            if (Shown)
            {
                Shown = false;
                LayoutChanged?.Invoke();
            }
            return;
        }

        if (Native.GetWindowRect(Handle, out var rc) && !rc.Same(Rect))
        {
            Rect = rc;
            Monitor = Monitors.Get(Native.MonitorFromWindow(Handle, Native.MONITOR_DEFAULTTONEAREST));
            _fastUntil = DateTime.Now.AddSeconds(1);
            changed = true;
        }

        bool shown = Native.IsWindowVisible(Handle) && VisibleThickness() >= 6;
        if (shown != Shown)
        {
            Shown = shown;
            changed = true;
        }

        // Poll fast while the taskbar is sliding (auto-hide) so the overlay follows it smoothly.
        _poll.Interval = DateTime.Now < _fastUntil ? FastTick : SlowTick;

        if (changed) LayoutChanged?.Invoke();
        if (!_scanning && (changed || DateTime.Now - _lastScan > ScanEvery)) Scan();
        ZOrderCheck?.Invoke();
    }

    int VisibleThickness()
    {
        if (Monitor == null) return Rect.Height;
        var b = Monitor.Bounds;
        int h = Math.Min(Rect.Bottom, b.Bottom) - Math.Max(Rect.Top, b.Top);
        int w = Math.Min(Rect.Right, b.Right) - Math.Max(Rect.Left, b.Left);
        return Math.Min(h, w);
    }

    async void Scan()
    {
        _scanning = true;
        _lastScan = DateTime.Now;
        var hwnd = Handle;
        var rc = Rect;
        bool horizontal = IsHorizontal;
        try
        {
            var (occupied, quickSettings) = await Task.Run(() => FindOccupied(hwnd, rc, horizontal));
            if (hwnd != Handle || !rc.Same(Rect)) return;

            // UI Automation on the taskbar sometimes answers with nothing, or with a fragment: at
            // startup, and for as long as Windows' Quick Settings is open. The notification area
            // always ends the taskbar (its right, or its bottom when vertical), so a scan without it
            // is not the taskbar's layout. Once a layout with it is known, keep that one until a scan
            // finds the tray again; before that (startup, Explorer restarting), look again every half
            // second and settle for what there is after about five (a secondary taskbar may have no
            // clock at its end).
            if (!Complete(occupied))
            {
                if (++_partialScans == 1)
                    Log.Info($"Taskbar scan without the tray ignored: [{Describe(occupied)}]");
                if (HasScanned && Complete(Occupied)) return;
                if (_partialScans < 10)
                {
                    _lastScan = DateTime.Now - ScanEvery + PartialRetry;
                    return;
                }
            }
            else
            {
                _partialScans = 0;
            }

            if (quickSettings != QuickSettings && (quickSettings != null || occupied.Count > 0))
            {
                QuickSettings = quickSettings;
                LayoutChanged?.Invoke();
            }

            if (!HasScanned || !occupied.SequenceEqual(Occupied))
            {
                Occupied = occupied;
                HasScanned = true;
                LayoutChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Scanning the taskbar failed", ex);
        }
        finally
        {
            _scanning = false;
        }
    }

    /// <summary>The spans reach the far end of the taskbar, where the notification area is.</summary>
    bool Complete(IEnumerable<Span> spans) => spans.Any(s => s.End >= End - Length / 20);

    public static string Describe(IEnumerable<Span> spans) => string.Join(", ", spans.Select(o => $"{o.Start}-{o.End}"));

    /// <summary>A screen rectangle as a stretch along the taskbar, and across it.</summary>
    readonly record struct Along(double Start, double End, double CrossStart, double CrossEnd)
    {
        public double Length => End - Start;

        public static Along Of(System.Windows.Rect r, bool horizontal) => horizontal
            ? new Along(r.Left, r.Right, r.Top, r.Bottom)
            : new Along(r.Top, r.Bottom, r.Left, r.Right);

        public static Along Of(Native.RECT r, bool horizontal) => horizontal
            ? new Along(r.Left, r.Right, r.Top, r.Bottom)
            : new Along(r.Top, r.Bottom, r.Left, r.Right);

        public Span ToSpan() => new((int)Math.Floor(Start), (int)Math.Ceiling(End));
    }

    /// <summary>
    /// Walks the taskbar's UI Automation tree and collects the stretches along
    /// it covered by real controls. Containers that span most of the bar are
    /// ignored. When icons are centered, the group is assumed symmetric around
    /// the middle, starting at the Start button.
    /// </summary>
    static (List<Span> Occupied, Span? QuickSettings) FindOccupied(IntPtr tray, Native.RECT rc, bool horizontal)
    {
        var bar = Along.Of(rc, horizontal);
        var spans = new List<Span>();
        var quick = new List<Span>();
        double? startAt = null;
        try
        {
            CollectFromAutomation(tray, bar, horizontal, spans, quick, ref startAt);
        }
        catch (Exception ex)
        {
            Log.Error("UI Automation scan of the taskbar failed", ex);
        }
        if (spans.Count == 0) CollectFromChildWindows(tray, bar, horizontal, spans, ref startAt);

        if (startAt is double start && start - bar.Start > bar.Length * 0.1)
            spans.Add(new Span((int)start, (int)(bar.Start + bar.End - start)));

        Span? quickSettings = quick.Count == 0 ? null : new Span(quick.Min(q => q.Start), quick.Max(q => q.End));
        return (Merge(spans), quickSettings);
    }

    /// <summary>
    /// Windows 11 tray buttons that open Quick Settings, recognised by their
    /// accessible names (English and Vietnamese Windows).
    /// </summary>
    static readonly string[] QuickSettingsNames =
        ["Network", "Wi-Fi", "Ethernet", "Volume", "Speakers", "Headphones", "Battery", "Mạng", "Âm lượng", "Loa", "Pin"];

    static bool IsQuickSettingsButton(string cls, string name) =>
        cls.StartsWith("SystemTray.", StringComparison.Ordinal)
        && QuickSettingsNames.Any(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Fallback for when UI Automation returns nothing: the Windows 10 style
    /// taskbar's own child windows (Start button, notification area, ...).
    /// </summary>
    static void CollectFromChildWindows(IntPtr tray, Along bar, bool horizontal, List<Span> spans, ref double? startAt)
    {
        var found = new List<(string Class, Native.RECT Rect)>();
        Native.EnumWindowsProc callback = (h, _) =>
        {
            if (Native.IsWindowVisible(h) && Native.GetWindowRect(h, out var r)) found.Add((Native.ClassName(h), r));
            return true;
        };
        Native.EnumChildWindows(tray, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        foreach (var (cls, r) in found)
        {
            if (cls == TrayContainer) continue;
            var a = Along.Of(r, horizontal);
            if (r.Width < 2 || r.Height < 2 || a.Length > bar.Length * 0.3) continue;
            if (a.CrossEnd <= bar.CrossStart || a.CrossStart >= bar.CrossEnd) continue;
            if (cls == "Start") startAt = startAt.HasValue ? Math.Min(startAt.Value, a.Start) : a.Start;
            spans.Add(a.ToSpan());
        }
    }

    static void CollectFromAutomation(IntPtr tray, Along bar, bool horizontal, List<Span> spans, List<Span> quick, ref double? startAt)
    {
        var root = AutomationElement.FromHandle(tray);
        var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

        foreach (AutomationElement e in all)
        {
            System.Windows.Rect b;
            string cls, id, name;
            try
            {
                var c = e.Current;
                if (c.IsOffscreen) continue;
                b = c.BoundingRectangle;
                cls = c.ClassName ?? "";
                id = c.AutomationId ?? "";
                name = c.Name ?? "";
            }
            catch (Exception)
            {
                continue;
            }
            if (b.IsEmpty) continue;
            var a = Along.Of(b, horizontal);
            if (IsQuickSettingsButton(cls, name)) quick.Add(a.ToSpan());

            if (b.Width < 2 || b.Height < 2) continue;
            if (a.Length > bar.Length * 0.3 || cls == TrayContainer) continue;
            if (a.CrossEnd <= bar.CrossStart || a.CrossStart >= bar.CrossEnd) continue;

            if (cls == "Start" || id == "StartButton")
                startAt = startAt.HasValue ? Math.Min(startAt.Value, a.Start) : a.Start;
            spans.Add(a.ToSpan());
        }
    }

    static List<Span> Merge(List<Span> spans)
    {
        var merged = new List<Span>();
        foreach (var s in spans.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && s.Start <= merged[^1].End + 2)
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, s.End) };
            else
                merged.Add(s);
        }
        return merged;
    }

    /// <summary>Empty stretches of the taskbar.</summary>
    public List<Span> FreeSpans()
    {
        var free = new List<Span>();
        int at = Begin, end = End;
        foreach (var o in Occupied)
        {
            if (o.End <= Begin || o.Start >= end) continue;
            if (o.Start > at) free.Add(new Span(at, o.Start));
            at = Math.Max(at, o.End);
        }
        if (at < end) free.Add(new Span(at, end));
        return free;
    }
}
