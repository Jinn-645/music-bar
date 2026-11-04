using System;
using System.Collections.Generic;

namespace MusicBar.Models;

public sealed class LyricLine
{
    public LyricLine(TimeSpan start, string text)
    {
        Start = start;
        Text = text;
    }

    public TimeSpan Start { get; }
    public TimeSpan End { get; internal set; }
    public string Text { get; }
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);
}

public sealed class SyncedLyrics
{
    public SyncedLyrics(IReadOnlyList<LyricLine> lines) => Lines = lines;

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>LRC [offset:] semantics: positive values make lyrics appear earlier.</summary>
    public TimeSpan Offset { get; set; }

    public bool IsInstrumental { get; init; }

    /// <summary>Index of the last line whose start is at or before <paramref name="t"/>; -1 before the first line.</summary>
    public int IndexAt(TimeSpan t)
    {
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Start <= t)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }
}

public enum LyricsStatus { None, Loading, Synced, PlainOnly, Instrumental, NotFound, Error }

public sealed record LyricsResult(LyricsStatus Status, SyncedLyrics? Lyrics, string Source, string? FilePath);

public sealed record TrackQuery(string Title, string Artist, string Album, TimeSpan Duration, string AppId);
