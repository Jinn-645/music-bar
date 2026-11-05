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

    /// <summary>A second line shown under this one instead of the next line (its romanization).</summary>
    public string? Sub { get; init; }

    /// <summary>When each word starts, for lyrics someone synced word by word; null for line timing only.</summary>
    public IReadOnlyList<LyricWord>? Words { get; init; }

    /// <summary>
    /// How much of the line is sung at <paramref name="position"/> by its words' own times, 0 to 1 as
    /// a share of its letters: the words already reached, and the part of the current one its time
    /// says. Null when the line has no word timing (the singing is then estimated from the line's length).
    /// </summary>
    public double? WordProgress(TimeSpan position)
    {
        if (Words is not { Count: > 0 } words) return null;
        double total = 0, sung = 0;
        foreach (var w in words) total += w.Text.Length;
        if (total <= 0) return null;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (position < w.Start) break;
            // A word lasts until the next one starts; the last one, until the line ends (at most a couple of seconds).
            var end = i + 1 < words.Count ? words[i + 1].Start : Min(End, w.Start + TimeSpan.FromSeconds(2));
            double part = end > w.Start ? Math.Clamp((position - w.Start).TotalSeconds / (end - w.Start).TotalSeconds, 0, 1) : 1;
            sung += w.Text.Length * part;
            if (part < 1) break;
        }
        return Math.Clamp(sung / total, 0, 1);
    }

    static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>
    /// How long the line is sung, for karaoke without word timing: its syllables at a singing pace
    /// (about a third of a second each), never past the next line. Estimated by letters instead, a
    /// line ran far slower than it is sung, so the fill crept on until the next line and looked late.
    /// </summary>
    public TimeSpan Sung
    {
        get
        {
            _syllables ??= Syllables(Text);
            double natural = End > Start ? (End - Start).TotalSeconds * 0.95 : double.MaxValue;
            double estimate = 0.35 + _syllables.Value * 0.32;
            return TimeSpan.FromSeconds(Math.Max(0.4, Math.Min(natural, estimate)));
        }
    }

    int? _syllables;

    /// <summary>
    /// Syllables, roughly: one per Chinese character, kana or Hangul block, and elsewhere one per
    /// run of vowels (a Vietnamese word is one syllable, "người" included; English comes out close).
    /// </summary>
    public static int Syllables(string text)
    {
        int count = 0;
        bool inVowels = false;
        foreach (char c in text)
        {
            if (c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿' or >= '가' and <= '힣')
            {
                count++;
                inVowels = false;
                continue;
            }
            bool vowel = char.IsLetter(c) && IsVowel(c);
            if (vowel && !inVowels) count++;
            inVowels = vowel;
        }
        return Math.Max(1, count);
    }

    // The letter without its marks: ơ and ư are o and u, à and ế are a and e.
    static bool IsVowel(char c)
    {
        char bare = char.ToLowerInvariant(c.ToString().Normalize(System.Text.NormalizationForm.FormD)[0]);
        return bare is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
    }
}

/// <summary>One word of a word-synced line, with any space after it, and when it starts.</summary>
public sealed record LyricWord(TimeSpan Start, string Text);

public sealed class SyncedLyrics
{
    public SyncedLyrics(IReadOnlyList<LyricLine> lines) => Lines = lines;

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>Lines carry their own second line (<see cref="LyricLine.Sub"/>); the next line is not shown under them.</summary>
    public bool HasSubs { get; init; }

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

/// <summary>One song a lyrics source offers for a search typed by hand.</summary>
public sealed record LyricsCandidate(string Title, string Artist, string Album, TimeSpan Duration, bool Synced, bool Instrumental)
{
    /// <summary>"LRCLIB" or "NetEase".</summary>
    public string From { get; init; } = "LRCLIB";

    internal Services.LyricsService.LrclibTrack? Track { get; init; }

    /// <summary>The song's id on NetEase; its lyrics are fetched when it is picked.</summary>
    internal long NetEaseId { get; init; }
}

/// <summary>Lyrics ready to be shared on LRCLIB: what song they belong to, and the text with its times.</summary>
public sealed record LyricsDraft(string Title, string Artist, string Album, TimeSpan Duration, string Synced, string Plain, int Lines, int OffsetMs);

public sealed record TrackQuery(string Title, string Artist, string Album, TimeSpan Duration, string AppId);
