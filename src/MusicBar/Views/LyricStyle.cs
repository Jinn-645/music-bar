using System.Windows.Media;
using System.Windows.Media.Effects;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>The lyric text look the user picked: text color and strength, and the stroke around it.</summary>
internal static class LyricStyle
{
    /// <summary>
    /// Colors the presenter's text and sets up <paramref name="stroke"/>; returns the effect to
    /// put on the lyrics' container (null when the stroke is off).
    /// </summary>
    public static Effect? Apply(AppSettings s, ILyricsView view, DropShadowEffect stroke)
    {
        view.SetTextStyle(ColorUtil.TryParse(s.LyricColor, out var text) ? text : Colors.White, s.LyricOpacity);
        if (!s.LyricStroke) return null;
        // A zero-offset shadow is a halo around every glyph: a small radius reads as an outline,
        // a large one as a soft glow.
        stroke.Color = ColorUtil.TryParse(s.LyricStrokeColor, out var c) ? c : Colors.Black;
        stroke.Opacity = s.LyricStrokeOpacity;
        stroke.BlurRadius = s.LyricStrokeRadius;
        return stroke;
    }
}
