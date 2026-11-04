using System;
using System.Globalization;

namespace MusicBar.Views.Face;

/// <summary>
/// The hat the season calls for: apricot blossom around Tết, rabbit ears for the Mid-Autumn
/// festival (both by the lunar calendar), a witch hat in the last week of October, a Santa hat
/// over Christmas.
/// </summary>
public static class FaceSeasons
{
    static readonly ChineseLunisolarCalendar Lunar = new();
    static DateTime _day = DateTime.MinValue;
    static FaceHat? _hat;

    /// <summary>The season's hat on <paramref name="date"/>, or null outside every season.</summary>
    public static FaceHat? HatOn(DateTime date)
    {
        date = date.Date;
        if (date == _day) return _hat;
        _day = date;
        _hat = Find(date);
        return _hat;
    }

    static FaceHat? Find(DateTime date)
    {
        if (date.Month == 12 && date.Day is >= 20 and <= 26) return FaceHat.Santa;
        if (date.Month == 10 && date.Day >= 25) return FaceHat.Witch;
        // From the Kitchen Gods' day (the 23rd of the last month) to the 7th day of the new year.
        if (LunarNewYear(date.Year) is { } tet && date >= tet.AddDays(-7) && date <= tet.AddDays(6)) return FaceHat.Mai;
        if (MidAutumn(date.Year) is { } moon && date >= moon.AddDays(-4) && date <= moon.AddDays(1)) return FaceHat.Bunny;
        return null;
    }

    /// <summary>The first day of the first lunar month in a Gregorian year (late January to mid February).</summary>
    public static DateTime? LunarNewYear(int year)
    {
        try
        {
            for (var d = new DateTime(year, 1, 19); d <= new DateTime(year, 2, 22); d = d.AddDays(1))
                if (Lunar.GetMonth(d) == 1 && Lunar.GetDayOfMonth(d) == 1) return d;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        return null;
    }

    /// <summary>The 15th day of the 8th lunar month (September or early October).</summary>
    public static DateTime? MidAutumn(int year)
    {
        try
        {
            for (var d = new DateTime(year, 9, 1); d.Month <= 10; d = d.AddDays(1))
            {
                int lunarYear = Lunar.GetYear(d);
                int month = Lunar.GetMonth(d);
                // With a leap month earlier in the year, every later month's number is one higher.
                int leap = Lunar.GetLeapMonth(lunarYear);
                if (leap > 0 && month >= leap) month--;
                if (month == 8 && Lunar.GetDayOfMonth(d) == 15 && !(leap > 0 && Lunar.GetMonth(d) == leap)) return d;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        return null;
    }
}
