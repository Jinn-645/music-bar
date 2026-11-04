using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views;

// Renders MusicBar's own UI to PNG frames with transparency, for the promo video.
//
//   seek   <style> <out> <seconds> <wDip> <hDip> <scale> <progressFrom> <progressTo> <#colorA> <#colorB>
//          the real SeekRenderer, frame-exact (its clock is driven per frame)
//   lyrics <out> <seconds> <font> <#text> <#stroke> <strokeOpacity> <strokeRadius> <wDip> <hDip> <scale> <sizeDip>
//          the real LyricsPresenter singing the promo's lines, captured in real time at 30 fps
//          (<font> is a family name, or "bundled:<Name>" for one of MusicBar's own fonts)
//   panel  <out> <scale>
//          the real control center panel: open, and with the playback-device list slid out
//   assets <out>
//          the app icon and the demo cover art as PNG
static class Program
{
    const double Fps = 30;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string FontsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        @"..\..\..\..\..\..\src\MusicBar\Assets\Fonts\"));

    // The lines the lyrics engine sings in the film (LRC, parsed by the app's own parser).
    const string PromoLrc = """
        [00:00.40]Taskbar ơi, dậy đi thôi
        [00:03.20]Hôm nay em được hát rồi
        [00:06.00]Lời chạy theo nhạc từng chữ một
        [00:08.80]Đổi màu, đổi phông, vẫn đúng dấu
        [00:11.60]
        """;

