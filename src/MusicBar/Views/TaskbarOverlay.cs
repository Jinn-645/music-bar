using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// Puts MusicBar on the Windows taskbar: a click-through progress layer that
/// spans the whole taskbar (just below its icons), a capsule widget with the
/// cover, transport buttons and expandable lyrics in the taskbar's empty
/// area, and a song card that pops up from the cover.
/// </summary>
public sealed class TaskbarOverlay : IDisposable
{
    const uint KeepPlace = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;

    readonly AppSettings _s;
    readonly MediaService _media;
    readonly LyricsService _lyricsService;
    readonly TrackInfoService _trackInfo;
    readonly TaskbarTracker _tracker = new();
    readonly TaskbarLayerWindow _layer;
    readonly WidgetWindow _widget;
    readonly InfoPopupWindow _popup;
    readonly LyricsBandWindow _band;
    readonly SolidColorBrush _accentBrush = new(ColorUtil.Brand);
    readonly SolidColorBrush _playGlyphBrush = new(Colors.White);
    readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly AudioMeter _audio = new();
    readonly DispatcherTimer _frameTimer = new(DispatcherPriority.Render);
    readonly System.Collections.Generic.List<(double Left, double Right)> _quietZones = [];
    (double Left, double Right)? _quietShown;
    DateTime _quietLostAt = DateTime.MinValue;
    readonly ControlCenter _controlCenter;

