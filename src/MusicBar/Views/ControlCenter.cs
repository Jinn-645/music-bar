using System;
using System.Windows.Interop;
using System.Windows.Media;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// MusicBar's control center on the taskbar: while it is on, the tray's volume
/// icon and notification button are folded into the Control Center button,
/// which opens MusicBar's panel (notifications, volume, playback device). Turned
/// off, or when MusicBar exits, the tray is exactly as Windows had it.
/// Driven by the taskbar tracker's heartbeat through <see cref="Tick"/>.
/// </summary>
internal sealed class ControlCenter : IDisposable
{
    const uint KeepPlace = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;

    readonly AppSettings _s;
    readonly TaskbarTracker _tracker;
    readonly TrayMerge _tray = new();
    readonly NotificationFeed _feed = new();
    readonly AudioOutputs _audio = new();
    readonly TrayHotspotWindow _hotspot;
    readonly ControlCenterWindow _panel;
    bool _merged;
    bool _hotspotShown;
    bool _disposed;
    Native.RECT _hotspotRect;

    public ControlCenter(AppSettings settings, TaskbarTracker tracker, SolidColorBrush accent)
    {
        _s = settings;
        _tracker = tracker;
        _hotspot = new TrayHotspotWindow(accent);
        _panel = new ControlCenterWindow(_feed, _audio);
        _hotspot.Clicked += Toggle;
        _panel.OutputChanged += () => OutputChanged?.Invoke();
        _feed.Changed += () => _hotspot.SetBadge(_panel.IsOpen ? 0 : _feed.Unseen);
        _panel.IsVisibleChanged += (_, _) =>
        {
            if (_panel.IsVisible) return;
            _feed.SetFastPolling(false);
            _hotspot.SetBadge(_feed.Unseen);
        };
    }

    /// <summary>Tray items moved; the free space on the taskbar changed.</summary>
    public event Action? TrayRearranged;

    /// <summary>The default playback device was switched from the panel.</summary>
    public event Action? OutputChanged;

    /// <summary>The taskbar has the Windows 10 style tray this works with (Windows 10, ExplorerPatcher).</summary>
    public bool Supported { get; private set; }

    public void Start()
    {
        new WindowInteropHelper(_hotspot).EnsureHandle();
        new WindowInteropHelper(_panel).EnsureHandle();
        // A crash in an earlier run may have left the tray folded.
        if (!_s.ControlCenter) TrayMerge.RecoverLeftovers(Native.FindWindow("Shell_TrayWnd", null));
        _ = _feed.StartAsync();
        Tick();
    }

    public void SetAccent(Color accent)
    {
        _panel.SetAccent(accent);
        _hotspot.SetBadgeForeground(accent);
    }

    /// <summary>The language changed: the panel re-renders the text it builds itself.</summary>
    public void RefreshText() => _panel.RefreshText();

    public void Tick()
    {
        if (_disposed) return;
        if (!_s.ControlCenter)
        {
            if (_merged)
            {
                _merged = false;
                _panel.Dismiss();
                _tray.Release();
                Log.Info("Control center off: tray restored");
                TrayRearranged?.Invoke();
            }
            HideHotspot();
            return;
        }

        // Windows 10 style tray: fold volume and notifications into its Control Center button.
        bool moved = _tray.Apply(_tracker.Handle);
        if (_tray.Entry != IntPtr.Zero && !_merged)
        {
            _merged = true;
            Log.Info("Control center on: tray folded");
            moved = true;
        }
        if (moved) TrayRearranged?.Invoke();

        // Windows 11 tray: its network/volume buttons already form the Quick Settings entry.
        var bar = _tracker.Rect;
        Native.RECT? entry = _tray.Entry != IntPtr.Zero ? _tray.EntryRect
            : _tracker.QuickSettings is not { } qs ? null
            : _tracker.IsHorizontal ? new Native.RECT(qs.Start, bar.Top, qs.End, bar.Bottom)
            : new Native.RECT(bar.Left, qs.Start, bar.Right, qs.End);
        Supported = entry != null;
        if (entry is not { } r || !_tracker.Shown)
        {
            HideHotspot();
            return;
        }
        if (!_hotspotShown)
        {
            _hotspot.Show();
            _hotspotShown = true;
            _hotspot.SetBadge(_feed.Unseen);
        }
        if (!r.Same(_hotspotRect))
        {
            _hotspotRect = r;
            Native.SetWindowPos(_hotspot.Hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE);
        }
        // Clicking the taskbar lifts it over everything; keep the hotspot on top of it.
        if (!Native.IsAbove(_hotspot.Hwnd, _tracker.Handle))
            Native.SetWindowPos(_hotspot.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
    }

    void HideHotspot()
    {
        if (!_hotspotShown) return;
        _hotspot.Hide();
        _hotspotShown = false;
        _hotspotRect = default;
    }

    void Toggle()
    {
        if (_panel.IsOpen)
        {
            _panel.Dismiss();
            return;
        }
        // The click that took focus away (and closed the panel) is the same click on the button.
        if ((DateTime.Now - _panel.ClosedAt).TotalMilliseconds < 300) return;

        var monitor = _tracker.Monitor;
        if (monitor == null) return;
        _panel.Open(_tracker.Edge, _tracker.Rect, monitor.Bounds);
        _feed.SetFastPolling(true);
        _hotspot.SetBadge(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _feed.Stop();
        if (_merged) _tray.Release();
        _hotspot.Close();
        _panel.Close();
        _audio.Dispose();
    }
}
