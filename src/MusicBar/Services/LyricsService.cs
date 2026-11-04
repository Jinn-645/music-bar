using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>
/// Finds synced lyrics: first in the local folder (.lrc files, including ones
/// the user drops in), then on LRCLIB (lrclib.net, the open lyrics database).
/// Downloads are written back as .lrc so the next play is instant and offline.
/// </summary>
public sealed class LyricsService
{
    const string Marker = "[re:MusicBar/LRCLIB]";
    static readonly TimeSpan MissTtl = TimeSpan.FromDays(3);
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    sealed class LrclibTrack
    {
        public long Id { get; set; }
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? AlbumName { get; set; }
        public double? Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
    }

    readonly AppSettings _settings;
    readonly object _missLock = new();
    HttpClient? _http;
    string? _httpProxy;
    Dictionary<string, DateTime>? _misses;

    public LyricsService(AppSettings settings) => _settings = settings;

    public string Folder => string.IsNullOrWhiteSpace(_settings.LyricsFolder) ? AppPaths.DefaultLyricsDir : _settings.LyricsFolder;

    string MissFile => Path.Combine(Folder, "not-found.json");

    // ------------------------------------------------------------------ public API

    public async Task<LyricsResult> GetAsync(TrackQuery q, bool force, CancellationToken ct)
    {
        if (q.AppId == DemoContent.AppId) return DemoContent.GetLyrics(q.Title);
        if (string.IsNullOrWhiteSpace(q.Title)) return new LyricsResult(LyricsStatus.None, null, "", null);

        Directory.CreateDirectory(Folder);
        var (artist, title) = TitleCleaner.Clean(q.Artist, q.Title);
        var key = MissKey(artist, title);

        if (!force)
        {
            if (TryLoadLocal(q, artist, title) is { } local) return local;
            if (IsRecentMiss(key)) return new LyricsResult(LyricsStatus.NotFound, null, "cache", null);
        }
        if (!_settings.FetchOnline) return new LyricsResult(LyricsStatus.NotFound, null, "offline", null);

        LrclibTrack? found;
        try
        {
            found = await FetchAsync(q, artist, title, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"LRCLIB lookup failed for '{artist} - {title}'", ex);
            return new LyricsResult(LyricsStatus.Error, null, "LRCLIB", null);
        }

        var path = Path.Combine(Folder, FileName(artist, title) + ".lrc");
        if (found == null)
        {
            RecordMiss(key);
            return new LyricsResult(LyricsStatus.NotFound, null, "LRCLIB", null);
        }

        if (found.Instrumental)
        {
            Save(path, Header(found, artist, title) + "[instrumental:true]\n");
            return new LyricsResult(LyricsStatus.Instrumental, null, "LRCLIB", path);
        }

        if (!string.IsNullOrWhiteSpace(found.SyncedLyrics))
        {
            var text = Header(found, artist, title) + found.SyncedLyrics.Replace("\r\n", "\n");
            Save(path, text);
            ForgetMiss(key);
            return new LyricsResult(LyricsStatus.Synced, LrcParser.Parse(text), "LRCLIB", path);
        }

        var plainPath = Path.ChangeExtension(path, ".txt");
        if (!string.IsNullOrWhiteSpace(found.PlainLyrics)) Save(plainPath, found.PlainLyrics);
        return new LyricsResult(LyricsStatus.PlainOnly, null, "LRCLIB", plainPath);
    }

