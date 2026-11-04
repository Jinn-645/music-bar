using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>
/// Frameless, click-through strip that shows the lyrics in the biggest empty
/// stretch of the taskbar, wherever the taskbar's icons happen to sit.
/// </summary>
public partial class LyricsBandWindow : Window
{
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    readonly DropShadowEffect _lyricStroke = new() { ShadowDepth = 0 };
    int _token;

    public LyricsBandWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                Native.WS_EX_APPWINDOW);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public bool IsRevealed { get; private set; }

    /// <summary>Screen pixels covered by the lyric text in the strip; empty when hidden.</summary>
    public (double Left, double Right) LyricsScreenSpan()
        => IsRevealed && IsVisible && Root.Opacity > 0.05 ? LyricsView.TextScreenSpan() : (0, 0);

    public void ApplyStyle(AppSettings s, double h)
    {
        FontFamily = BundledFonts.Resolve(s.FontFamily);
        Root.Effect = LyricStyle.Apply(s, LyricsView, _lyricStroke);
        bool twoLine = s.ShowNextLine && h >= 40;
        double main = (twoLine ? Math.Clamp(h * 0.30, 12, 15) : Math.Clamp(h * 0.45, 12, 16)) * s.LyricScale;
        double sub = Math.Clamp(h * 0.22, 10, 12) * s.LyricScale;
        LyricsView.Configure(s.LyricEffect, s.Karaoke, twoLine, FontFamily,
            Math.Round(main * 2) / 2, Math.Round(sub * 2) / 2, HorizontalAlignment.Center, s.AnimationSpeed);
    }

    /// <summary>Fades the strip in (sliding from the widget's side) or out, then hides the window.</summary>
    public void Reveal(bool show, double fromX)
    {
        if (show == IsRevealed) return;
        IsRevealed = show;
        int token = ++_token;
        if (show)
        {
            if (!IsVisible) Show();
            var d = TimeSpan.FromMilliseconds(380);
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, d) { EasingFunction = EaseOut });
            RootShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, d) { EasingFunction = EaseOut });
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (token == _token) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
