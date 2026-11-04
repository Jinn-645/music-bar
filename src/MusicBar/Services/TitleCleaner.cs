using System;
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
    [GeneratedRegex(@"\s*[\(\[【「][^\)\]】」]*\b(official|video|audio|lyrics?|lyric video|mv|m/v|visuali[sz]er|hd|hq|4k|remaster(ed)?|live session|performance video|color coded|vietsub|engsub|karaoke|beat|instrumental version)\b[^\)\]】」]*[\)\]】」]", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\s*[\(\[](feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex FeatBracket();

    [GeneratedRegex(@"\s+(feat\.?|ft\.?|featuring)\s.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatTail();

    [GeneratedRegex(@"\s*[|｜].*$")]
    private static partial Regex PipeTail();

    [GeneratedRegex(@"#\S+")]
    private static partial Regex Hashtag();

    [GeneratedRegex(@"^(.+?)\s+[-–—]\s+(.+)$")]
    private static partial Regex DashSplit();

    [GeneratedRegex(@"\s*-\s*Topic$|VEVO$|\s+Official(\s+(Channel|Music|Artist))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffix();

    [GeneratedRegex(@"\s*(,|&|\bx\b|×|\bfeat\.?|\bft\.?|\bvs\.?)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    public static (string Artist, string Title) Clean(string artist, string title)
    {
        var a = ChannelSuffix().Replace(artist.Trim(), "").Trim();
        var t = title.Trim();

        t = PipeTail().Replace(t, "");
        t = Hashtag().Replace(t, "").Trim();

        // "Artist - Song" stuffed into the title (YouTube and most browsers).
        var dash = DashSplit().Match(t);
        if (dash.Success)
        {
            var left = dash.Groups[1].Value.Trim();
            var right = dash.Groups[2].Value.Trim();
            if (a.Length == 0 || Loose(left).Contains(Loose(a)) || Loose(a).Contains(Loose(left)))
            {
                a = left;
                t = right;
            }
        }

        t = Noise().Replace(t, "");
        t = FeatBracket().Replace(t, "");
        t = FeatTail().Replace(t, "");
        t = t.Trim(' ', '-', '–', '"', '\'', '“', '”');
        return (a.Trim(), t.Length > 0 ? t : title.Trim());
    }

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
