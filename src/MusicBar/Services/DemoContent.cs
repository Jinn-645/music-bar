using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>Built-in tracks for "--demo": lets the bar be previewed without any player running.</summary>
public static class DemoContent
{
    public const string AppId = "MusicBar.Demo";

    public sealed record DemoTrack(string Title, string Artist, string Album, string Genre, TimeSpan Duration, string Lrc, Color A, Color B);

    public static IReadOnlyList<DemoTrack> Tracks { get; } =
    [
        new("Mưa Neon", "MusicBar Demo", "Night Drive", "Synthwave", TimeSpan.FromSeconds(84), """
            [00:04.50]Mưa neon rơi trên phố vắng đêm nay
            [00:08.80]City lights are fading into blue
            [00:13.10]Em nghe không tiếng tim anh khẽ gọi
            [00:17.40]Every step I take leads back to you
            [00:21.70]Đèn xanh đèn đỏ chẳng thể giữ chân
            [00:26.00]We're running through the rain without a clue
            [00:30.30]
            [00:36.00]Và khi bình minh lên ta vẫn còn đây
            [00:40.30]Holding on to everything we knew
            [00:44.60]Một câu hát thật dài để thử hiệu ứng chữ chạy khi dòng lời rộng hơn khoảng trống còn lại trên thanh bar
            [00:52.00]Mưa neon, mưa neon
            [00:56.30]Shine on, shine on, shine through
            [01:00.60]Ta cứ đi, ta cứ đi
            [01:04.90]Into the night, me and you
            [01:09.20]
            """, Color.FromRgb(0xFF, 0x3D, 0x7F), Color.FromRgb(0xFF, 0xA6, 0x2B)),

        new("Đêm Thành Phố", "MusicBar Demo", "Night Drive", "City Pop", TimeSpan.FromSeconds(70), """
            [00:03.00]Thành phố ngủ quên dưới ánh trăng xanh
            [00:07.20]Gió đưa câu hát qua những con đường
            [00:11.40]Slow down, the night is ours to keep
            [00:15.60]Ta nghe nhịp tim hòa cùng nhịp phố
            [00:19.80]
            [00:26.00]Một ngày mới sẽ đến thôi
            [00:30.20]Light is breaking through the sky
            [00:34.40]Cứ hát lên, cứ hát lên
            [00:38.60]Till the morning says goodbye
            [00:42.80]
            """, Color.FromRgb(0x1F, 0xD1, 0xC1), Color.FromRgb(0x2B, 0x6C, 0xFF)),
    ];

    public static LyricsResult GetLyrics(string title)
    {
        var track = Tracks.FirstOrDefault(t => t.Title == title);
        if (track == null) return new LyricsResult(LyricsStatus.NotFound, null, "demo", null);
        return new LyricsResult(LyricsStatus.Synced, LrcParser.Parse(track.Lrc), "demo", null);
    }

    public static BitmapSource CreateArt(DemoTrack t)
    {
        const int size = 256;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = new LinearGradientBrush(t.A, t.B, new Point(0, 0), new Point(1, 1));
            dc.DrawRectangle(bg, null, new Rect(0, 0, size, size));

            var glow = new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
            {
                Center = new Point(0.3, 0.25),
                GradientOrigin = new Point(0.3, 0.25),
                RadiusX = 0.6,
                RadiusY = 0.6,
            };
            dc.DrawRectangle(glow, null, new Rect(0, 0, size, size));

            var ring = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 6);
            dc.DrawEllipse(null, ring, new Point(size * 0.68, size * 0.66), 62, 62);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Point(size * 0.68, size * 0.66), 22, 22);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