    [STAThread]
    static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // MusicBar's shared resources (icon font, button styles), as its App.xaml merges them.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MusicBar;component/Themes/Bar.xaml"),
        });
        int code = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                switch (args[0])
                {
                    case "seek": Seek(args); break;
                    case "lyrics": await Lyrics(args); break;
                    case "panel": await Panel(args); break;
                    case "assets": Assets(args); break;
                    default: throw new ArgumentException("unknown mode " + args[0]);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                code = 1;
            }
            app.Shutdown();
        };
        app.Run();
        return code;
    }

    static double D(string s) => double.Parse(s, Inv);

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>An invisible window far off screen, so elements get real layout, effects and animation ticks.</summary>
    static Window Stage(UIElement content, double w, double h)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000,
            Width = w, Height = h, Content = content,
        };
        window.Show();
        return window;
    }

    /// <summary>
    /// The element as an image. Drawn through a brush pinned to the element's own box: rendering
    /// the element directly would include its layout offset (margins) and crop the far edges.
    /// </summary>
    static BitmapSource Snap(FrameworkElement e, double scale)
    {
        e.UpdateLayout();
        double w = e.ActualWidth, h = e.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(e)
            {
                Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap((int)Math.Round(w * scale), (int)Math.Round(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // ---------------------------------------------------------------- seekbar

    static void Seek(string[] a)
    {
        var style = Enum.Parse<SeekStyle>(a[1], true);
        string outDir = Directory.CreateDirectory(a[2]).FullName;
        double seconds = D(a[3]), w = D(a[4]), h = D(a[5]), scale = D(a[6]), p0 = D(a[7]), p1 = D(a[8]);
        double time = 0;
        var seek = new SeekRenderer { Look = style, Intensity = 0.6, Width = w, Height = h, Clock = () => time };
        seek.SetEdge(true);
        seek.SetColors(C(a[9]), C(a[10]), animate: false);
        seek.SetArt(DemoContent.CreateArt(DemoContent.Tracks[0]));
        seek.SetTrack("promo-" + style);
        var stage = Stage(seek, w, h);

        int frames = (int)Math.Round(seconds * Fps);
        for (int i = 0; i < frames; i++)
        {
            time = 10 + i / Fps;
            seek.SetState(p0 + (p1 - p0) * i / Math.Max(1, frames - 1), timeline: true, playing: true);
            seek.Tick();
            seek.InvalidateVisual();
            SavePng(Snap(seek, scale), Path.Combine(outDir, $"f{i:0000}.png"));
        }
        stage.Close();
        Console.WriteLine($"{style}: {frames} frames → {outDir}");
    }

    // ---------------------------------------------------------------- lyrics

    static async Task Lyrics(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double seconds = D(a[2]);
        var font = a[3].StartsWith("bundled:")
            ? new FontFamily(new Uri(FontsDir), "./#" + a[3]["bundled:".Length..])
            : new FontFamily(a[3]);
        double w = D(a[8]), h = D(a[9]), scale = D(a[10]), size = D(a[11]);

        var presenter = new LyricsPresenter { Accent = Color.FromRgb(0xFF, 0x6B, 0x6B) };
        presenter.Configure(LyricEffect.SlideUp, karaoke: true, twoLine: false, font, size, size * 0.75,
            HorizontalAlignment.Left, speed: 1);
        presenter.SetTextStyle(C(a[4]), 0.96);
        // The same halo MusicBar puts around lyrics (LyricStyle): a zero-offset drop shadow.
        var host = new Grid
        {
            Width = w, Height = h,
            Effect = new DropShadowEffect { ShadowDepth = 0, Color = C(a[5]), Opacity = D(a[6]), BlurRadius = D(a[7]) },
        };
        host.Children.Add(presenter);
        var stage = Stage(host, w, h);
        presenter.ShowLyrics(LrcParser.Parse(PromoLrc));

        // Real time: the line changes animate on WPF's own clock, the karaoke follows the given position.
        int frames = (int)Math.Round(seconds * Fps);
        var pending = new ConcurrentBag<Task>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            double due = i / Fps;
            while (clock.Elapsed.TotalSeconds < due) await Task.Delay(1);
            presenter.Update(TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds), playing: true);
            var frame = Snap(host, scale);
            string path = Path.Combine(outDir, $"f{i:0000}.png");
            pending.Add(Task.Run(() => SavePng(frame, path)));
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        await Task.WhenAll(pending);
        stage.Close();
        Console.WriteLine($"lyrics: {frames} frames → {outDir}");
    }

    // ---------------------------------------------------------------- control center

    static async Task Panel(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double scale = D(a[2]);
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // Two demo notifications in place of whatever this machine has waiting.
        var feed = new NotificationFeed();
        typeof(NotificationFeed).GetProperty("Available", Any)!.SetValue(feed, true);
        typeof(NotificationFeed).GetProperty("Items", Any)!.SetValue(feed, new List<NotificationItem>
        {
            new(1, "Tin nhắn", "", null, "Mẹ", "Con ăn cơm chưa? Nhớ nghỉ tay nghe nhạc nha",
                DateTimeOffset.Now.AddMinutes(-2)),
            new(2, "Lịch", "", null, "Họp nhóm lúc 10:00", "Phòng họp tầng 3 · còn 15 phút",
                DateTimeOffset.Now.AddMinutes(-15)),
        });
        var audio = new AudioOutputs();
        var panel = new ControlCenterWindow(feed, audio) { Left = -20000, Top = -20000, ShowActivated = false };
        panel.SetAccent(Color.FromRgb(0xFF, 0x6B, 0x6B));
        panel.Show();

        var type = typeof(ControlCenterWindow);
        void Call(string name, params object[] args) => type.GetMethod(name, Any)!.Invoke(panel, args);
        var root = (FrameworkElement)type.GetField("Root", Any)!.GetValue(panel)!;
        root.BeginAnimation(UIElement.OpacityProperty, null);
        root.Opacity = 1;
        typeof(ControlCenterWindow).GetProperty("IsOpen", Any)!.SetValue(panel, true);
        Call("SyncVolume");
        Call("SyncOutput");
        Call("RenderNotifications");
        Call("RenderTiles");
        await Task.Delay(1500); // the tiles load asynchronously (radios, Night light state)

        var sideKind = type.GetNestedType("SideKind", BindingFlags.NonPublic)!;
        // The whole window: its root sits inside a margin that leaves room for the shadows.
        Call("SetSide", Enum.Parse(sideKind, "None"), false);
        await Task.Delay(300);
        SavePng(Snap(panel, scale), Path.Combine(outDir, "panel-open.png"));

        Call("SetSide", Enum.Parse(sideKind, "Outputs"), false);
        await Task.Delay(300);
        SavePng(Snap(panel, scale), Path.Combine(outDir, "panel-outputs.png"));
        panel.Close();
        Console.WriteLine($"panel → {outDir} ({panel.ActualWidth}x{panel.ActualHeight} DIP)");
    }

    // ---------------------------------------------------------------- icon and cover

    static void Assets(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        string ico = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\src\MusicBar\Assets\MusicBar.ico"));
        var decoder = new IconBitmapDecoder(new Uri(ico), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var largest = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        SavePng(largest, Path.Combine(outDir, "musicbar-icon.png"));
        SavePng(DemoContent.CreateArt(DemoContent.Tracks[0]), Path.Combine(outDir, "cover-mua-neon.png"));
        SavePng(DemoContent.CreateArt(DemoContent.Tracks[1]), Path.Combine(outDir, "cover-dem-thanh-pho.png"));
        Console.WriteLine($"icon {largest.PixelWidth}px and covers → {outDir}");
    }
}
