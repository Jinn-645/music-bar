using System.Windows.Media;
using System.Windows.Media.Effects;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>The lyric text look the user picked: text color and strength, and the stroke around it.</summary>
internal static class LyricStyle
{
    /// <summary>Text on a light taskbar, in place of the default white.</summary>
    public static readonly Color Ink = Color.FromRgb(0x15, 0x17, 0x1E);

    /// <summary>
    /// Colors the presenter's text and sets up <paramref name="stroke"/>; returns the effect to
    /// put on the lyrics' container (null when the stroke is off). On a light taskbar the default
    /// white text and black outline swap round; colors the user picked stay as they are.
    /// </summary>
    public static Effect? Apply(AppSettings s, ILyricsView view, DropShadowEffect stroke, bool lightBar)
    {
        var text = ColorUtil.TryParse(s.LyricColor, out var t) ? t : Colors.White;
        if (lightBar && text == Colors.White) text = Ink;
        view.SetTextStyle(text, s.LyricOpacity);
        if (!s.LyricStroke) return null;
        // A zero-offset shadow is a halo around every glyph: a small radius reads as an outline,
        // a large one as a soft glow.
        var halo = ColorUtil.TryParse(s.LyricStrokeColor, out var c) ? c : Colors.Black;
        stroke.Color = lightBar && halo == Colors.Black ? Colors.White : halo;
        stroke.Opacity = s.LyricStrokeOpacity;
        stroke.BlurRadius = s.LyricStrokeRadius;
        return stroke;
    }
}