    /// <summary>Persists a per-song timing correction into the song's .lrc file.</summary>
    public void SaveOffset(LyricsResult result, int offsetMs)
    {
        if (result.Lyrics == null) return;
        result.Lyrics.Offset = TimeSpan.FromMilliseconds(offsetMs);
        if (result.FilePath == null || !File.Exists(result.FilePath)) return;
        try
        {
            var text = File.ReadAllText(result.FilePath);
            File.WriteAllText(result.FilePath, LrcParser.WithOffset(text, offsetMs), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error("Saving lyric offset failed", ex);
        }
    }

    public (int Saved, int Misses) GetCacheStats()
    {
        try
        {
            int saved = Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*.lrc").Count() : 0;
            lock (_missLock) return (saved, LoadMisses().Count);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    public void ClearMisses()
    {
        lock (_missLock)
        {
            _misses = new Dictionary<string, DateTime>();
            try { File.Delete(MissFile); } catch (Exception) { }
        }
    }

    /// <summary>Deletes only files this app downloaded; hand-made .lrc files are kept.</summary>
    public int ClearDownloaded()
    {
        int removed = 0;
        if (!Directory.Exists(Folder)) return 0;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.*").Where(f => f.EndsWith(".lrc") || f.EndsWith(".txt")).ToList())
        {
            try
            {
                using (var reader = new StreamReader(file))
                {
                    var head = reader.ReadLine() ?? "";
                    if (!file.EndsWith(".txt") && !head.Contains(Marker)) continue;
                }
                File.Delete(file);
                removed++;
            }
            catch (Exception)
            {
            }
        }
        ClearMisses();
        return removed;
    }

    // ------------------------------------------------------------------ local files

    LyricsResult? TryLoadLocal(TrackQuery q, string artist, string title)
    {
        var names = new[] { FileName(artist, title), FileName(q.Artist, q.Title), FileName("", title) }
            .Distinct().ToList();

        foreach (var name in names)
        {
            var lrc = Path.Combine(Folder, name + ".lrc");
            if (!File.Exists(lrc)) continue;
            try
            {
                var lyrics = LrcParser.Parse(File.ReadAllText(lrc));
                if (lyrics.IsInstrumental) return new LyricsResult(LyricsStatus.Instrumental, null, "local", lrc);
                if (lyrics.Lines.Count > 0) return new LyricsResult(LyricsStatus.Synced, lyrics, "local", lrc);
            }
            catch (Exception ex)
            {
                Log.Error($"Unreadable lyrics file {lrc}", ex);
            }
        }

        foreach (var name in names)
        {
            var txt = Path.Combine(Folder, name + ".txt");
            if (File.Exists(txt)) return new LyricsResult(LyricsStatus.PlainOnly, null, "local", txt);
        }
        return null;
    }

    static void Save(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error($"Writing lyrics file {path} failed", ex);
        }
    }

    static string Header(LrclibTrack t, string artist, string title)
    {
        var sb = new StringBuilder();
        sb.Append(Marker).Append('\n');
        sb.Append("[ti:").Append(t.TrackName ?? title).Append("]\n");
        sb.Append("[ar:").Append(t.ArtistName ?? artist).Append("]\n");
        if (!string.IsNullOrWhiteSpace(t.AlbumName)) sb.Append("[al:").Append(t.AlbumName).Append("]\n");
        if (t.Duration is double d) sb.Append("[length:").Append(TimeSpan.FromSeconds(d).ToString(@"m\:ss", CultureInfo.InvariantCulture)).Append("]\n");
        sb.Append("[id:lrclib-").Append(t.Id).Append("]\n");
        return sb.ToString();
    }

    public static string FileName(string artist, string title)
    {
        var raw = artist.Trim().Length > 0 ? $"{artist.Trim()} - {title.Trim()}" : title.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw) sb.Append(invalid.Contains(ch) ? '_' : ch);
        var name = sb.ToString().Trim().TrimEnd('.');
        return name.Length > 150 ? name[..150] : name;
    }

    // ------------------------------------------------------------------ negative cache

    static string MissKey(string artist, string title) => $"{TitleCleaner.Loose(artist)}|{TitleCleaner.Loose(title)}";

    Dictionary<string, DateTime> LoadMisses()
    {
        if (_misses != null) return _misses;
        try
        {
            _misses = File.Exists(MissFile)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(MissFile)) ?? new()
                : new();
        }
        catch (Exception)
        {
            _misses = new();
        }
        return _misses;
    }

    bool IsRecentMiss(string key)
    {
        lock (_missLock)
            return LoadMisses().TryGetValue(key, out var when) && DateTime.UtcNow - when < MissTtl;
    }

