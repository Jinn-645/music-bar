using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Models;

namespace MusicBar.Services;

public sealed class YouTubeCaptionBridge : IDisposable
{
    readonly HttpListener _listener = new();
    readonly object _gate = new();
    readonly Dictionary<string, YouTubeCaptionCue> _cues = new();
    CancellationTokenSource? _stop;
    string _videoId = "";
    string _title = "";
    DateTime _lastUpdateUtc;

    public event EventHandler? CaptionsUpdated;

    public void Start()
    {
        _listener.Prefixes.Add("http://127.0.0.1:19693/");
        _listener.Start();
        _stop = new CancellationTokenSource();
        _ = ListenAsync(_stop.Token);
    }

    public bool TryGet(TrackQuery q, out SyncedLyrics lyrics)
    {
        lock (_gate)
        {
            lyrics = null!;
            if (_videoId.Length == 0 || DateTime.UtcNow - _lastUpdateUtc > TimeSpan.FromSeconds(4) || !NamesMatch(q.Title, _title)) return false;
            var lines = new List<LyricLine>();
            foreach (var cue in _cues.Values) lines.Add(new LyricLine(cue.Start, cue.Text) { End = cue.End });
            if (lines.Count == 0) return false;
            lyrics = new SyncedLyrics(lines);
            return true;
        }
    }

    async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? context;
            try { context = await _listener.GetContextAsync(); }
            catch when (ct.IsCancellationRequested) { break; }
            catch { continue; }
            _ = HandleAsync(context, ct);
        }
    }

    async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            if (context.Request.Url?.AbsolutePath != "/caption") { context.Response.StatusCode = 404; return; }
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            if (context.Request.HttpMethod == "OPTIONS") { context.Response.StatusCode = 204; return; }
            if (context.Request.HttpMethod != "POST") { context.Response.StatusCode = 405; return; }
            using var doc = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: ct);
            var root = doc.RootElement;
            var video = root.GetProperty("videoId").GetString() ?? "";
            var title = root.GetProperty("title").GetString() ?? "";
            if (video.Length == 0 || title.Length == 0) { context.Response.StatusCode = 400; return; }
            bool notify;
            lock (_gate)
            {
                notify = video != _videoId || _cues.Count == 0;
                if (video != _videoId) _cues.Clear();
                _videoId = video; _title = title; _cues.Clear();
                if (root.TryGetProperty("cues", out var cues))
                    foreach (var cue in cues.EnumerateArray())
                    {
                        var text = cue.GetProperty("text").GetString()?.Trim() ?? "";
                        var start = cue.GetProperty("start").GetDouble();
                        var duration = cue.GetProperty("duration").GetDouble();
                        if (text.Length > 0 && start >= 0 && duration > 0)
                            _cues[$"{start:0.###}"] = new YouTubeCaptionCue(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + duration));
                    }
                _lastUpdateUtc = DateTime.UtcNow;
            }
            if (notify) CaptionsUpdated?.Invoke(this, EventArgs.Empty);
            context.Response.StatusCode = 204;
        }
        catch { context.Response.StatusCode = 400; }
        finally { context.Response.Close(); }
    }

    static bool NamesMatch(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase)
        || b.Contains(a.Trim(), StringComparison.OrdinalIgnoreCase) || a.Contains(b.Trim(), StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _stop?.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}
