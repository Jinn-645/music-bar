using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>Three colors a style paints with: its main color, its second, and an accent.</summary>
public sealed record Palette3(Color A, Color B, Color C);

/// <summary>
/// Each progress style, visualizer and skin can keep its own colors instead of the song's: one of
/// the presets, or three picked by hand. Saved per look in <see cref="AppSettings.Palettes"/> as
/// "preset:sunset" or "#RRGGBB,#RRGGBB,#RRGGBB"; a look with nothing saved follows the song.
/// </summary>
public static class StylePalettes
{
    public sealed record Preset(string Id, string NameKey, Color A, Color B, Color C)
    {
        public Palette3 Colors => new(A, B, C);
    }

    static Color Hex(string hex) => ColorUtil.TryParse(hex, out var c) ? c : Colors.White;

    public static IReadOnlyList<Preset> Presets { get; } =
    [
        new("sunset", "Pal_Sunset", Hex("#FF5F6D"), Hex("#FFC371"), Hex("#FF8C42")),
        new("ocean", "Pal_Ocean", Hex("#2E8BFF"), Hex("#00D4C7"), Hex("#7A6CFF")),
        new("aurora", "Pal_Aurora", Hex("#4CFFB0"), Hex("#47A9FF"), Hex("#B26CFF")),
        new("neon", "Pal_Neon", Hex("#FF2BD6"), Hex("#00F0FF"), Hex("#8A2BFF")),
        new("candy", "Pal_Candy", Hex("#FF9AD5"), Hex("#9AD8FF"), Hex("#C9A7FF")),
        new("sakura", "Pal_Sakura", Hex("#FF7FA8"), Hex("#FFC6D9"), Hex("#E9A2FF")),
        new("forest", "Pal_Forest", Hex("#3DDC84"), Hex("#A8E063"), Hex("#17B890")),
        new("ember", "Pal_Ember", Hex("#FF3D00"), Hex("#FFAB00"), Hex("#FF6D00")),
        new("gold", "Pal_Gold", Hex("#F7C948"), Hex("#FFE7A3"), Hex("#E09B12")),
        new("glacier", "Pal_Glacier", Hex("#BDEBFF"), Hex("#6FB7FF"), Hex("#E8F7FF")),
        new("mono", "Pal_Mono", Hex("#FFFFFF"), Hex("#BFC4CC"), Hex("#8A93A3")),
    ];

    /// <summary>What the look on the taskbar is saved under: its skin's file, or its style's name.</summary>
    public static string KeyFor(AppSettings s) => s.Skin.Length > 0 ? "skin:" + s.Skin : s.SeekStyle.ToString();

    public static string KeyFor(SeekStyle style) => style.ToString();

    public static string KeyForSkin(string file) => "skin:" + file;

    /// <summary>The colors saved for a look; null when it follows the song.</summary>
    public static Palette3? For(AppSettings s, string key) => Parse(s.Palettes.TryGetValue(key, out var value) ? value : null);

    public static Palette3? For(AppSettings s) => For(s, KeyFor(s));

    /// <summary>The saved value as it was written: "", "preset:..." or three hex colors.</summary>
    public static string Saved(AppSettings s, string key) => s.Palettes.TryGetValue(key, out var value) ? value : "";

    public static Palette3? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith("preset:", StringComparison.Ordinal))
            return Presets.FirstOrDefault(p => p.Id == value[7..])?.Colors;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return null;
        return ColorUtil.TryParse(parts[0], out var a) && ColorUtil.TryParse(parts[1], out var b) && ColorUtil.TryParse(parts[2], out var c)
            ? new Palette3(a, b, c)
            : null;
    }

    public static string Custom(Color a, Color b, Color c) => $"{ColorUtil.ToHex(a)},{ColorUtil.ToHex(b)},{ColorUtil.ToHex(c)}";

    /// <summary>Saves (or with an empty value, forgets) a look's colors.</summary>
    public static void Set(AppSettings s, string key, string value)
    {
        var next = new Dictionary<string, string>(s.Palettes, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) next.Remove(key);
        else next[key] = value;
        s.Palettes = next;
    }
}
