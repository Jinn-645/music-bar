using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>A horizontal pixel range on screen, in physical pixels.</summary>
internal readonly record struct Span(int Left, int Right)
{
    public int Width => Right - Left;
}

/// <summary>
/// Follows the Windows taskbar (Shell_TrayWnd): its rectangle, whether it is
/// on screen (auto-hide, fullscreen), and which parts of it are occupied by
/// the Start button, app icons and the notification area, so the widget can
/// sit in the empty space.
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

    public TaskbarTracker()
    {
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

    public bool AtTop => Monitor != null && Rect.Top <= Monitor.Bounds.Top + 2 && Rect.Bottom < Monitor.Bounds.Bottom - 2;

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
        if (Handle == IntPtr.Zero || !Native.IsWindow(Handle))
        {
            Handle = Native.FindWindow("Shell_TrayWnd", null);
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
        try
        {
            var (occupied, quickSettings) = await Task.Run(() => FindOccupied(hwnd, rc));
            if (hwnd != Handle || !rc.Same(Rect)) return;

            // UI Automation on the taskbar sometimes answers with nothing, or with a fragment: at
            // startup, and for as long as Windows' Quick Settings is open. The notification area
            // always ends a horizontal taskbar, so a scan without it is not the taskbar's layout.
            // Once a layout with it is known, keep that one until a scan finds the tray again;
            // before that (startup), look again shortly and settle for what there is after a few tries.
            if (!Complete(occupied, rc))
            {
                if (++_partialScans == 1)
                    Log.Info($"Taskbar scan without the tray ignored: [{string.Join(", ", occupied.Select(o => $"{o.Left}-{o.Right}"))}]");
                if (HasScanned && Complete(Occupied, rc)) return;
                if (_partialScans < 4)
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

    /// <summary>The spans reach the far end of a horizontal taskbar, where the notification area is.</summary>
    bool Complete(IEnumerable<Span> spans, Native.RECT rc)
        => !IsHorizontal || spans.Any(s => s.Right >= rc.Right - rc.Width / 20);

    /// <summary>
    /// Walks the taskbar's UI Automation tree and collects the horizontal
    /// ranges covered by real controls. Containers that span most of the bar
    /// are ignored. When icons are centered, the group is assumed symmetric
    /// around the middle, starting at the Start button.
    /// </summary>
    static (List<Span> Occupied, Span? QuickSettings) FindOccupied(IntPtr tray, Native.RECT rc)
    {
        var spans = new List<Span>();
        var quick = new List<Span>();
        double? startLeft = null;
        try
        {
            CollectFromAutomation(tray, rc, spans, quick, ref startLeft);
        }
        catch (Exception ex)
        {
            Log.Error("UI Automation scan of the taskbar failed", ex);
        }
        if (spans.Count == 0) CollectFromChildWindows(tray, rc, spans, ref startLeft);

        if (startLeft is double start && start - rc.Left > rc.Width * 0.1)
            spans.Add(new Span((int)start, (int)(rc.Left + rc.Right - start)));

        Span? quickSettings = quick.Count == 0 ? null : new Span(quick.Min(q => q.Left), quick.Max(q => q.Right));
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
    static void CollectFromChildWindows(IntPtr tray, Native.RECT rc, List<Span> spans, ref double? startLeft)
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
            if (r.Width < 2 || r.Height < 2 || r.Width > rc.Width * 0.3) continue;
            if (r.Bottom <= rc.Top || r.Top >= rc.Bottom) continue;
            if (cls == "Start") startLeft = startLeft.HasValue ? Math.Min(startLeft.Value, r.Left) : r.Left;
            spans.Add(new Span(r.Left, r.Right));
        }
    }

    static void CollectFromAutomation(IntPtr tray, Native.RECT rc, List<Span> spans, List<Span> quick, ref double? startLeft)
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
            if (!b.IsEmpty && IsQuickSettingsButton(cls, name))
                quick.Add(new Span((int)Math.Floor(b.Left), (int)Math.Ceiling(b.Right)));

            if (b.IsEmpty || b.Width < 2 || b.Height < 2) continue;
            if (b.Width > rc.Width * 0.3 || cls == TrayContainer) continue;
            if (b.Bottom <= rc.Top || b.Top >= rc.Bottom) continue;

            if (cls == "Start" || id == "StartButton")
                startLeft = startLeft.HasValue ? Math.Min(startLeft.Value, b.Left) : b.Left;
            spans.Add(new Span((int)Math.Floor(b.Left), (int)Math.Ceiling(b.Right)));
        }
    }

    static List<Span> Merge(List<Span> spans)
    {
        var merged = new List<Span>();
        foreach (var s in spans.OrderBy(s => s.Left))
        {
            if (merged.Count > 0 && s.Left <= merged[^1].Right + 2)
                merged[^1] = merged[^1] with { Right = Math.Max(merged[^1].Right, s.Right) };
            else
                merged.Add(s);
        }
        return merged;
    }

    /// <summary>Empty horizontal ranges of the taskbar.</summary>
    public List<Span> FreeSpans()
    {
        var free = new List<Span>();
        int x = Rect.Left;
        foreach (var o in Occupied)
        {
            if (o.Right <= Rect.Left || o.Left >= Rect.Right) continue;
            if (o.Left > x) free.Add(new Span(x, o.Left));
            x = Math.Max(x, o.Right);
        }
        if (x < Rect.Right) free.Add(new Span(x, Rect.Right));
        return free;
    }
}
