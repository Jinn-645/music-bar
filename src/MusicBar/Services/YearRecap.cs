using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MusicBar.Services;

public sealed record RecapSong(string Title, string Artist, int Plays, double Minutes);

public sealed record RecapArtist(string Name, double Minutes);

/// <summary>What kind of listener the year shows, for the line at the bottom of the card.</summary>
public enum ListenerKind { AllRounder, NightOwl, EarlyBird, DayShift, OnRepeat, Explorer }

/// <summary>A year in music, worked out from what <see cref="ListeningStats"/> counted.</summary>
public sealed class YearRecap
{
    public required int Year { get; init; }
    public required double Minutes { get; init; }
    public required int Plays { get; init; }
    public required int Songs { get; init; }
    public required int Artists { get; init; }
    public required IReadOnlyList<RecapSong> TopSongs { get; init; }
    public required IReadOnlyList<RecapArtist> TopArtists { get; init; }
    public string? TopApp { get; init; }
    public double TopAppShare { get; init; }
    public DateTime? BiggestDay { get; init; }
    public double BiggestDayMinutes { get; init; }
    /// <summary>The hour of the day with the most music (its start, 0 to 23).</summary>
    public int PeakHour { get; init; }
    public int DaysListened { get; init; }
    /// <summary>The most days in a row with some music.</summary>
    public int LongestStreak { get; init; }
    public ListenerKind Kind { get; init; }

    /// <summary>Enough to be worth a recap: an hour of music.</summary>
    public bool Worthwhile => Minutes >= 60;

    public static YearRecap From(YearTally t)
    {
        var songs = t.Songs.Values.Where(s => s.Seconds >= 20).ToList();
        int plays = songs.Sum(s => s.Plays);
        var topSongs = songs.OrderByDescending(s => s.Plays).ThenByDescending(s => s.Seconds).Take(5)
            .Select(s => new RecapSong(s.Title, s.Artist, s.Plays, s.Seconds / 60)).ToList();

        // Every credited artist gets the song's time; the spelling heard first is the one shown.
        var artists = new Dictionary<string, (string Name, double Seconds)>();
        foreach (var s in songs.OrderBy(s => s.First))
        {
            foreach (var name in TitleCleaner.Artists(s.Artist))
            {
                var key = TitleCleaner.Loose(name);
                artists[key] = artists.TryGetValue(key, out var a) ? (a.Name, a.Seconds + s.Seconds) : (name, s.Seconds);
            }
        }
        var topArtists = artists.Values.OrderByDescending(a => a.Seconds).Take(5).Select(a => new RecapArtist(a.Name, a.Seconds / 60)).ToList();

        double total = t.TotalSeconds;
        var app = t.Apps.OrderByDescending(a => a.Value).FirstOrDefault();
        var days = t.Days.Where(d => d.Value >= 60)
            .Select(d => (Day: DateTime.TryParseExact(d.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : (DateTime?)null, Seconds: d.Value))
            .Where(d => d.Day != null).Select(d => (Day: d.Day!.Value, d.Seconds)).OrderBy(d => d.Day).ToList();
        var biggest = days.Count > 0 ? days.MaxBy(d => d.Seconds) : default;

        int streak = 0, run = 0;
        DateTime? previous = null;
        foreach (var (day, _) in days)
        {
            run = previous is { } p && (day - p).TotalDays == 1 ? run + 1 : 1;
            streak = Math.Max(streak, run);
            previous = day;
        }

        int peak = Enumerable.Range(0, 24).MaxBy(h => t.Hours[h]);
        return new YearRecap
        {
            Year = t.Year,
            Minutes = total / 60,
            Plays = plays,
            Songs = songs.Count,
            Artists = artists.Count,
            TopSongs = topSongs,
            TopArtists = topArtists,
            TopApp = app.Key,
            TopAppShare = total > 0 ? app.Value / total : 0,
            BiggestDay = days.Count > 0 ? biggest.Day : null,
            BiggestDayMinutes = biggest.Seconds / 60,
            PeakHour = peak,
            DaysListened = days.Count,
            LongestStreak = streak,
            Kind = KindOf(t, songs, plays, total),
        };
    }

    static ListenerKind KindOf(YearTally t, List<SongTally> songs, int plays, double total)
    {
        if (total <= 0) return ListenerKind.AllRounder;
        double Share(params int[] hours) => hours.Sum(h => t.Hours[h]) / total;
        int top = songs.Count > 0 ? songs.Max(s => s.Plays) : 0;
        if (Share(22, 23, 0, 1, 2, 3) >= 0.35) return ListenerKind.NightOwl;
        if (Share(5, 6, 7, 8) >= 0.3) return ListenerKind.EarlyBird;
        if (plays >= 20 && (top >= 50 || top >= plays * 0.15)) return ListenerKind.OnRepeat;
        if (songs.Count >= 80 && songs.Count >= plays * 0.6) return ListenerKind.Explorer;
        if (Share(9, 10, 11, 12, 13, 14, 15, 16, 17) >= 0.6) return ListenerKind.DayShift;
        return ListenerKind.AllRounder;
    }

    /// <summary>Which year's recap the end of the year offers: this one until New Year, last year's in the first week of January.</summary>
    public static int SeasonYear(DateTime now) => now.Month == 1 ? now.Year - 1 : now.Year;

    /// <summary>The recap season: from Christmas Eve to the 7th of January.</summary>
    public static bool InSeason(DateTime now)
        => (now.Month == 12 && now.Day >= 24) || (now.Month == 1 && now.Day <= 7);
}
