using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBar.Services;

/// <summary>
/// Normalizes the messy titles players publish (YouTube channel names,
/// "Artist - Title (Official MV)", "feat." credits...) into something a
/// lyrics database can match.
/// </summary>
public static partial class TitleCleaner
{
    // Words that mark a bracket as packaging rather than part of the song's name.
    const string NoiseWords = @"official|video|audio|lyrics?|lyric video|mv|m/v|visuali[sz]er|hd|hq|4k|remaster(ed)?|live session|performance video|color coded|vietsub|engsub|karaoke|beat|instrumental version|album|full album|ep|single|prod\.?|produced by|o\.?s\.?t|soundtrack|nhạc phim|teaser|dance practice|track ?\d+";

    // The same for a whole segment between dashes or pipes; stricter, since a song may start with "Single" or "Video".
    const string SegmentWords = @"album|full album|official|mv|m/v|lyrics?|lyric video|music video|vietsub|engsub|prod\.?|produced by|o\.?s\.?t|soundtrack|nhạc phim|teaser|karaoke|visuali[sz]er|performance video|dance practice|color coded|4k|hd|track ?\d+";

    [GeneratedRegex(@"\s*[\(\[【「『][^\)\]】」』]*\b(" + NoiseWords + @")(\b|(?=\W))[^\)\]】」』]*[\)\]】」』]", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"^\W*(" + SegmentWords + @")(\b|(?=\W))", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseSegment();

    [GeneratedRegex(@"\s+(official(\s+(music\s+)?(mv|m/v|video|audio|lyrics?(\s+video)?))?|music\s+video|lyrics?\s+video|m/v|mv)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseTail();

    [GeneratedRegex(@"^\s*(track\s*)?\d{1,2}\s*[.:)\-–]\s+", RegexOptions.IgnoreCase)]
    private static partial Regex TrackNumber();

    [GeneratedRegex(@"\s*[\(\[](feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex FeatBracket();

    [GeneratedRegex(@"\s+(feat\.?|ft\.?|featuring)\s.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatTail();

    [GeneratedRegex(@"\s*[\(\[【「『][^\)\]】」』]*[\)\]】」』]")]
    private static partial Regex AnyBracket();

    [GeneratedRegex(@"\s*(?:[|｜]|//|\s[-–—~]\s)\s*")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\s*[:：/~]\s*")]
    private static partial Regex SoftSeparator();

    [GeneratedRegex(@"#\S+")]
    private static partial Regex Hashtag();

    [GeneratedRegex(@"\s*-\s*Topic$|VEVO$|\s+Official(\s+(Channel|Music|Artist))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffix();

    [GeneratedRegex(@"\s*(,|&|\bx\b|×|\bfeat\.?|\bft\.?|\bvs\.?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    static readonly char[] Quotes = [' ', '-', '–', '"', '\'', '“', '”', '‘', '’', '«', '»'];

    /// <summary>
    /// The artist and the song's name from what the player published. A YouTube title such as
    /// "HIEUTHUHAI - Không Thể Say (prod. by Kewtiie) | Album 'Ai Cũng Phải Bắt Đầu Từ Đâu Đó'"
    /// becomes ("HIEUTHUHAI", "Không Thể Say"): segments between dashes and pipes that are
    /// packaging (album, official MV, lyrics, OST…) are dropped, and the one naming the channel
    /// or artist is taken as the artist.
    /// </summary>
    public static (string Artist, string Title) Clean(string artist, string title)
    {
        var (a, parts, _) = Parse(artist, title);
        var song = FeatTail().Replace(parts[0], "").Trim(Quotes);
        return (a, song.Length > 0 ? song : title.Trim());
    }

    /// <summary>
    /// The artist and the title's meaningful segments, the song first. A segment naming the
    /// artist (the channel, or whoever the player says) becomes the artist; with no artist to go
    /// by, "Artist - Song" is the usual order. <c>Matched</c> is false when segments were left
    /// that none could be told apart as the artist (a record label's channel, a " - Live" suffix).
    /// </summary>
    static (string Artist, List<string> Parts, bool Matched) Parse(string artist, string title)
    {
        var a = ChannelSuffix().Replace(artist.Trim(), "").Trim();
        var t = Hashtag().Replace(title.Trim(), "").Trim();
        t = Noise().Replace(t, "");
        t = FeatBracket().Replace(t, "");

        var parts = Separator().Split(t)
            .Select(p => TrackNumber().Replace(NoiseTail().Replace(p, ""), "").Trim(Quotes))
            .Where(p => p.Length > 0 && !NoiseSegment().IsMatch(p))
            .ToList();
        if (parts.Count == 0) parts.Add(t.Trim(Quotes));

        int artistAt = a.Length > 0 ? parts.FindIndex(p => SameArtist(p, a)) : -1;
        if (artistAt < 0 && a.Length == 0 && parts.Count >= 2) artistAt = 0;
        if (artistAt >= 0 && parts.Count >= 2)
        {
            a = parts[artistAt];
            parts.RemoveAt(artistAt);
            return (a.Trim(), parts, true);
        }
        return (a.Trim(), parts, parts.Count < 2);
    }

    /// <summary>
    /// Ways to look the song up, best first: the cleaned names, then shorter forms of the title
    /// (without brackets, up to the first colon or slash, its first words), "Artist - Song" read
    /// from a label's upload, and the title alone, since a channel is not always the artist and
    /// LRCLIB misses long titles.
    /// </summary>
    public static IReadOnlyList<(string Artist, string Title)> Variants(string artist, string title)
    {
        var (a, parts, matched) = Parse(artist, title);
        var t = Clean(artist, title).Title;
        var list = new List<(string Artist, string Title)> { (a, t) };
        void Add(string ar, string ti)
        {
            ti = FeatTail().Replace(ti, "").Trim(Quotes);
            if (Loose(ti).Length < 2) return;
            if (!list.Any(v => Loose(v.Artist) == Loose(ar) && Loose(v.Title) == Loose(ti))) list.Add((ar.Trim(), ti));
        }

        if (!matched) Add(parts[0], parts[1]);
        var plain = AnyBracket().Replace(t, "").Trim();
        Add(a, plain);
        var head = SoftSeparator().Split(plain)[0];
        Add(a, head);
        var words = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 6) Add(a, string.Join(' ', words.Take(5)));
        if (a.Length > 0) Add("", plain);
        return list;
    }

    static bool SameArtist(string segment, string artist)
    {
        var s = Loose(segment);
        var x = Loose(artist);
        if (s.Length == 0 || x.Length == 0) return false;
        return s == x || (s.Length >= 3 && x.Contains(s)) || (x.Length >= 3 && s.Contains(x)) || Similarity(segment, artist) >= 0.6;
    }

    /// <summary>Every name in a credit such as "A, B & C feat. D".</summary>
    public static IEnumerable<string> Artists(string artist)
        => ArtistSplit().Split(artist).Select(p => p.Trim()).Where(p => p.Length > 0 && Loose(p).Length > 0);

    public static string FirstArtist(string artist)
    {
        var parts = ArtistSplit().Split(artist);
        return parts.FirstOrDefault(p => p.Trim().Length > 0)?.Trim() ?? artist;
    }

    /// <summary>Lower-case, accent-free, alphanumeric-only form used for fuzzy comparisons.</summary>
    public static string Loose(string s)
    {
        var decomposed = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    public static double Similarity(string a, string b)
    {
        var x = Loose(a);
        var y = Loose(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 1;
        if (x.Contains(y) || y.Contains(x)) return 0.8 * Math.Min(x.Length, y.Length) / Math.Max(x.Length, y.Length) + 0.2;

        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Length == 0 || tb.Length == 0) return 0;
        int common = ta.Intersect(tb).Count();
        return 0.7 * common / ta.Union(tb).Count();
    }

    static string[] Tokens(string s)
        => s.Split([' ', ',', '.', '(', ')', '[', ']', '-', '/', '&', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Loose).Where(t => t.Length > 0).Distinct().ToArray();
}
