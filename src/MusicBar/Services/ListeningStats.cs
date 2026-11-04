using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>One song's year: how long it played and how many times.</summary>
public sealed class SongTally
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Seconds { get; set; }
    public int Plays { get; set; }
    public DateTime First { get; set; }
}

/// <summary>Everything counted in one year, as saved in stats\{year}.json.</summary>
public sealed class YearTally
{
    public int Year { get; set; }

    /// <summary>By song, keyed by its loose artist and title.</summary>
    public Dictionary<string, SongTally> Songs { get; set; } = new();

    /// <summary>Seconds listened per day ("2026-11-04").</summary>
    public Dictionary<string, double> Days { get; set; } = new();

    /// <summary>Seconds listened in each hour of the day, 0 to 23.</summary>
    public double[] Hours { get; set; } = new double[24];

    /// <summary>Seconds listened per player app, by its name.</summary>
    public Dictionary<string, double> Apps { get; set; } = new();

    public double TotalSeconds => Days.Values.Sum();
}

/// <summary>
/// Counts what is listened to, for the year in music: every few seconds while something plays,
/// the song, its artist, the day, the hour and the app get the time. A song counts as played once
/// it has been heard for half a minute (or half of a short one), again on each repeat. Kept on
/// this computer only, a small JSON file per year.
/// </summary>
public sealed class ListeningStats : IDisposable
{
    static readonly TimeSpan Step = TimeSpan.FromSeconds(5);
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    readonly AppSettings _s;
    readonly MediaService _media;
    readonly DispatcherTimer _timer = new() { Interval = Step };
    readonly Dictionary<int, YearTally> _years = new();
    DateTime _lastTick = DateTime.Now;
    DateTime _lastSave = DateTime.Now;
    bool _dirty;
    string _songKey = "";
    double _stint;
    bool _counted;

    public ListeningStats(AppSettings settings, MediaService media)
    {
        _s = settings;
        _media = media;
        _timer.Tick += (_, _) => Tick();
        _media.TrackChanged += (_, _) => NewStint();
        _media.Restarted += (_, _) => NewStint();
        _timer.Start();
    }

    public static string Folder => Path.Combine(AppPaths.DataDir, "Stats");

    /// <summary>The year's counts so far (empty when nothing was counted).</summary>
    public YearTally Year(int year)
    {
        if (_years.TryGetValue(year, out var known)) return known;
        var tally = Load(year);
        _years[year] = tally;
        return tally;
    }

    void NewStint()
    {
        _stint = 0;
        _counted = false;
        _songKey = "";
    }

    void Tick()
    {
        var now = DateTime.Now;
        double seconds = Math.Min((now - _lastTick).TotalSeconds, Step.TotalSeconds * 2);
        _lastTick = now;
        if (_dirty && now - _lastSave > TimeSpan.FromMinutes(1)) Save();
        if (!_s.KeepListeningStats || !_media.HasSession || !_media.IsPlaying || _media.IsDemo || _media.Title.Length == 0) return;

        var (artist, title) = TitleCleaner.Clean(_media.Artist, _media.Title);
        var key = $"{TitleCleaner.Loose(artist)}|{TitleCleaner.Loose(title)}";
        if (key != _songKey)
        {
            _songKey = key;
            _stint = 0;
            _counted = false;
        }
        var year = Year(now.Year);
        if (!year.Songs.TryGetValue(key, out var song))
        {
            song = new SongTally { Title = title, Artist = artist, First = now };
            year.Songs[key] = song;
        }
        song.Seconds += seconds;
        _stint += seconds;
        double needed = _media.Duration > TimeSpan.Zero ? Math.Min(30, _media.Duration.TotalSeconds / 2) : 30;
        if (!_counted && _stint >= needed)
        {
            _counted = true;
            song.Plays++;
        }
        var day = now.ToString("yyyy-MM-dd");
        year.Days[day] = year.Days.GetValueOrDefault(day) + seconds;
        year.Hours[now.Hour] += seconds;
        var app = _media.AppName.Length > 0 ? _media.AppName : "?";
        year.Apps[app] = year.Apps.GetValueOrDefault(app) + seconds;
        _dirty = true;
    }

    static string FileFor(int year) => Path.Combine(Folder, $"{year}.json");

    static YearTally Load(int year)
    {
        try
        {
            var path = FileFor(year);
            if (File.Exists(path) && JsonSerializer.Deserialize<YearTally>(File.ReadAllText(path), Json) is { } tally)
            {
                if (tally.Hours.Length != 24) tally.Hours = new double[24];
                return tally;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Reading listening stats for {year} failed", ex);
        }
        return new YearTally { Year = year };
    }

    public void Save()
    {
        _lastSave = DateTime.Now;
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var tally in _years.Values.Where(y => y.Days.Count > 0))
            {
                var path = FileFor(tally.Year);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(tally, Json));
                File.Move(path + ".tmp", path, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Saving listening stats failed", ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Save();
    }
}
