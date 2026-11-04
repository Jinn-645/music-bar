using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MusicBar.Models;

namespace MusicBar.Services;

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[([A-Za-z#]+):(.*)\]\s*$")]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>")]
    private static partial Regex WordTag();

    public static SyncedLyrics Parse(string text)
    {
        var lines = new List<LyricLine>();
        var offset = TimeSpan.Zero;
        bool instrumental = false;

        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) != null)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var times = new List<TimeSpan>();
            int pos = 0;
            while (true)
            {
                var m = TimeTag().Match(line, pos);
                if (!m.Success || m.Index != pos) break;
                times.Add(ToTime(m));
                pos = m.Index + m.Length;
            }

            if (times.Count == 0)
            {
                var meta = MetaTag().Match(line);
                if (!meta.Success) continue;
                var key = meta.Groups[1].Value.ToLowerInvariant();
                var value = meta.Groups[2].Value.Trim();
                if (key == "offset" && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                    offset = TimeSpan.FromMilliseconds(ms);
                else if (key == "instrumental" && value.Equals("true", StringComparison.OrdinalIgnoreCase))
                    instrumental = true;
                continue;
            }

            var content = WordTag().Replace(line[pos..], "").Trim();
            foreach (var t in times) lines.Add(new LyricLine(t, content));
        }

        var sorted = lines.OrderBy(l => l.Start).ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].End = i + 1 < sorted.Count ? sorted[i + 1].Start : sorted[i].Start + TimeSpan.FromSeconds(6);

        return new SyncedLyrics(sorted) { Offset = offset, IsInstrumental = instrumental };
    }

    static TimeSpan ToTime(Match m)
    {
        int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int ms = 0;
        if (m.Groups[3].Success)
        {
            var frac = m.Groups[3].Value;
            ms = int.Parse(frac, CultureInfo.InvariantCulture) * frac.Length switch { 1 => 100, 2 => 10, _ => 1 };
        }
        return new TimeSpan(0, 0, min, sec, ms);
    }

    /// <summary>Rewrites (or inserts) the [offset:] tag of an LRC document.</summary>
    public static string WithOffset(string text, int offsetMs)
    {
        var kept = text.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.TrimStart().StartsWith("[offset:", StringComparison.OrdinalIgnoreCase));
        var header = offsetMs == 0 ? Enumerable.Empty<string>() : [$"[offset:{offsetMs.ToString("+0;-0", CultureInfo.InvariantCulture)}]"];
        return string.Join("\n", header.Concat(kept));
    }
}
