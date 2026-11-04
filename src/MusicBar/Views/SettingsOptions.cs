using System.Collections.Generic;
using MusicBar.Models;

namespace MusicBar.Views;

public sealed record Option(object Value, string Label);

public sealed record SeekStyleOption(SeekStyle Value, string Label, string Description);

/// <summary>Display labels for every enum the settings window exposes.</summary>
public static class SettingsOptions
{
    public static IReadOnlyList<Option> LayerModes { get; } =
    [
        new(LayerMode.Auto, "Tự động"),
        new(LayerMode.Under, "Nằm dưới icon taskbar"),
        new(LayerMode.Over, "Phủ mờ lên taskbar"),
    ];

    public static IReadOnlyList<Option> Placements { get; } =
    [
        new(WidgetPlacement.Right, "Bên phải, cạnh khay hệ thống"),
        new(WidgetPlacement.Left, "Bên trái"),
    ];

    public static IReadOnlyList<Option> LyricsPlacements { get; } =
    [
        new(LyricsPlacement.BesideCover, "Ngay cạnh ảnh bìa"),
        new(LyricsPlacement.LargestGap, "Khoảng trống lớn nhất trên taskbar"),
    ];

    public static IReadOnlyList<Option> TaskbarSizes { get; } =
    [
        new(true, "Nhỏ"),
        new(false, "Thường"),
    ];

    public static IReadOnlyList<Option> Backgrounds { get; } =
    [
        new(BarBackground.Dark, "Tối"),
        new(BarBackground.Tinted, "Tối, pha màu bài hát"),
        new(BarBackground.Transparent, "Trong suốt (chỉ có thanh tiến trình)"),
    ];

    public static IReadOnlyList<Option> AccentSources { get; } =
    [
        new(AccentSource.AlbumArt, "Theo ảnh bìa bài hát"),
        new(AccentSource.App, "Theo màu ứng dụng phát nhạc"),
        new(AccentSource.SystemAccent, "Theo màu nhấn của Windows"),
        new(AccentSource.Custom, "Màu tự chọn"),
    ];

    public static IReadOnlyList<SeekStyleOption> SeekStyles { get; } =
    [
        new(SeekStyle.Aurora, "Cực quang", "Ba dải sáng uốn lượn theo màu bài hát, dâng lên theo nhạc"),
        new(SeekStyle.Mesh, "Lưới màu", "Những mảng màu lớn của ảnh bìa trôi chậm, hòa vào nhau và thở theo nhạc"),
        new(SeekStyle.Silk, "Sợi tơ", "Những sợi tơ mảnh xoắn quanh nhau, se lại thành một sợi ở đầu phát"),
        new(SeekStyle.Holo, "Ánh kim", "Giấy kim tuyến đổi sắc quanh màu bài hát, ánh sáng lướt qua mặt giấy"),
        new(SeekStyle.Bokeh, "Ánh đèn mờ", "Đốm đèn ngoài nét xa gần, mỗi đốm nhấp nháy theo một dải tần"),
        new(SeekStyle.Ink, "Mực loang", "Màu nước loang ra sau đầu phát, từng giọt mực vẫn tiếp tục lan"),
        new(SeekStyle.Ribbon, "Dải màu ảnh bìa", "Màu từng cột của ảnh bìa trải thành dải, lộ dần theo bài"),
        new(SeekStyle.Cinema, "Điện ảnh", "Ảnh bìa nhòe sâu, trôi chậm như một cảnh phim"),
        new(SeekStyle.Neon, "Ống neon", "Ống đèn neon dọc mép, chập chờn và hắt sáng xuống taskbar"),
        new(SeekStyle.Liquid, "Chất lỏng", "Mặt chất lỏng gợn sóng ở đầu phát, bọt khí li ti nổi lên"),
        new(SeekStyle.Spectrum, "Phổ âm", "Cột phổ âm thanh thật, phần đã phát được tô màu"),
        new(SeekStyle.Waveform, "Dạng sóng", "Sóng âm kiểu SoundCloud, tự vẽ dạng sóng thật khi phát"),
        new(SeekStyle.Mosaic, "Ô LED", "Ma trận ô vuông lấp lánh theo bảng màu bài hát"),
        new(SeekStyle.Classic, "Cổ điển", "Phủ màu và quầng sáng ở đầu phát"),
    ];

    public static IReadOnlyList<Option> LyricEffects { get; } =
    [
        new(LyricEffect.SlideUp, "Cuộn lên (Slide up)"),
        new(LyricEffect.Fade, "Mờ dần (Fade)"),
        new(LyricEffect.Swipe, "Quét ngang (Swipe)"),
        new(LyricEffect.Zoom, "Phóng to + nhòe (Zoom blur)"),
        new(LyricEffect.Words, "Từng chữ nảy lên (Word pop)"),
        new(LyricEffect.Typewriter, "Gõ chữ (Typewriter)"),
        new(LyricEffect.Ticker, "Băng chữ chạy ngang (Ticker)"),
        new(LyricEffect.None, "Không hiệu ứng"),
    ];

    public static IReadOnlyList<Option> Alignments { get; } =
    [
        new(LyricsAlign.Left, "Sát ảnh bìa"),
        new(LyricsAlign.Center, "Giữa widget"),
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
