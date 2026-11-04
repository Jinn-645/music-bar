using System.Collections.Generic;
using System.Linq;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>Korean, Japanese and Chinese lyrics in Latin letters, for the bar and the lyrics panel.</summary>
public static class LyricRomanization
{
    /// <summary>
    /// Each line in Latin letters (null for a line with nothing to romanize), or null when the whole
    /// song has nothing to romanize. Slow for a long song the first time: call it off the UI thread.
    /// </summary>
    public static string?[]? ForLines(IReadOnlyList<string> lines)
    {
        var script = Romanizer.Detect(lines);
        if (script == LyricScript.None) return null;
        var roman = lines.Select(l => Romanizer.Romanize(l, script)).ToArray();
        return roman.Any(r => r != null) ? roman : null;
    }

    /// <summary>
    /// Kanji left as written: Windows reads them only with Japanese installed, and a line half in
    /// letters, half in kanji is no better than the original in its place.
    /// </summary>
    static bool Partial(string roman) => roman.Any(c => c is >= '㐀' and <= '鿿');

    /// <summary>
    /// The lyrics as the bar should show them: unchanged, each line carrying its romanization to
    /// show under it, or the romanization in place of the line. A vertical taskbar's word stack
    /// has no room for a second line, so it only takes the replacement.
    /// </summary>
    public static SyncedLyrics ForBar(SyncedLyrics lyrics, Romanization mode, bool vertical)
    {
        if (mode == Romanization.Off) return lyrics;
        var roman = ForLines(lyrics.Lines.Select(l => l.Text).ToList());
        if (roman == null) return lyrics;
        bool below = mode == Romanization.Below && !vertical;
        var lines = new List<LyricLine>(lyrics.Lines.Count);
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var r = roman[i];
            if (!below && r != null && Partial(r)) r = null;
            // The original line keeps its word times; a romanized one has other words.
            var copy = below || r == null
                ? new LyricLine(line.Start, line.Text) { Sub = below ? r ?? "" : null, Words = line.Words }
                : new LyricLine(line.Start, r);
            copy.End = line.End;
            lines.Add(copy);
        }
        return new SyncedLyrics(lines) { Offset = lyrics.Offset, IsInstrumental = lyrics.IsInstrumental, HasSubs = below };
    }
}
