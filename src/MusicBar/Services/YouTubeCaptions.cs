using System;
using System.Collections.Generic;

namespace MusicBar.Services;

public sealed record YouTubeCaptionPayload(
    string VideoId, string Title, string Artist, double DurationSeconds,
    double CurrentSeconds, string Text, double StartSeconds, double Duration,
    string Language = "");

public sealed record YouTubeCaptionCue(string Text, TimeSpan Start, TimeSpan End);

public sealed class YouTubeCaptionState
{
    readonly TimeSpan _expiry;
    readonly object _gate = new();
    readonly Dictionary<string, YouTubeCaptionCue> _cues = new();
    string? _videoId;
    DateTime _lastHeartbeatUtc;

    public YouTubeCaptionState(TimeSpan expiry) => _expiry = expiry;

    public bool Accept(YouTubeCaptionPayload payload)
    {
        if (payload == null || string.IsNullOrWhiteSpace(payload.VideoId) || string.IsNullOrWhiteSpace(payload.Text)) return false;
        if (payload.StartSeconds < 0 || payload.Duration <= 0) return false;
        lock (_gate)
        {
            if (!string.Equals(_videoId, payload.VideoId, StringComparison.Ordinal))
            {
                _videoId = payload.VideoId;
                _cues.Clear();
            }
            _cues[$"{payload.VideoId}:{payload.StartSeconds:0.###}"] = new YouTubeCaptionCue(
                payload.Text.Trim(), TimeSpan.FromSeconds(payload.StartSeconds),
                TimeSpan.FromSeconds(payload.StartSeconds + payload.Duration));
            _lastHeartbeatUtc = DateTime.UtcNow;
            return true;
        }
    }

    public YouTubeCaptionCue? GetActive(TimeSpan position)
    {
        lock (_gate)
        {
            if (_videoId == null || DateTime.UtcNow - _lastHeartbeatUtc > _expiry) return null;
            foreach (var cue in _cues.Values)
                if (position >= cue.Start && position < cue.End) return cue;
            return null;
        }
    }
}