    void RecordMiss(string key)
    {
        lock (_missLock)
        {
            var misses = LoadMisses();
            misses[key] = DateTime.UtcNow;
            foreach (var old in misses.Where(kv => DateTime.UtcNow - kv.Value > MissTtl).Select(kv => kv.Key).ToList())
                misses.Remove(old);
            try { File.WriteAllText(MissFile, JsonSerializer.Serialize(misses)); } catch (Exception) { }
        }
    }

    void ForgetMiss(string key)
    {
        lock (_missLock)
        {
            if (LoadMisses().Remove(key))
                try { File.WriteAllText(MissFile, JsonSerializer.Serialize(_misses)); } catch (Exception) { }
        }
    }

    // ------------------------------------------------------------------ LRCLIB

    HttpClient Client()
    {
        var proxy = _settings.ProxyUrl;
        if (_http != null && proxy == _httpProxy) return _http;

        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }
        _http?.Dispose();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://lrclib.net/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicBar/1.0 (Windows desktop lyrics bar)");
        _httpProxy = proxy;
        return _http;
    }

    async Task<LrclibTrack?> FetchAsync(TrackQuery q, string artist, string title, CancellationToken ct)
    {
        int duration = (int)Math.Round(q.Duration.TotalSeconds);
        var candidates = new List<LrclibTrack>();

        // 1) Exact signature lookup with what the player reported.
        var exact = await GetAsync<LrclibTrack>("api/get" + Query(
            ("track_name", q.Title), ("artist_name", q.Artist), ("album_name", q.Album),
            ("duration", duration > 0 ? duration.ToString(CultureInfo.InvariantCulture) : null)), ct);
        if (exact != null) candidates.Add(exact);

        // 2) Same with the cleaned-up names.
        if ((title != q.Title || artist != q.Artist) && artist.Length > 0)
        {
            var cleaned = await GetAsync<LrclibTrack>("api/get" + Query(
                ("track_name", title), ("artist_name", artist),
                ("duration", duration > 0 ? duration.ToString(CultureInfo.InvariantCulture) : null)), ct);
            if (cleaned != null) candidates.Add(cleaned);
        }

        var best = Pick(candidates, artist, title, duration);
        if (best != null && !string.IsNullOrWhiteSpace(best.SyncedLyrics)) return best;

        // 3) Search by fields, then by free text.
        var byFields = await GetAsync<List<LrclibTrack>>("api/search" + Query(
            ("track_name", title), ("artist_name", artist.Length > 0 ? TitleCleaner.FirstArtist(artist) : null)), ct);
        if (byFields != null) candidates.AddRange(byFields);
        best = Pick(candidates, artist, title, duration);
        if (best != null && !string.IsNullOrWhiteSpace(best.SyncedLyrics)) return best;

        var byText = await GetAsync<List<LrclibTrack>>("api/search" + Query(("q", $"{artist} {title}".Trim())), ct);
        if (byText != null) candidates.AddRange(byText);
        return Pick(candidates, artist, title, duration);
    }

    static LrclibTrack? Pick(List<LrclibTrack> candidates, string artist, string title, int duration)
    {
        LrclibTrack? best = null;
        double bestScore = double.MinValue;
        foreach (var c in candidates)
        {
            double titleSim = TitleCleaner.Similarity(c.TrackName ?? "", title);
            if (titleSim < 0.45) continue;
            double score = titleSim * 40;
            if (artist.Length > 0) score += TitleCleaner.Similarity(c.ArtistName ?? "", artist) * 30;

            if (!string.IsNullOrWhiteSpace(c.SyncedLyrics)) score += 100;
            else if (c.Instrumental) score += 60;
            else if (!string.IsNullOrWhiteSpace(c.PlainLyrics)) score += 10;
            else continue;

            if (duration > 0 && c.Duration is double d)
            {
                double diff = Math.Abs(d - duration);
                if (diff <= 2) score += 40;
                else if (diff <= 5) score += 20;
                else if (diff > 20) score -= 80;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

    async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await Client().GetAsync(url, ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(json, Json);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(600, ct);
            }
        }
    }

    static string Query(params (string Key, string? Value)[] pairs)
    {
        var parts = pairs.Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}");
        return "?" + string.Join("&", parts);
    }
}