    Palette? _artPalette;
    CancellationTokenSource? _lyricsCts;
    LyricsResult? _lyricsResult;
    LyricsStatus _lyricsStatus = LyricsStatus.None;
    static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3)];
    CancellationTokenSource? _detailsCts;
    TrackDetails? _details;

    DateTime _noSessionSince = DateTime.MinValue;
    bool _fullscreen;
    bool _idle;
    bool _layerShown;
    bool _widgetShown;
    bool _rendering;
    bool _disposed;

    bool? _translucentTb;
    DateTime _translucentCheckedAt;
    TaskbarKind _kind;
    DateTime _kindCheckedAt = DateTime.MinValue;
    bool _transparencyApplied;
    Color _currentPrimary = ColorUtil.Brand;
    Color _currentSecondary = ColorUtil.Brand;
    Native.RECT _lastWidgetRect;
    Native.RECT? _bandRect;
    bool _usingBand;
    bool _lyricsShownLogged;

    // What the lyrics area shows, so it can be re-staged when it moves between band and widget.
    SyncedLyrics? _stagedLyrics;
    string _stagedTitle = "";
    string _stagedArtist = "";

    double _styledHeight = -1;
    bool _styledEdgeTop = true;
    bool _styledOver;

    public TaskbarOverlay(AppSettings settings, MediaService media, LyricsService lyrics)
    {
        _s = settings;
        _media = media;
        _lyricsService = lyrics;
        _trackInfo = new TrackInfoService(settings);
        _layer = new TaskbarLayerWindow { Audio = _audio };
        _widget = new WidgetWindow(media, _accentBrush, _playGlyphBrush);
        _popup = new InfoPopupWindow(_accentBrush);
        _band = new LyricsBandWindow();
        _widget.MenuRequested += ShowMenu;
        _widget.InfoRequested += ShowInfo;
        _widget.InfoDismissed += _popup.Dismiss;
        _widget.ExpandToggled += () => _s.LyricsExpanded = !_s.LyricsExpanded;

        _controlCenter = new ControlCenter(settings, _tracker, _accentBrush);
        _controlCenter.TrayRearranged += _tracker.Rescan;
        // A new playback device: start listening to it instead of the old one.
        _controlCenter.OutputChanged += () =>
        {
            _audio.SetActive(false);
            Watch();
        };

        _tracker.LayoutChanged += Reposition;
        _tracker.ZOrderCheck += () =>
        {
            UpdateTransparency();
            EnsureZOrder();
            _controlCenter.Tick();
        };
        _s.PropertyChanged += OnSettingChanged;
        Loc.Changed += OnLanguageChanged;
        _media.PropertyChanged += OnMediaPropertyChanged;
        _media.TrackChanged += (_, _) => OnTrackChanged();
        _media.ThumbnailChanged += (_, _) => OnThumbnailChanged();
        _watch.Tick += (_, _) => Watch();
        _frameTimer.Tick += OnFrame;
    }

    /// <summary>True when the layer is drawn over the taskbar instead of under it.</summary>
    public bool IsOverMode
    {
        get
        {
            if (_s.LayerMode == LayerMode.Over) return true;
            if (_s.LayerMode == LayerMode.Under) return false;
            return !TranslucentTaskbarDetected && !OwnsTransparency;
        }
    }

    /// <summary>
    /// MusicBar clears the taskbar background itself: allowed by the user and
    /// TranslucentTB is not already doing it. Windows 10 style taskbars take a
    /// DWM accent; the Windows 11 taskbar needs the helper inside Explorer.
    /// </summary>
    public bool OwnsTransparency => _s.TransparentTaskbar && !TranslucentTaskbarDetected;

    TaskbarKind CurrentTaskbarKind
    {
        get
        {
            if (DateTime.Now - _kindCheckedAt > TimeSpan.FromSeconds(5))
            {
                _kind = TaskbarTweaks.Kind;
                _kindCheckedAt = DateTime.Now;
            }
            return _kind;
        }
    }

    /// <summary>Explorer resets the accent every now and then (Start menu, theme), so this runs on every tick.</summary>
    void UpdateTransparency()
    {
        if (_disposed) return;
        if (OwnsTransparency)
        {
            if (CurrentTaskbarKind == TaskbarKind.Windows11) XamlTaskbar.Clear(_tracker.Handle);
            else TaskbarTransparency.ApplyAll();
            _transparencyApplied = true;
        }
        else if (_transparencyApplied)
        {
            _transparencyApplied = false;
            RestoreTransparency();
        }
    }

    static void RestoreTransparency()
    {
        XamlTaskbar.Restore();
        TaskbarTransparency.RestoreAll();
    }

    /// <summary>A tool that makes the taskbar see-through (TranslucentTB) is running.</summary>
    public bool TranslucentTaskbarDetected
    {
        get
        {
            if (_translucentTb == null || DateTime.Now - _translucentCheckedAt > TimeSpan.FromSeconds(10))
            {
                var found = Process.GetProcessesByName("TranslucentTB");
                _translucentTb = found.Length > 0;
                foreach (var p in found) p.Dispose();
                _translucentCheckedAt = DateTime.Now;
            }
            return _translucentTb.Value;
        }
    }

    public void Start()
    {
        new WindowInteropHelper(_layer).EnsureHandle();
        new WindowInteropHelper(_widget).EnsureHandle();
        new WindowInteropHelper(_popup).EnsureHandle();
        new WindowInteropHelper(_band).EnsureHandle();
        ApplyAppearance();
        UpdateLyricsSurface(animate: false);
        _widget.SetPlaying(_media.IsPlaying, animate: false);
        OnSessionStateChanged();
        _controlCenter.Start();
        _tracker.Start();
        _watch.Start();
    }

    /// <summary>The taskbar's tray can be folded into the control center (Windows 10 style tray).</summary>
    public bool ControlCenterSupported => _controlCenter.Supported;

    public void PreviewLyrics() => ActiveLyrics.Preview();

    /// <summary>The presenter currently on screen: the free-space band, or the panel beside the cover.</summary>
    LyricsPresenter ActiveLyrics => _usingBand ? _band.LyricsView : _widget.LyricsView;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loc.Changed -= OnLanguageChanged;
        _controlCenter.Dispose();
        if (_transparencyApplied) RestoreTransparency();
        SetRendering(false);
        _watch.Stop();
        _tracker.Dispose();
        _audio.Dispose();
        _lyricsCts?.Cancel();
        _detailsCts?.Cancel();
        _layer.Close();
        _widget.Close();
        _popup.Close();
        _band.Close();
    }

    // ------------------------------------------------------------------ placement

    void Reposition()
    {
        if (_disposed) return;
        bool show = _tracker.Shown && _tracker.IsHorizontal && !_fullscreen && !_idle;
        if (!show)
        {
            HideAll();
            return;
        }

        var rc = _tracker.Rect;
        double scale = _tracker.Scale;
        bool over = IsOverMode;
        bool edgeTop = !_tracker.AtTop;
        double height = rc.Height / scale;
        if (Math.Abs(height - _styledHeight) > 0.1 || edgeTop != _styledEdgeTop || over != _styledOver)
        {
            _styledHeight = height;
            _styledEdgeTop = edgeTop;
            _styledOver = over;
            ApplyAppearance();
        }

        if (!_layerShown)
        {
            _layer.Show();
            _layerShown = true;
        }
        Native.SetWindowPos(_layer.Hwnd, Native.HWND_TOPMOST, rc.Left, rc.Top, rc.Width, rc.Height, Native.SWP_NOACTIVATE);
        if (!over) Native.SetWindowPos(_layer.Hwnd, _tracker.Handle, 0, 0, 0, 0, KeepPlace);

        var widgetRect = WidgetRect(rc, scale, out bool anchorRight);
        if (widgetRect is { } w)
        {
            _widget.SetAnchor(anchorRight);
            if (!_widgetShown)
            {
                _widget.Show();
                _widgetShown = true;
            }
            Native.SetWindowPos(_widget.Hwnd, Native.HWND_TOPMOST, w.Left, w.Top, w.Width, w.Height, Native.SWP_NOACTIVATE);
            if (!w.Same(_lastWidgetRect))
            {
                _lastWidgetRect = w;
                Log.Info($"Taskbar {rc}, occupied [{string.Join(", ", _tracker.Occupied.Select(o => $"{o.Left}-{o.Right}"))}], widget {w}");
            }
        }
        else if (_widgetShown)
        {
            _widget.Hide();
            _widgetShown = false;
            _popup.Dismiss();
        }

        _bandRect = BandRect(rc, scale, widgetRect);
        UpdateLyricsSurface(animate: true);
        SetRendering(true);
    }

    void HideAll()
    {
        if (_layerShown) _layer.Hide();
        if (_widgetShown) _widget.Hide();
        _layerShown = _widgetShown = false;
        _popup.Dismiss();
        _band.Reveal(false, 0);
        SetRendering(false);
    }

    /// <summary>
    /// The biggest empty stretch of taskbar not taken by the widget, whatever
    /// the icon alignment: the left side when icons are centred, the middle
    /// when they sit on the left. Lyrics are centred inside it.
    /// </summary>
    Native.RECT? BandRect(Native.RECT rc, double scale, Native.RECT? widget)
    {
        if (!_tracker.HasScanned || _tracker.Occupied.Count == 0) return null;
        int margin = (int)Math.Round(16 * scale);
        int min = (int)Math.Round(220 * scale);
        int max = (int)Math.Round(820 * scale);

        var pieces = new System.Collections.Generic.List<Span>();
        foreach (var f in _tracker.FreeSpans())
        {
            if (widget is { } w && w.Right > f.Left && w.Left < f.Right)
            {
                if (w.Left > f.Left) pieces.Add(new Span(f.Left, w.Left));
                if (w.Right < f.Right) pieces.Add(new Span(w.Right, f.Right));
            }
            else
            {
                pieces.Add(f);
            }
        }

        var best = pieces.Where(p => p.Width - 2 * margin >= min).OrderByDescending(p => p.Width).FirstOrDefault();
        if (best.Width == 0) return null;
        int width = Math.Min(best.Width - 2 * margin, max);
        int x = best.Left + (best.Width - width) / 2;
        return new Native.RECT(x, rc.Top, x + width, rc.Bottom);
    }

    /// <summary>
    /// Shows or hides the lyrics wherever they currently belong: in the
    /// free-space band, or in the panel that slides out beside the cover.
    /// </summary>
    void UpdateLyricsSurface(bool animate)
    {
        bool useBand = _s.LyricsPlacement == LyricsPlacement.LargestGap && _bandRect != null;
        if (useBand != _usingBand)
        {
            _usingBand = useBand;
            Restage();
        }

        bool show = _s.LyricsExpanded && _layerShown;
        if (show != _lyricsShownLogged)
        {
            _lyricsShownLogged = show;
            Log.Info($"Lyrics surface: show={show} band={useBand} bandRect={_bandRect?.ToString() ?? "none"}");
        }
        _widget.SetExpanded(show && !useBand, animate);
        _widget.SetLyricsElsewhere(useBand, show);

        if (useBand && show && _bandRect is { } r)
        {
            // Slide in from the widget's side.
            double from = (r.Left + r.Right) / 2.0 < (_lastWidgetRect.Left + _lastWidgetRect.Right) / 2.0 ? 24 : -24;
            _band.Reveal(true, animate ? from : 0);
            Native.SetWindowPos(_band.Hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOACTIVATE);
        }
        else
        {
            _band.Reveal(false, 0);
        }
    }

    void Restage()
    {
        var target = ActiveLyrics;
        target.ShowMessage(_stagedTitle, _stagedArtist);
        if (_stagedLyrics != null) target.ShowLyrics(_stagedLyrics);
    }

    void ShowLyricsMessage(string title, string artist)
    {
        _stagedLyrics = null;
        _stagedTitle = title;
        _stagedArtist = artist;
        ActiveLyrics.ShowMessage(title, artist);
    }

    void ShowSyncedLyrics(SyncedLyrics lyrics)
    {
        _stagedLyrics = lyrics;
        ActiveLyrics.ShowLyrics(lyrics);
    }

    /// <summary>
    /// Picks the empty stretch of taskbar the widget lives in: by default the
    /// gap just left of the notification area, anchored to its right end.
    /// </summary>
    Native.RECT? WidgetRect(Native.RECT rc, double scale, out bool anchorRight)
    {
        anchorRight = _s.WidgetPlacement != WidgetPlacement.Left;
        if (!_tracker.HasScanned) return null;

        int margin = (int)Math.Round(10 * scale);
        int min = (int)Math.Round(140 * scale);
        int max = (int)Math.Round(_s.WidgetMaxWidth * scale);

        Span span;
        if (_tracker.Occupied.Count == 0)
        {
            // UI Automation gave nothing: fall back to the left third, free on a default Windows 11 taskbar.
            span = new Span(rc.Left, rc.Left + rc.Width * 3 / 10);
            anchorRight = false;
        }
        else
        {
            var free = _tracker.FreeSpans().Where(f => f.Width - 2 * margin >= min).ToList();
            if (free.Count == 0) return null;
            span = anchorRight ? free[^1] : free[0];
        }

        int width = Math.Min(span.Width - 2 * margin, max);
        if (width < min) return null;
        int x = anchorRight ? span.Right - margin - width : span.Left + margin;
        return new Native.RECT(x, rc.Top, x + width, rc.Bottom);
    }

    /// <summary>
    /// The taskbar jumps to the top of the topmost band whenever it is
    /// clicked; put the layer back under it and the widget back over it.
    /// </summary>
    void EnsureZOrder()
    {
        if (_disposed || !_layerShown || _tracker.Handle == IntPtr.Zero) return;
        var tray = _tracker.Handle;
        if (_styledOver)
        {
            if (!Native.IsAbove(_layer.Hwnd, tray)) Native.SetWindowPos(_layer.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        }
        else if (Native.GetWindow(tray, Native.GW_HWNDNEXT) != _layer.Hwnd)
        {
            Native.SetWindowPos(_layer.Hwnd, tray, 0, 0, 0, 0, KeepPlace);
        }

        if (_widgetShown && (!Native.IsAbove(_widget.Hwnd, tray) || !Native.IsAbove(_widget.Hwnd, _layer.Hwnd)))
        {
            Native.SetWindowPos(_widget.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        }
        if (_popup.IsOpen && !Native.IsAbove(_popup.Hwnd, tray))
            Native.SetWindowPos(_popup.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
        if (_band.IsRevealed && !Native.IsAbove(_band.Hwnd, tray))
            Native.SetWindowPos(_band.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, KeepPlace);
    }

    void Watch()
    {
        if (_media.HasSession) _noSessionSince = DateTime.MinValue;
        else if (_noSessionSince == DateTime.MinValue) _noSessionSince = DateTime.Now;

        bool idle = _s.HideWhenIdle && _noSessionSince != DateTime.MinValue
                    && (DateTime.Now - _noSessionSince).TotalSeconds > 2;
        bool fullscreen = _s.HideInFullscreen && IsForegroundFullscreen();
        // Listen to the speakers only while a music-driven style is actually on screen and playing.
        _audio.SetActive(_s.AudioReactive && _layerShown && _media.IsPlaying && SeekRenderer.UsesAudio(_s.SeekStyle));
        UpdateFramePace();
        if (idle != _idle || fullscreen != _fullscreen || (_layerShown && IsOverMode != _styledOver))
        {
            _idle = idle;
            _fullscreen = fullscreen;
            Reposition();
        }
    }

    bool IsForegroundFullscreen()
    {
        // The shell's own verdict (what makes the taskbar step aside for games and videos).
        if (Native.SHQueryUserNotificationState(out int state) == 0
            && state is Native.QUNS_BUSY or Native.QUNS_RUNNING_D3D_FULL_SCREEN or Native.QUNS_PRESENTATION_MODE)
            return true;

        var monitor = _tracker.Monitor;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || monitor == null) return false;
        Native.GetWindowThreadProcessId(fg, out var pid);
        if (pid == Environment.ProcessId) return false;
        // Start, Search and other shell surfaces are screen-sized but are not fullscreen apps.
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow") return false;
        if (Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONULL) != monitor.Handle) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var b = monitor.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    // ------------------------------------------------------------------ frame loop

    /// <summary>
    /// A paced timer instead of CompositionTarget.Rendering: subscribing to the
    /// latter keeps WPF composing at the display's refresh rate even when
    /// nothing changes. 30 Hz while something moves, 4 Hz otherwise.
    /// </summary>
    void SetRendering(bool on)
    {
        if (on == _rendering) return;
        _rendering = on;
        if (on)
        {
            UpdateFramePace();
            _frameTimer.Start();
        }
        else
        {
            _frameTimer.Stop();
        }
    }

    void UpdateFramePace()
    {
        bool moving = _media.IsPlaying && (_s.SeekStyle != SeekStyle.Classic || _s.LyricsExpanded);
        var interval = TimeSpan.FromMilliseconds(moving ? 33 : 250);
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var pos = _media.GetPosition();
        var dur = _media.Duration;
        bool timeline = _media.HasSession && dur > TimeSpan.Zero;
        double progress = timeline ? Math.Clamp((double)pos.Ticks / dur.Ticks, 0, 1) : 0;
        _layer.SetProgress(progress, timeline, _media.IsPlaying);
        _widget.SetProgress(progress);
        if (_popup.IsOpen) _popup.SetProgress(pos, dur);

        UpdateQuietZones();
        // Hidden lyrics need no karaoke updates; they catch up from the position when shown.
        if (!_s.LyricsExpanded) return;
        var lyricPos = pos + TimeSpan.FromMilliseconds(_s.LyricsOffsetMs) + (_lyricsResult?.Lyrics?.Offset ?? TimeSpan.Zero);
        ActiveLyrics.Update(lyricPos, _media.IsPlaying);
    }

    /// <summary>
    /// Tells the progress layer where lyrics are on screen so it can fade itself
    /// away underneath them; otherwise bright fills would wash the text out.
    /// </summary>
    void UpdateQuietZones()
    {
        _quietZones.Clear();
        (double Left, double Right)? target = null;
        if (_s.LyricsExpanded && _layerShown)
        {
            // Just the text, so the quiet zone is as long as the line being sung.
            var span = _usingBand ? _band.LyricsScreenSpan() : _widgetShown ? _widget.LyricsScreenSpan() : (0, 0);
            if (span.Right - span.Left > 4)
            {
                const double pad = 10;
                double origin = _tracker.Rect.Left, scale = _tracker.Scale;
                target = ((span.Left - origin) / scale - pad, (span.Right - origin) / scale + pad);
            }
        }

        if (target is { } t)
        {
            // Glide to each new line's width instead of jumping.
            _quietShown = _quietShown is { } s ? (Glide(s.Left, t.Left), Glide(s.Right, t.Right)) : t;
            _quietLostAt = DateTime.MinValue;
        }
        else if (_quietShown != null && _s.LyricsExpanded && _layerShown)
        {
            // Between two lines nothing may be drawn for a moment; keep the zone briefly.
            if (_quietLostAt == DateTime.MinValue) _quietLostAt = DateTime.Now;
            if (DateTime.Now - _quietLostAt > TimeSpan.FromSeconds(0.8)) _quietShown = null;
        }
        else
        {
            _quietShown = null;
        }
        if (_quietShown is { } shown) _quietZones.Add(shown);
        _layer.SetQuietZones(_quietZones);
    }

    static double Glide(double from, double to) => Math.Abs(to - from) < 0.5 ? to : from + (to - from) * 0.3;

    // ------------------------------------------------------------------ appearance

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.LayerMode) or nameof(AppSettings.WidgetPlacement) or nameof(AppSettings.WidgetMaxWidth):
                Reposition();
                break;
            case nameof(AppSettings.TransparentTaskbar):
                UpdateTransparency();
                Reposition();
                break;
            case nameof(AppSettings.ControlCenter):
                _controlCenter.Tick();
                break;
            case nameof(AppSettings.LyricsExpanded) or nameof(AppSettings.LyricsPlacement):
                UpdateLyricsSurface(animate: true);
                break;
            case nameof(AppSettings.ShowInfoPopup):
                if (!_s.ShowInfoPopup) _popup.Dismiss();
                break;
            case nameof(AppSettings.OnlineTrackInfo):
                LoadDetails();
                break;
            case nameof(AppSettings.SeekStyle):
                ApplyAppearance();
                Watch();
                break;
            case nameof(AppSettings.HideWhenIdle) or nameof(AppSettings.HideInFullscreen) or nameof(AppSettings.AudioReactive):
                Watch();
                break;
            case nameof(AppSettings.LyricsEnabled) or nameof(AppSettings.FetchOnline) or nameof(AppSettings.LyricsFolder):
                if (_media.HasSession)
                {
                    ShowTrackMessage();
                    LoadLyrics(force: false);
                }
                break;
            case nameof(AppSettings.PinnedAppId) or nameof(AppSettings.StartWithWindows)
                or nameof(AppSettings.ProxyUrl) or nameof(AppSettings.LyricsOffsetMs):
                break;
            default:
                ApplyAppearance();
                break;
        }
    }

    void ApplyAppearance()
    {
        double h = _styledHeight > 0 ? _styledHeight : 30;
        bool over = _styledHeight > 0 ? _styledOver : IsOverMode;
        _layer.ApplyStyle(_s, over, _styledEdgeTop);
        bool shadow = over || _s.Background == BarBackground.Transparent || _s.BackgroundOpacity < 0.45;
        _widget.ApplyStyle(_s, h, shadow);
        _band.ApplyStyle(_s, h);
        UpdateBadge();
        UpdateAccent(animate: false);
    }

    /// <summary>Colours and cover the settings previews should use, so they look like the real bar.</summary>
    public (Color Primary, Color Secondary, System.Windows.Media.Imaging.BitmapSource? Art)? PreviewPalette()
        => _media.HasSession && _media.Thumbnail != null ? (_currentPrimary, _currentSecondary, _media.Thumbnail) : null;

    void UpdateAccent(bool animate)
    {
        Color? app = _media.HasSession && _media.AppColor is { } c ? ColorUtil.ForDarkSurface(c) : null;
        Palette? art = _media.HasSession ? _artPalette : null;
        Color primary;
        Color secondary;
        switch (_s.AccentSource)
        {
            case AccentSource.AlbumArt when art is { } a:
                (primary, secondary) = (a.Primary, a.Secondary);
                break;
            case AccentSource.AlbumArt when app is { } ac:
                (primary, secondary) = (ac, ColorUtil.ShiftHue(ac, 28, -0.04));
                break;
            case AccentSource.App when app is { } ac:
                (primary, secondary) = (ac, ColorUtil.ShiftHue(ac, 28, -0.04));
                break;
            case AccentSource.App when art is { } a:
                (primary, secondary) = (a.Primary, a.Secondary);
                break;
            case AccentSource.Custom:
                primary = ColorUtil.TryParse(_s.CustomColor, out var custom) ? custom : ColorUtil.Brand;
                secondary = ColorUtil.ShiftHue(primary, 28, -0.04);
                break;
            default:
                primary = ColorUtil.SystemAccent();
                secondary = ColorUtil.ShiftHue(primary, 28, -0.04);
                break;
        }

        var d = animate ? TimeSpan.FromMilliseconds(900) : TimeSpan.Zero;
        if (d > TimeSpan.Zero)
        {
            _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(primary, d)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        else
        {
            _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _accentBrush.Color = primary;
        }
        _layer.SetColors(primary, secondary, animate);
        _currentPrimary = primary;
        _currentSecondary = secondary;
        _playGlyphBrush.Color = ColorUtil.Luminance(primary) > 0.42 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White;
        _widget.SetAccent(primary);
        _popup.SetAccent(primary);
        _controlCenter.SetAccent(primary);
        _widget.LyricsView.Accent = primary;
        _band.LyricsView.Accent = primary;
    }

    // ------------------------------------------------------------------ media state

    void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaService.IsPlaying):
                _widget.SetPlaying(_media.IsPlaying, animate: true);
                break;
            case nameof(MediaService.CanPlayPause) or nameof(MediaService.CanNext) or nameof(MediaService.CanPrevious):
                UpdateButtons();
                break;
            case nameof(MediaService.HasSession):
                OnSessionStateChanged();
                break;
            case nameof(MediaService.AppIcon) or nameof(MediaService.AppColor):
                UpdateBadge();
                UpdateAccent(animate: true);
                break;
        }
    }

    void OnSessionStateChanged()
    {
        UpdateButtons();
        UpdateBadge();
        _widget.SetPlaying(_media.IsPlaying, animate: false);
        if (!_media.HasSession) ShowIdle();
        Watch();
    }

    void UpdateButtons()
        => _widget.SetButtons(_media.HasSession && _media.CanPrevious, _media.HasSession && _media.CanPlayPause,
            _media.HasSession && _media.CanNext);

    void UpdateBadge() => _widget.SetBadge(_media.HasSession ? _media.AppIcon : null, _s.ShowAppIcon);

    void ShowIdle()
    {
        _lyricsCts?.Cancel();
        _detailsCts?.Cancel();
        _lyricsResult = null;
        _details = null;
        _lyricsStatus = LyricsStatus.None;
        _popup.Dismiss();
        ShowLyricsMessage(Loc.T("Msg_NothingPlaying"), "");
        _widget.SetArt(null);
        UpdateBadge();
        UpdateAccent(animate: true);
    }

    void OnTrackChanged()
    {
        if (!_media.HasSession)
        {
            ShowIdle();
            return;
        }
        ShowTrackMessage();
        LoadLyrics(force: false);
        LoadDetails();
        _layer.SetTrack($"{_media.AppId}|{_media.Artist}|{_media.Title}");
        if (_popup.IsOpen) _popup.SetContent(BuildCard());
    }

    /// <summary>Messages already on the bar are re-said in the new language; menus pick it up when next built.</summary>
    void OnLanguageChanged()
    {
        if (!_media.HasSession) ShowLyricsMessage(Loc.T("Msg_NothingPlaying"), "");
        else if (_lyricsStatus == LyricsStatus.Instrumental) ShowLyricsMessage(_media.Title, Loc.T("Msg_Instrumental"));
        if (_popup.IsOpen) _popup.SetContent(BuildCard());
        _controlCenter.RefreshText();
    }

    void ShowTrackMessage()
    {
        var title = _media.Title.Length > 0 ? _media.Title : _media.AppName;
        var artist = _media.Artist.Length > 0 ? _media.Artist : _media.Title.Length > 0 ? _media.AppName : "";
        ShowLyricsMessage(title, artist);
    }

    async void LoadLyrics(bool force, int attempt = 0)
    {
        _lyricsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _lyricsResult = null;
        _lyricsStatus = LyricsStatus.None;
        if (!_s.LyricsEnabled || !_media.HasSession || _media.Title.Length == 0) return;

        _lyricsStatus = LyricsStatus.Loading;
        if (force) ShowTrackMessage();
        try
        {
            // Let the player finish publishing duration/album before matching.
            await Task.Delay(force ? 0 : 450, cts.Token);
            var query = new TrackQuery(_media.Title, _media.Artist, _media.Album, _media.Duration, _media.AppId);
            var result = await _lyricsService.GetAsync(query, force, cts.Token);
            if (cts.IsCancellationRequested) return;

            _lyricsResult = result;
            _lyricsStatus = result.Status;
            if (result.Status == LyricsStatus.Synced && result.Lyrics != null)
                ShowSyncedLyrics(result.Lyrics);
            else if (result.Status == LyricsStatus.Instrumental)
                ShowLyricsMessage(_media.Title, Loc.T("Msg_Instrumental"));
            Log.Info($"Lyrics for '{_media.Artist} - {_media.Title}': {result.Status} ({result.Source})");

            // Network trouble or LRCLIB briefly down: try again while the same song keeps playing.
            if (result.Status == LyricsStatus.Error && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cts.Token);
                LoadLyrics(force: false, attempt + 1);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _lyricsStatus = LyricsStatus.Error;
            Log.Error("Loading lyrics failed", ex);
        }
    }

    /// <summary>Genre, year and album from the iTunes catalogue, for the song card.</summary>
    async void LoadDetails()
    {
        _detailsCts?.Cancel();
        _details = null;
        if (!_media.HasSession || _media.IsDemo || _media.Title.Length == 0) return;
        var cts = new CancellationTokenSource();
        _detailsCts = cts;
        try
        {
            await Task.Delay(800, cts.Token);
            var details = await _trackInfo.LookupAsync(_media.Artist, _media.Title, _media.Duration, cts.Token);
            if (cts.IsCancellationRequested) return;
            _details = details;
            if (_popup.IsOpen) _popup.SetContent(BuildCard());
        }
        catch (OperationCanceledException)
        {
        }
    }

    async void OnThumbnailChanged()
    {
        var bmp = _media.Thumbnail;
        _widget.SetArt(bmp);
        _layer.SetArt(bmp);
        UpdateBadge();
        if (_popup.IsOpen) _popup.SetContent(BuildCard());
        _artPalette = null;
        if (bmp != null)
        {
            var palette = await Task.Run(() => ColorExtractor.FromBitmap(bmp));
            if (!ReferenceEquals(bmp, _media.Thumbnail)) return;
            _artPalette = palette;
        }
        UpdateAccent(animate: true);
    }

    // ------------------------------------------------------------------ song card

    void ShowInfo()
    {
        if (!_s.ShowInfoPopup || !_media.HasSession || !_widgetShown || _tracker.Monitor == null) return;
        _popup.SetContent(BuildCard());
        _popup.SetProgress(_media.GetPosition(), _media.Duration);
        var anchor = _widget.CoverCenterOnScreen();
        bool above = !_tracker.AtTop;
        int edge = above ? _tracker.Rect.Top : _tracker.Rect.Bottom;
        _popup.ShowAt(anchor, edge, above, _tracker.Monitor.Bounds);
    }

    SongCard BuildCard()
    {
        var d = _details;
        var genres = (_media.Genres.Length > 0 ? _media.Genres : d?.Genre ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        string? track = _media.TrackNumber > 0
            ? _media.AlbumTrackCount > 0 ? Loc.T("Card_TrackOf", _media.TrackNumber, _media.AlbumTrackCount) : Loc.T("Card_Track", _media.TrackNumber)
            : null;
        var album = _media.Album.Length > 0 ? _media.Album : d?.Album ?? "";
        var artist = _media.Artist.Length > 0 ? _media.Artist : d?.Artist ?? "";
        return new SongCard(
            _media.Title.Length > 0 ? _media.Title : _media.AppName,
            artist, album, d?.Year, genres, track, _media.AppIcon, _media.Thumbnail);
    }

    // ------------------------------------------------------------------ context menu

    public void ShowMenu()
    {
        _popup.Dismiss();
        var menu = BuildMenu();
        menu.Placement = PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            // Without foreground the menu would not close when clicking elsewhere.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Native.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("BarContextMenu") };

        if (_media.HasSession)
        {
            var artist = _media.Artist.Length > 0 ? " — " + _media.Artist : "";
            menu.Items.Add(new MenuItem { Header = Shorten(_media.Title + artist, 52), IsEnabled = false, Icon = Glyph("") });
            if (!_media.IsDemo)
            {
                var open = new MenuItem { Header = Loc.T("Menu_OpenApp", _media.AppName), Icon = Glyph("") };
                open.Click += (_, _) => AppVisualResolver.Activate(_media.AppId);
                menu.Items.Add(open);
            }
            menu.Items.Add(new Separator());
        }

        var showLyrics = new MenuItem { Header = Loc.T("Menu_ShowLyrics"), Icon = Glyph(""), IsChecked = _s.LyricsExpanded };
        showLyrics.Click += (_, _) => _s.LyricsExpanded = !_s.LyricsExpanded;
        menu.Items.Add(showLyrics);

        var sources = new MenuItem { Header = Loc.T("Menu_Source"), Icon = Glyph("") };
        var auto = new MenuItem { Header = Loc.T("Menu_SourceAuto"), IsChecked = _s.PinnedAppId.Length == 0 };
        auto.Click += (_, _) => _s.PinnedAppId = "";
        sources.Items.Add(auto);
        var sessions = _media.GetSessions();
        if (sessions.Count > 0) sources.Items.Add(new Separator());
        foreach (var session in sessions)
        {
            var label = session.Title.Length > 0 ? $"{session.AppName} — {Shorten(session.Title, 36)}" : session.AppName;
            var item = new MenuItem
            {
                Header = label + (session.IsPlaying ? "   ▶" : ""),
                IsChecked = _s.PinnedAppId == session.AppId,
            };
            var id = session.AppId;
            item.Click += (_, _) => _s.PinnedAppId = id;
            sources.Items.Add(item);
        }
        menu.Items.Add(sources);

        var lyrics = new MenuItem { Header = Loc.T("Menu_Lyrics"), Icon = Glyph("") };
        lyrics.Items.Add(new MenuItem { Header = Loc.T("Menu_LyricsStatus", LyricsStatusText()), IsEnabled = false });
        lyrics.Items.Add(new Separator());
        var reload = new MenuItem { Header = Loc.T("Menu_Reload"), Icon = Glyph(""), IsEnabled = _media.HasSession && !_media.IsDemo };
        reload.Click += (_, _) => LoadLyrics(force: true);
        lyrics.Items.Add(reload);

        bool synced = _lyricsResult?.Lyrics != null;
        int offset = synced ? (int)_lyricsResult!.Lyrics!.Offset.TotalMilliseconds : 0;
        var earlier = new MenuItem { Header = Loc.T("Menu_Earlier"), Icon = Glyph(""), IsEnabled = synced };
        earlier.Click += (_, _) => NudgeOffset(+250);
        var later = new MenuItem { Header = Loc.T("Menu_Later"), Icon = Glyph(""), IsEnabled = synced };
        later.Click += (_, _) => NudgeOffset(-250);
        var reset = new MenuItem { Header = Loc.T("Menu_ResetOffset", offset.ToString("+0;-0;0")), IsEnabled = synced && offset != 0 };
        reset.Click += (_, _) => NudgeOffset(-offset);
        lyrics.Items.Add(earlier);
        lyrics.Items.Add(later);
        lyrics.Items.Add(reset);
        lyrics.Items.Add(new Separator());
        var folder = new MenuItem { Header = Loc.T("Menu_LyricsFolder"), Icon = Glyph("") };
        folder.Click += (_, _) => OpenLyricsFolder();
        lyrics.Items.Add(folder);
        menu.Items.Add(lyrics);

        var effects = new MenuItem { Header = Loc.T("Menu_LyricEffect"), Icon = Glyph("") };
        foreach (var option in SettingsOptions.LyricEffects)
        {
            var value = (LyricEffect)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.LyricEffect == value };
            item.Click += (_, _) =>
            {
                _s.LyricEffect = value;
                _widget.Dispatcher.BeginInvoke(PreviewLyrics, DispatcherPriority.Background);
            };
            effects.Items.Add(item);
        }
        menu.Items.Add(effects);

        var seek = new MenuItem { Header = Loc.T("Menu_SeekStyle"), Icon = Glyph("") };
        foreach (var option in SettingsOptions.SeekStyles)
        {
            var item = new MenuItem { Header = option.Label, IsChecked = _s.SeekStyle == option.Value };
            var value = option.Value;
            item.Click += (_, _) => _s.SeekStyle = value;
            seek.Items.Add(item);
        }
        menu.Items.Add(seek);

        var controls = new MenuItem { Header = Loc.T("Menu_ControlStyle"), Icon = Glyph("") };
        foreach (var option in SettingsOptions.ControlStyles)
        {
            var value = (ControlStyle)option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.ControlStyle == value };
            item.Click += (_, _) => _s.ControlStyle = value;
            controls.Items.Add(item);
        }
        menu.Items.Add(controls);

        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = Loc.T("Menu_Settings"), Icon = Glyph("") };
        settings.Click += (_, _) => App.Instance.ShowSettings();
        menu.Items.Add(settings);
        var exit = new MenuItem { Header = Loc.T("Menu_Exit"), Icon = Glyph("") };
        exit.Click += (_, _) => App.Instance.ExitApp();
        menu.Items.Add(exit);
        return menu;
    }

    static TextBlock Glyph(string glyph) => new() { Text = glyph };

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    string LyricsStatusText() => _lyricsStatus switch
    {
        LyricsStatus.Loading => Loc.T("LyrSt_Loading"),
        LyricsStatus.Synced => _lyricsResult?.Source switch
        {
            "LRCLIB" => Loc.T("LyrSt_Downloaded"),
            "demo" => Loc.T("LyrSt_Demo"),
            _ => Loc.T("LyrSt_Cached"),
        },
        LyricsStatus.PlainOnly => Loc.T("LyrSt_PlainOnly"),
        LyricsStatus.Instrumental => Loc.T("LyrSt_Instrumental"),
        LyricsStatus.NotFound => Loc.T("LyrSt_NotFound"),
        LyricsStatus.Error => Loc.T("LyrSt_Error"),
        _ => _s.LyricsEnabled ? Loc.T("LyrSt_None") : Loc.T("LyrSt_Off"),
    };

    void NudgeOffset(int deltaMs)
    {
        if (_lyricsResult?.Lyrics is not { } lyrics) return;
        _lyricsService.SaveOffset(_lyricsResult, (int)lyrics.Offset.TotalMilliseconds + deltaMs);
    }

    void OpenLyricsFolder()
    {
        try
        {
            Directory.CreateDirectory(_lyricsService.Folder);
            var file = _lyricsResult?.FilePath;
            var args = file != null && File.Exists(file) ? $"/select,\"{file}\"" : $"\"{_lyricsService.Folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Opening lyrics folder failed", ex);
        }
    }
}
