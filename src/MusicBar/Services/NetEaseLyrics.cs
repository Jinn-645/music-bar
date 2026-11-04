using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBar.Services;

/// <summary>A song NetEase Cloud Music found: its id, name, first artist, album and length in seconds.</summary>
public sealed record NetEaseSong(long Id, string Name, string Artist, string Album, double Seconds);

/// <summary>
/// NetEase Cloud Music as a second source of synced lyrics, strong on Chinese, Korean and Japanese
/// songs. These are the public web endpoints its site uses, not an official API: they can change or
/// go away, so MusicBar only asks when the user turned this on, and every failure just means "none".
/// </summary>
public sealed partial class NetEaseLyrics
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Credits NetEase puts in as the first timed lines ("作词 : …", "Composer: …").
    [GeneratedRegex(@"^\s*(作词|作曲|编曲|制作人|监制|和声|混音|母带|录音|吉他|贝斯|鼓|弦乐|出品|OP|SP|词|曲|Lyricist|Lyrics|Composer|Arranger|Producer|Written by)\s*[:：]", RegexOptions.IgnoreCase)]
    private static partial Regex Credit();

    [GeneratedRegex(@"^\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex Timed();

    readonly Func<string, HttpClient> _client;

    /// <param name="client">An HTTP client for a base address (it follows the proxy setting).</param>
    public NetEaseLyrics(Func<string, HttpClient> client) => _client = client;

    HttpClient Client() => _client("https://music.163.com/");

    public async Task<IReadOnlyList<NetEaseSong>> SearchAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var url = "api/search/get?type=1&limit=12&offset=0&s=" + Uri.EscapeDataString(text.Trim());
        using var doc = await GetAsync(url, ct);
        if (doc == null || !doc.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs)) return [];
        var list = new List<NetEaseSong>();
        foreach (var s in songs.EnumerateArray())
        {
            if (!s.TryGetProperty("id", out var id) || !id.TryGetInt64(out long songId)) continue;
            string name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string artist = s.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                ? string.Join(", ", a.EnumerateArray().Select(x => x.TryGetProperty("name", out var an) ? an.GetString() : null).Where(x => !string.IsNullOrWhiteSpace(x)))
                : "";
            string album = s.TryGetProperty("album", out var al) && al.TryGetProperty("name", out var aln) ? aln.GetString() ?? "" : "";
            double seconds = s.TryGetProperty("duration", out var d) && d.TryGetDouble(out double ms) ? ms / 1000 : 0;
            list.Add(new NetEaseSong(songId, name, artist, album, seconds));
        }
        return list;
    }

    /// <summary>The song's synced lyrics as LRC, credits taken out; null when it has none.</summary>
    public async Task<string?> LyricsAsync(long id, CancellationToken ct)
    {
        using var doc = await GetAsync($"api/song/lyric?id={id}&lv=1&tv=-1", ct);
        if (doc == null) return null;
        var root = doc.RootElement;
        if (root.TryGetProperty("nolyric", out var none) && none.ValueKind == JsonValueKind.True) return null;
        if (!root.TryGetProperty("lrc", out var lrc) || !lrc.TryGetProperty("lyric", out var lyric)) return null;
        var text = lyric.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        var sb = new StringBuilder();
        int timed = 0;
        bool opening = true;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var m = Timed().Match(line);
            if (!m.Success) continue;
            var words = line[m.Length..];
            // The credits and a "Song - Artist" title line come first; a line like that further in is part of the song.
            if (opening && (Credit().IsMatch(words) || words.Contains(" - ", StringComparison.Ordinal))) continue;
            if (words.Trim().Length > 0) opening = false;
            sb.Append(line).Append('\n');
            if (words.Trim().Length > 0) timed++;
        }
        return timed >= 3 ? sb.ToString() : null;
    }

    async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        using var response = await Client().GetAsync(url, ct);
        if (response.StatusCode != HttpStatusCode.OK) return null;
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
