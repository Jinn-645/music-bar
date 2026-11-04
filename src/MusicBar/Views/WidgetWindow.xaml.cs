using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// A capsule on top of the taskbar: album cover and transport buttons, with
/// a lyrics panel that slides open beside the cover when the cover is clicked.
/// Never takes focus away from other windows.
/// </summary>
public partial class WidgetWindow : Window
{
    static readonly TimeSpan InfoDelay = TimeSpan.FromMilliseconds(650);
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });

    readonly MediaService _media;
    readonly DropShadowEffect _textShadow = new() { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
    readonly DropShadowEffect _lyricStroke = new() { ShadowDepth = 0 };
    readonly DropShadowEffect _playGlow = new() { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.6 };
    readonly DispatcherTimer _infoTimer = new() { Interval = InfoDelay };
    readonly SolidColorBrush _accentBrush;
    readonly SolidColorBrush _playGlyphBrush;
    // The record's label (Vinyl style): the current cover.
    readonly ImageBrush _labelBrush = new() { Stretch = Stretch.UniformToFill };
    readonly RotateTransform _spin = new();
    static readonly Brush CapsuleFill = Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0x16, 0x17, 0x1C)));
    static readonly Brush CapsuleRim = Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));

    ControlStyle _controlStyle = ControlStyle.Glow;
    bool _spinning;

    bool _artFrontIsA;
    BitmapSource? _shownArt;
    bool _expanded;
    bool _anchorRight = true;
    bool _playing;
    bool _lyricsElsewhere;
    bool _lyricsVisibleElsewhere;

    public WidgetWindow(MediaService media, SolidColorBrush accentBrush, SolidColorBrush playGlyphBrush)
    {
        _media = media;
        _accentBrush = accentBrush;
        _playGlyphBrush = playGlyphBrush;
        RenderOptions.SetBitmapScalingMode(_labelBrush, BitmapScalingMode.HighQuality);
        InitializeComponent();
        PlayButton.Background = accentBrush;
        PlayButton.Effect = _playGlow;
        PlayGlyph.Fill = PauseGlyph.Fill = playGlyphBrush;
        _infoTimer.Tick += (_, _) =>
        {
            _infoTimer.Stop();
            InfoRequested?.Invoke();
        };
        SizeChanged += (_, _) => FitLyrics(animate: false);
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public event Action? MenuRequested;

    /// <summary>The pointer rested on the cover long enough to show the song card.</summary>
    public event Action? InfoRequested;

    public event Action? InfoDismissed;

    public event Action? ExpandToggled;

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    // ------------------------------------------------------------------ layout

    public void ApplyStyle(AppSettings s, double h, bool textShadow)
    {
        FontFamily = BundledFonts.Resolve(s.FontFamily);
        LyricsHost.Effect = LyricStyle.Apply(s, LyricsView, _lyricStroke);

        double pill = Math.Max(16, h - 4);
        Pill.CornerRadius = new CornerRadius(pill / 2);

        double art = Math.Clamp(Math.Round(h * 0.7), 18, 34);
        ArtHost.Width = ArtHost.Height = art;
        var radius = new CornerRadius(Math.Round(art * 0.24));
        ArtPlaceholder.CornerRadius = ArtA.CornerRadius = ArtB.CornerRadius = ArtStroke.CornerRadius = ArtOverlay.CornerRadius = radius;
        AppBadge.Width = AppBadge.Height = Math.Round(art * 0.55);
        ArtOverlayGlyph.FontSize = Math.Round(art * 0.45);

        ApplyControlStyle(s.ControlStyle, Math.Clamp(Math.Round(h * 0.8), 22, 34), textShadow);

        CoverButton.Visibility = s.ShowAlbumArt ? Visibility.Visible : Visibility.Collapsed;
        ControlsShell.Visibility = s.ShowControls ? Visibility.Visible : Visibility.Collapsed;

        bool twoLine = s.ShowNextLine && h >= 40;
        double main = (twoLine ? Math.Clamp(h * 0.30, 12, 15) : Math.Clamp(h * 0.42, 12, 15)) * s.LyricScale;
        double sub = Math.Clamp(h * 0.22, 10, 12) * s.LyricScale;
        LyricsView.Configure(s.LyricEffect, s.Karaoke, twoLine, FontFamily,
            Math.Round(main * 2) / 2, Math.Round(sub * 2) / 2,
            s.LyricsAlign == LyricsAlign.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            s.AnimationSpeed);
        UpdateOverlayGlyph();
    }

    /// <summary>
    /// Dresses the previous / play / next buttons in one of the control styles: picks their
    /// templates (Themes/Bar.xaml), sizes them around the taskbar's button size, and colours the
    /// glyphs for what sits behind them.
    /// </summary>
    void ApplyControlStyle(ControlStyle style, double button, bool textShadow)
    {
        _controlStyle = style;
        var (side, play) = style switch
        {
            ControlStyle.Minimal => ("TransportMinimal", "TransportMinimal"),
            ControlStyle.Capsule => ("TransportSegment", "PlayCapsule"),
            ControlStyle.Ring => ("TransportButton", "PlayRing"),
            ControlStyle.Vinyl => ("TransportButton", "PlayVinyl"),
            ControlStyle.Keys => ("KeyButton", "KeyButton"),
            ControlStyle.Bubbles => ("BubbleButton", "BubbleButton"),
            _ => ("TransportButton", "PlayButton"),
        };
        PrevButton.Style = NextButton.Style = (Style)FindResource(side);
        PlayButton.Style = (Style)FindResource(play);

        // Sizes as multiples of the taskbar's button size: (side w, side h, play w, play h, side glyph, play glyph, gap).
        var (sw, sh, pw, ph, sg, pg, gap) = style switch
        {
            ControlStyle.Minimal => (1.0, 1.0, 1.0, 1.0, 0.6, 0.72, 1.0),
            ControlStyle.Capsule => (0.92, 0.8, 1.38, 0.8, 0.5, 0.46, 1.0),
            ControlStyle.Ring => (1.0, 1.0, 1.0, 1.0, 0.6, 0.42, 3.0),
            ControlStyle.Vinyl => (1.0, 1.0, 1.1, 1.1, 0.6, 0.36, 3.0),
            ControlStyle.Keys => (1.0, 0.86, 1.06, 0.86, 0.46, 0.42, 2.0),
            ControlStyle.Bubbles => (0.82, 0.82, 1.0, 1.0, 0.44, 0.48, 3.5),
            _ => (1.0, 1.0, 1.0, 1.0, 0.6, 0.56, 3.0),
        };
        PrevButton.Width = NextButton.Width = Math.Round(button * sw);
        PrevButton.Height = NextButton.Height = Math.Round(button * sh);
        PlayButton.Width = Math.Round(button * pw);
        PlayButton.Height = Math.Round(button * ph);
        PrevIcon.Width = PrevIcon.Height = NextIcon.Width = NextIcon.Height = Math.Round(button * sg);
        PlayIconBox.Width = PlayIconBox.Height = Math.Round(button * pg);
        PlayButton.Margin = new Thickness(gap, 0, gap, 0);

        // Background carries the accent everywhere (bubble tint, key lamp, ring arc), except on
        // the record, whose label is the cover.
        PrevButton.Background = NextButton.Background = _accentBrush;
        PlayButton.Background = style == ControlStyle.Vinyl && _labelBrush.ImageSource != null ? _labelBrush : _accentBrush;
        PlayGlyph.Fill = PauseGlyph.Fill = style switch
        {
            ControlStyle.Glow or ControlStyle.Capsule => _playGlyphBrush,
            ControlStyle.Minimal => _accentBrush,
            _ => Brushes.White,
        };
        Transport.SetLamp(PlayButton, style == ControlStyle.Keys);

        // Bare glyphs need the shadow to read over a transparent taskbar; drawn buttons carry their own contrast.
        bool bare = style is ControlStyle.Glow or ControlStyle.Minimal or ControlStyle.Ring or ControlStyle.Vinyl;
        PrevButton.Effect = NextButton.Effect = bare && textShadow ? _textShadow : null;
        PlayButton.Effect = style switch
        {
            ControlStyle.Glow => _playGlow,
            ControlStyle.Minimal when textShadow => _textShadow,
            _ => null,
        };
        _playGlow.BlurRadius = Math.Round(button * 0.45);

        bool capsule = style == ControlStyle.Capsule;
        ControlsShell.Background = capsule ? CapsuleFill : null;
        ControlsShell.BorderBrush = capsule ? CapsuleRim : null;
        ControlsShell.BorderThickness = new Thickness(capsule ? 0.8 : 0);
        ControlsShell.Padding = new Thickness(capsule ? 2 : 0);
        ControlsShell.CornerRadius = new CornerRadius(capsule ? (PlayButton.Height + 5.6) / 2 : 0);

        PlayButton.ApplyTemplate();
        if (PlayButton.Template.FindName("Disc", PlayButton) is FrameworkElement disc) disc.RenderTransform = _spin;
        _spinning = false;
        UpdateSpin();
    }

    /// <summary>The record turns at 33⅓ rpm while music plays and stops where it is when paused.</summary>
    void UpdateSpin()
    {
        bool spin = _controlStyle == ControlStyle.Vinyl && _playing;
        if (spin == _spinning) return;
        _spinning = spin;
        double angle = _spin.Angle % 360;
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        _spin.Angle = angle;
        if (!spin) return;
        var turn = new DoubleAnimation(angle, angle + 360, TimeSpan.FromSeconds(1.8)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(turn, 30);
        _spin.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    /// <summary>The song's progress, for the ring around play (0 when there is no timeline).</summary>
    public void SetProgress(double progress)
    {
        if (_controlStyle != ControlStyle.Ring) return;
        double shown = Transport.GetProgress(PlayButton);
        if (Math.Abs(progress - shown) >= 0.0015 || (progress == 0) != (shown == 0)) Transport.SetProgress(PlayButton, progress);
    }

    /// <summary>Right-anchored widgets open their lyrics to the left of the cover, left-anchored ones to the right.</summary>
    public void SetAnchor(bool right)
    {
        if (right == _anchorRight) return;
        _anchorRight = right;
        Pill.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Grid.SetColumn(LyricsHost, right ? 0 : 2);
        Grid.SetColumn(ControlsShell, right ? 2 : 0);
        UpdateOverlayGlyph();
    }

    public void SetExpanded(bool expanded, bool animate)
    {
        _expanded = expanded;
        FitLyrics(animate);
        UpdateOverlayGlyph();
    }

    void FitLyrics(bool animate)
    {
        double target = 0;
        if (_expanded)
        {
            double fixedPart = CoverButton.ActualWidth + ControlsShell.ActualWidth + 8;
            target = Math.Max(0, ActualWidth - fixedPart);
        }
        if (!animate)
        {
            LyricsHost.BeginAnimation(WidthProperty, null);
            LyricsHost.BeginAnimation(OpacityProperty, null);
            LyricsHost.Width = target;
            LyricsHost.Opacity = _expanded ? 1 : 0;
            return;
        }
        var d = TimeSpan.FromMilliseconds(_expanded ? 380 : 280);
        LyricsHost.BeginAnimation(WidthProperty, new DoubleAnimation(target, d) { EasingFunction = EaseOut });
        LyricsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(_expanded ? 1 : 0, TimeSpan.FromMilliseconds(_expanded ? 420 : 160))
        {
            BeginTime = _expanded ? TimeSpan.FromMilliseconds(90) : TimeSpan.Zero,
        });
    }

    /// <summary>
    /// Lyrics shown elsewhere on the taskbar (not beside the cover): the cover
    /// toggles them, so its hover glyph becomes a lyrics on/off sign.
    /// </summary>
    public void SetLyricsElsewhere(bool elsewhere, bool lyricsVisible)
    {
        _lyricsElsewhere = elsewhere;
        _lyricsVisibleElsewhere = lyricsVisible;
        UpdateOverlayGlyph();
    }

    void UpdateOverlayGlyph()
    {
        if (_lyricsElsewhere)
        {
            ArtOverlayGlyph.Text = _lyricsVisibleElsewhere ? "" : "";
            return;
        }
        // Chevron points the way the lyrics panel will move.
        bool opensLeft = _anchorRight != _expanded;
        ArtOverlayGlyph.Text = opensLeft ? "" : "";
    }

    // ------------------------------------------------------------------ content

    public void SetAccent(Color accent) => _playGlow.Color = accent;

    /// <summary>
    /// Screen pixels covered by the lyric text beside the cover (just the text, so it
    /// follows each line's length); empty when the panel is closed.
    /// </summary>
    public (double Left, double Right) LyricsScreenSpan()
    {
        if (!IsVisible || LyricsHost.ActualWidth < 2 || LyricsHost.Opacity < 0.05) return (0, 0);
        double hostLeft = LyricsHost.PointToScreen(new Point(0, 0)).X;
        double hostRight = LyricsHost.PointToScreen(new Point(LyricsHost.ActualWidth, 0)).X;
        var (left, right) = LyricsView.TextScreenSpan();
        if (right - left < 1) return (0, 0);
        return (Math.Max(hostLeft, left), Math.Min(hostRight, right));
    }

    public Point CoverCenterOnScreen()
    {
        var center = new Point(CoverButton.ActualWidth / 2, CoverButton.ActualHeight / 2);
        return CoverButton.IsVisible ? CoverButton.PointToScreen(center) : Pill.PointToScreen(new Point(Pill.ActualWidth / 2, 0));
    }

    public void SetArt(BitmapSource? bmp)
    {
        if (ReferenceEquals(bmp, _shownArt)) return;
        _shownArt = bmp;
        _labelBrush.ImageSource = bmp;
        if (_controlStyle == ControlStyle.Vinyl) PlayButton.Background = bmp != null ? _labelBrush : _accentBrush;
        var d = TimeSpan.FromMilliseconds(450);
        var front = _artFrontIsA ? ArtA : ArtB;
        var back = _artFrontIsA ? ArtB : ArtA;
        var backBrush = _artFrontIsA ? ArtBrushB : ArtBrushA;
        if (bmp == null)
        {
            Fade(front, 0, d);
            Fade(back, 0, d);
            return;
        }
        backBrush.ImageSource = bmp;
        Fade(back, 1, d);
        Fade(front, 0, d);
        _artFrontIsA = !_artFrontIsA;
    }

    public void SetBadge(ImageSource? appIcon, bool showBadge)
    {
        bool hasArt = _shownArt != null;
        ArtAppIcon.Source = hasArt ? null : appIcon;
        ArtGlyph.Visibility = !hasArt && appIcon == null ? Visibility.Visible : Visibility.Collapsed;
        AppBadgeImage.Source = appIcon;
        AppBadge.Visibility = showBadge && hasArt && appIcon != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Morphs between the play and pause glyphs.</summary>
    public void SetPlaying(bool playing, bool animate)
    {
        if (playing == _playing && animate) return;
        _playing = playing;
        Transport.SetIsPlaying(PlayButton, playing);
        UpdateSpin();
        var show = playing ? PauseGlyph : PlayGlyph;
        var hide = playing ? PlayGlyph : PauseGlyph;
        if (!animate)
        {
            SetGlyph(show, 1, 1);
            SetGlyph(hide, 0, 0.4);
            return;
        }
        var outD = TimeSpan.FromMilliseconds(130);
        var inD = TimeSpan.FromMilliseconds(320);
        hide.BeginAnimation(OpacityProperty, new DoubleAnimation(0, outD));
        ScaleOf(hide).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, outD));
        ScaleOf(hide).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, outD));
        show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, inD) { BeginTime = outD / 2 });
        ScaleOf(show).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, 1, inD) { BeginTime = outD / 2, EasingFunction = Spring });
        ScaleOf(show).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, inD) { BeginTime = outD / 2, EasingFunction = Spring });
    }

    static ScaleTransform ScaleOf(UIElement e) => (ScaleTransform)e.RenderTransform;

    static void SetGlyph(UIElement glyph, double opacity, double scale)
    {
        glyph.BeginAnimation(OpacityProperty, null);
        glyph.Opacity = opacity;
        var s = ScaleOf(glyph);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        s.ScaleX = s.ScaleY = scale;
    }

    public void SetButtons(bool previous, bool playPause, bool next)
    {
        PrevButton.IsEnabled = previous;
        PlayButton.IsEnabled = playPause;
        NextButton.IsEnabled = next;
    }

    static void Fade(UIElement element, double to, TimeSpan duration)
        => element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, duration) { EasingFunction = EaseOut });

    /// <summary>Short push in the direction of the skip, then back.</summary>
    static void Nudge(TranslateTransform shift, double dx)
        => shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromMilliseconds(110))
        {
            AutoReverse = true,
            EasingFunction = EaseOut,
        });

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    // ------------------------------------------------------------------ input

    void Pill_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CancelInfo();
        MenuRequested?.Invoke();
        e.Handled = true;
    }

    void Cover_MouseEnter(object sender, MouseEventArgs e)
    {
        var d = TimeSpan.FromMilliseconds(220);
        ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.1, d) { EasingFunction = Spring });
        ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.1, d) { EasingFunction = Spring });
        Fade(ArtOverlay, 1, TimeSpan.FromMilliseconds(160));
        _infoTimer.Stop();
        _infoTimer.Start();
    }

    void Cover_MouseLeave(object sender, MouseEventArgs e)
    {
        var d = TimeSpan.FromMilliseconds(220);
        ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, d) { EasingFunction = EaseOut });
        ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, d) { EasingFunction = EaseOut });
        Fade(ArtOverlay, 0, TimeSpan.FromMilliseconds(200));
        CancelInfo();
    }

    void CancelInfo()
    {
        _infoTimer.Stop();
        InfoDismissed?.Invoke();
    }

    void Cover_Click(object sender, RoutedEventArgs e)
    {
        CancelInfo();
        ExpandToggled?.Invoke();
    }

    void Play_Click(object sender, RoutedEventArgs e) => _ = _media.PlayPauseAsync();

    void Next_Click(object sender, RoutedEventArgs e)
    {
        Nudge(NextShift, 3.5);
        _ = _media.NextAsync();
    }

    void Prev_Click(object sender, RoutedEventArgs e)
    {
        Nudge(PrevShift, -3.5);
        _ = _media.PreviousAsync();
    }
}
