using System;
using System.Windows;
using System.Windows.Media;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>
/// What the bar asks of a lyrics display: the line-by-line presenter on a horizontal
/// taskbar (<see cref="LyricsPresenter"/>), the word-per-row column on a vertical one
/// (<see cref="WordStackPresenter"/>).
/// </summary>
public interface ILyricsView
{
    bool HasLyrics { get; }

    Color Accent { get; set; }

    /// <summary>The lyric text color and how solid it is.</summary>
    void SetTextStyle(Color color, double opacity);

    void Configure(LyricEffect effect, bool karaoke, bool twoLine, FontFamily font,
        double mainSize, double subSize, HorizontalAlignment align, double speed);

    /// <summary>A message instead of lyrics (the track title and artist, "nothing playing").</summary>
    void ShowMessage(string primary, string? secondary);

    void ShowLyrics(SyncedLyrics lyrics);

    /// <summary>Replays the entrance of what is on screen (the settings window's preview).</summary>
    void Preview();

    /// <summary>Every frame, with the playback position the lyrics are synced to.</summary>
    void Update(TimeSpan position, bool playing);

    /// <summary>Where text is drawn, in the element's coordinates, or null when nothing shows.</summary>
    Rect? TextBounds();

    /// <summary>
    /// The stretch of the taskbar the text covers, in physical screen pixels along the
    /// taskbar's length (x on a horizontal taskbar, y on a vertical one), or (0, 0).
    /// </summary>
    (double Start, double End) TextScreenSpan();
}
