using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views.Face;

/// <summary>
/// The face's own little window on the taskbar. It never takes focus, runs the face on a
/// paced clock (slower while it sleeps), tells it about the music, the clock, the pointer and
/// whether the user is around, and offers a menu on right-click.
/// </summary>
public sealed class FaceWindow : Window
{
    static readonly TimeSpan Lively = TimeSpan.FromMilliseconds(33);
    static readonly TimeSpan Sleeping = TimeSpan.FromMilliseconds(80);

    readonly AppSettings _s;
    readonly MediaService _media;
    readonly AudioMeter _audio;
    readonly FaceView _view = new(Environment.TickCount);
    readonly DropShadowEffect _shadow = new() { ShadowDepth = 0, BlurRadius = 5, Opacity = 0.55, Color = Colors.Black };
    readonly DispatcherTimer _clock = new(DispatcherPriority.Render) { Interval = Lively };

    public FaceWindow(AppSettings settings, MediaService media, AudioMeter audio)
    {
        _s = settings;
        _media = media;
        _audio = audio;
        Title = "MusicBar Face";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Left = Top = -20000;
        Width = 64;
        Height = 48;
        UseLayoutRounding = true;
        _view.Effect = _shadow;
        Content = _view;

        _clock.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _clock.Start();
            else
            {
                _clock.Stop();
                _audio.Want(this, false);
            }
        };
        _media.TrackChanged += OnTrackChanged;
        MouseRightButtonUp += (_, e) =>
        {
            ShowMenu();
            e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(Hwnd).AddHook(NoActivate);
        };
        Closed += (_, _) =>
        {
            _media.TrackChanged -= OnTrackChanged;
            _audio.Want(this, false);
        };
    }

    public IntPtr Hwnd { get; private set; }

    void OnTrackChanged(object? sender, EventArgs e) => _view.Brain.TrackChanged(_view.Now);

    static IntPtr NoActivate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    /// <param name="lightBar">The taskbar behind is light: lines go dark and the halo light.</param>
    public void ApplyStyle(AppSettings s, bool lightBar)
    {
        _view.Look = s.FaceStyle;
        _view.Ink = lightBar ? LyricStyle.Ink : Color.FromRgb(0xFF, 0xFD, 0xF6);
        _shadow.Color = lightBar ? Colors.White : Colors.Black;
        _view.Brain.FollowsClock = s.FaceFollowsClock;
        _view.Brain.ReactsToMusic = s.FaceReactsToMusic;
    }

    public void SetAccent(Color accent) => _view.Accent = accent;

    void Tick()
    {
        bool playing = _media.HasSession && _media.IsPlaying;
        _audio.Want(this, _s.FaceReactsToMusic && playing);
        double level = _audio.IsCapturing ? _audio.Level : -1;

        Vector? pointer = null;
        if (_s.FaceWatchesPointer && Native.GetCursorPos(out var p) && PresentationSource.FromVisual(_view) != null)
        {
            var local = _view.PointFromScreen(new Point(p.X, p.Y));
            var from = local - new Point(_view.ActualWidth / 2, _view.ActualHeight * 0.45);
            var d = from / Math.Max(1, _view.ActualWidth);
            // Only a pointer within a few face widths is worth watching.
            if (d.Length < 5) pointer = d;
        }

        _view.Step(new FaceSenses(DateTime.Now, _media.HasSession, playing, level, Native.UserIdle(), pointer, _view.IsMouseOver));
        var pace = _view.Brain.Calm && !_view.IsMouseOver ? Sleeping : Lively;
        if (_clock.Interval != pace) _clock.Interval = pace;
    }

    void ShowMenu()
    {
        var menu = new ContextMenu { Style = Application.Current?.TryFindResource("BarContextMenu") as Style };
        foreach (var option in SettingsOptions.FaceStyles)
        {
            var value = option.Value;
            var item = new MenuItem { Header = option.Label, IsChecked = _s.FaceStyle == value };
            item.Click += (_, _) => _s.FaceStyle = value;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var hide = new MenuItem { Header = Loc.T("Face_Hide") };
        hide.Click += (_, _) => _s.FaceEnabled = false;
        menu.Items.Add(hide);
        var settings = new MenuItem { Header = Loc.T("Face_Settings") };
        settings.Click += (_, _) => (Application.Current as App)?.ShowSettings(SettingsWindow.FacePage);
        menu.Items.Add(settings);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Opened += (_, _) =>
        {
            // Without foreground the menu would not close when clicking elsewhere.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Native.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }
}
