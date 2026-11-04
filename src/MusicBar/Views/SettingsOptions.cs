using System.Collections.Generic;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>A choice in a list; its label is looked up in <see cref="Strings"/> whenever it is shown.</summary>
public sealed record Option(object Value, string LabelKey)
{
    public string Label => Loc.T(LabelKey);
}

public sealed record SeekStyleOption(SeekStyle Value, string LabelKey, string DescriptionKey)
{
    public string Label => Loc.T(LabelKey);
    public string Description => Loc.T(DescriptionKey);
}

/// <summary>Display labels for every enum the settings window exposes.</summary>
public static class SettingsOptions
{
    /// <summary>Each language in its own words, so it can be found whatever language is showing.</summary>
    public static IReadOnlyList<Option> Languages { get; } =
    [
        new(AppLanguage.English, "Lang_English"),
        new(AppLanguage.Vietnamese, "Lang_Vietnamese"),
    ];

    public static IReadOnlyList<Option> LayerModes { get; } =
    [
        new(LayerMode.Auto, "Opt_Layer_Auto"),
        new(LayerMode.Under, "Opt_Layer_Under"),
        new(LayerMode.Over, "Opt_Layer_Over"),
    ];

    public static IReadOnlyList<Option> Placements { get; } =
    [
        new(WidgetPlacement.Right, "Opt_Place_Right"),
        new(WidgetPlacement.Left, "Opt_Place_Left"),
    ];

    public static IReadOnlyList<Option> LyricsPlacements { get; } =
    [
        new(LyricsPlacement.BesideCover, "Opt_Lyrics_BesideCover"),
        new(LyricsPlacement.LargestGap, "Opt_Lyrics_LargestGap"),
    ];

    public static IReadOnlyList<Option> TaskbarSizes { get; } =
    [
        new(true, "Opt_Size_Small"),
        new(false, "Opt_Size_Medium"),
    ];

    public static IReadOnlyList<Option> Backgrounds { get; } =
    [
        new(BarBackground.Dark, "Opt_Bg_Dark"),
        new(BarBackground.Tinted, "Opt_Bg_Tinted"),
        new(BarBackground.Transparent, "Opt_Bg_Transparent"),
    ];

    public static IReadOnlyList<Option> ControlStyles { get; } =
    [
        new(ControlStyle.Glow, "Opt_Ctl_Glow"),
        new(ControlStyle.Minimal, "Opt_Ctl_Minimal"),
        new(ControlStyle.Capsule, "Opt_Ctl_Capsule"),
        new(ControlStyle.Ring, "Opt_Ctl_Ring"),
        new(ControlStyle.Vinyl, "Opt_Ctl_Vinyl"),
        new(ControlStyle.Keys, "Opt_Ctl_Keys"),
        new(ControlStyle.Bubbles, "Opt_Ctl_Bubbles"),
    ];

    public static IReadOnlyList<Option> AccentSources { get; } =
    [
        new(AccentSource.AlbumArt, "Opt_Accent_Art"),
        new(AccentSource.App, "Opt_Accent_App"),
        new(AccentSource.SystemAccent, "Opt_Accent_System"),
        new(AccentSource.Custom, "Opt_Accent_Custom"),
    ];

    public static IReadOnlyList<SeekStyleOption> SeekStyles { get; } =
    [
        new(SeekStyle.Aurora, "Seek_Aurora", "Seek_Aurora_Desc"),
        new(SeekStyle.Mesh, "Seek_Mesh", "Seek_Mesh_Desc"),
        new(SeekStyle.Silk, "Seek_Silk", "Seek_Silk_Desc"),
        new(SeekStyle.Holo, "Seek_Holo", "Seek_Holo_Desc"),
        new(SeekStyle.Bokeh, "Seek_Bokeh", "Seek_Bokeh_Desc"),
        new(SeekStyle.Ink, "Seek_Ink", "Seek_Ink_Desc"),
        new(SeekStyle.Ribbon, "Seek_Ribbon", "Seek_Ribbon_Desc"),
        new(SeekStyle.Cinema, "Seek_Cinema", "Seek_Cinema_Desc"),
        new(SeekStyle.Neon, "Seek_Neon", "Seek_Neon_Desc"),
        new(SeekStyle.Liquid, "Seek_Liquid", "Seek_Liquid_Desc"),
        new(SeekStyle.Spectrum, "Seek_Spectrum", "Seek_Spectrum_Desc"),
        new(SeekStyle.Waveform, "Seek_Waveform", "Seek_Waveform_Desc"),
        new(SeekStyle.Mosaic, "Seek_Mosaic", "Seek_Mosaic_Desc"),
        new(SeekStyle.Classic, "Seek_Classic", "Seek_Classic_Desc"),
    ];

    public static IReadOnlyList<Option> LyricEffects { get; } =
    [
        new(LyricEffect.SlideUp, "Fx_SlideUp"),
        new(LyricEffect.Fade, "Fx_Fade"),
        new(LyricEffect.Swipe, "Fx_Swipe"),
        new(LyricEffect.Zoom, "Fx_Zoom"),
        new(LyricEffect.Words, "Fx_Words"),
        new(LyricEffect.Typewriter, "Fx_Typewriter"),
        new(LyricEffect.Ticker, "Fx_Ticker"),
        new(LyricEffect.None, "Fx_None"),
    ];

    public static IReadOnlyList<Option> Alignments { get; } =
    [
        new(LyricsAlign.Left, "Opt_Align_Left"),
        new(LyricsAlign.Center, "Opt_Align_Center"),
    ];

    public static IReadOnlyList<string> Swatches { get; } =
    [
        "#FF6B6B", "#FF4D8D", "#F72585", "#9B5DE5", "#4CC9F0", "#3A86FF",
        "#2EC4B6", "#1ED760", "#A3E635", "#FFD166", "#FF9F1C", "#E5E5E5",
    ];

    /// <summary>Light colors that read on a dark taskbar.</summary>
    public static IReadOnlyList<string> LyricSwatches { get; } =
    [
        "#FFFFFF", "#FFF4D6", "#FFE066", "#FFC2A1", "#FFB3C7", "#E0C3FF", "#B5E8FF", "#B9F6CA",
    ];

    /// <summary>Dark colors for the stroke, plus white for dark text.</summary>
    public static IReadOnlyList<string> StrokeSwatches { get; } =
    [
        "#000000", "#14141C", "#1B2440", "#2B1B3D", "#3D0C11", "#0B3D2E", "#FFFFFF",
    ];
}
