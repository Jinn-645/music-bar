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
//   settings <outDir> <English|Vietnamese> <scale>
//          the real settings window, one PNG per page, in the given language
//   controls <out.png> <taskbarHeightDip> <scale>
//          the real widget's previous / play / next buttons in every control style, paused and playing
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
                    case "controls": await ControlStyles(args); break;
                    case "settings": await SettingsPages(args); break;
                    case "wireless": await Wireless(); break;
                    case "faces": Faces(args); break;
                    case "facelife": await FaceLife(args); break;
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

        // The Wi-Fi list with made-up networks and a key being typed. It is drawn directly: opening it
        // for real would scan, and on a PC that blocks the scan, open Windows' own Wi-Fi list on screen.
        // Closed as far as Dismiss knows, so losing focus doesn't fade it out.
        typeof(ControlCenterWindow).GetProperty("IsOpen", Any)!.SetValue(panel, false);
        Call("SetSide", Enum.Parse(sideKind, "None"), false);
        type.GetField("_side", Any)!.SetValue(panel, Enum.Parse(sideKind, "WiFi"));
        var side = (FrameworkElement)type.GetField("Side", Any)!.GetValue(panel)!;
        side.BeginAnimation(UIElement.OpacityProperty, null);
        side.Opacity = 1;
        ((TranslateTransform)type.GetField("SideShift", Any)!.GetValue(panel)!).BeginAnimation(TranslateTransform.XProperty, null);
        ((TranslateTransform)type.GetField("SideShift", Any)!.GetValue(panel)!).X = 0;
        type.GetField("_networks", Any)!.SetValue(panel, new List<WifiNetwork>
        {
            new("Nhà Mèo 5G", 4, WifiSecurity.Password, true, "Nhà Mèo 5G", true),
            new("Cafe Sáng", 3, WifiSecurity.Open, false, null, true),
            new("VNPT-Tang2", 3, WifiSecurity.Password, false, null, true),
            new("Viettel_8A2C", 2, WifiSecurity.Password, false, "Viettel_8A2C", false),
            new("CongTy-Staff", 1, WifiSecurity.Enterprise, false, null, true),
        });
        type.GetField("_wifiOpen", Any)!.SetValue(panel, "VNPT-Tang2");
        type.GetField("_wifiAskKey", Any)!.SetValue(panel, true);
        Call("RenderWifi");
        await Task.Delay(300);
        SavePng(Snap(panel, scale), Path.Combine(outDir, "panel-wifi-list.png"));
        panel.Close();
        Console.WriteLine($"panel → {outDir} ({panel.ActualWidth}x{panel.ActualHeight} DIP)");
    }

    // ---------------------------------------------------------------- faces

    /// <summary>
    /// One sheet per background (dark, light): a row per face set, a column per expression, each
    /// face drawn the way the taskbar draws it (design box fitted to a taskbar-tall cell).
    /// </summary>
    static void Faces(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        double cell = D(a[2]);   // cell height in pixels, like the taskbar's thickness
        var accent = Color.FromRgb(0xFF, 0x6B, 0x6B);
        var poses = new (string Name, Func<Color, MusicBar.Views.Face.FaceFrame> Make)[]
        {
            ("rest", ink => Pose(ink, accent)),
            ("smile", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Happy, Mouth = MusicBar.Views.Face.MouthShape.Grin, Blush = 0.5 }),
            ("look", ink => Pose(ink, accent) with { Look = new Vector(0.9, -0.4), LidLeft = 0.3, LidRight = 0.3, Mouth = MusicBar.Views.Face.MouthShape.Flat }),
            ("wow", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Wide, Pupil = 0.65, Mouth = MusicBar.Views.Face.MouthShape.O, Open = 0.9, Brows = 0.6, Extras = MusicBar.Views.Face.FaceExtras.Exclaim }),
            ("angry", ink => Pose(ink, accent) with { LidLeft = 0.25, LidRight = 0.25, Brows = -1, Mouth = MusicBar.Views.Face.MouthShape.Teeth, Extras = MusicBar.Views.Face.FaceExtras.Anger }),
            ("asleep", ink => Pose(ink, accent) with { LidLeft = 1, LidRight = 1, Mouth = MusicBar.Views.Face.MouthShape.O, Open = 0.2, Extras = MusicBar.Views.Face.FaceExtras.Zzz }),
            ("laugh", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Squeeze, Mouth = MusicBar.Views.Face.MouthShape.Open, Open = 0.9, Blush = 0.8, Extras = MusicBar.Views.Face.FaceExtras.Tears }),
            ("love", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Heart, Mouth = MusicBar.Views.Face.MouthShape.Smile, Blush = 0.9, Extras = MusicBar.Views.Face.FaceExtras.Hearts }),
            ("dizzy", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Spiral, Mouth = MusicBar.Views.Face.MouthShape.Wavy, Brows = 0.4, Extras = MusicBar.Views.Face.FaceExtras.Sweat }),
            ("star", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Star, Mouth = MusicBar.Views.Face.MouthShape.Open, Open = 0.7, Extras = MusicBar.Views.Face.FaceExtras.Sparkle }),
            ("sad", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Teary, Brows = 0.9, Mouth = MusicBar.Views.Face.MouthShape.Frown, Extras = MusicBar.Views.Face.FaceExtras.Tears }),
            ("yawn", ink => Pose(ink, accent) with { LidLeft = 0.8, LidRight = 0.8, Mouth = MusicBar.Views.Face.MouthShape.Yawn, Open = 1, Brows = 0.35 }),
            ("sing", ink => Pose(ink, accent) with { Mouth = MusicBar.Views.Face.MouthShape.O, Open = 0.5, Level = 0.7, Extras = MusicBar.Views.Face.FaceExtras.Notes, Blush = 0.25 }),
            ("cat", ink => Pose(ink, accent) with { Eyes = MusicBar.Views.Face.EyeShape.Happy, Mouth = MusicBar.Views.Face.MouthShape.Cat, Blush = 0.5 }),
            ("cheeky", ink => Pose(ink, accent) with { LidLeft = 1, Mouth = MusicBar.Views.Face.MouthShape.Tongue, Blush = 0.4 }),
        };
        var styles = Enum.GetValues<MusicBar.Views.Face.FaceStyle>();
        double cw = cell * 116 / 88, labelW = 110, headH = 22;
        foreach (var (name, background, ink) in new[]
                 {
                     ("dark", Color.FromRgb(0x14, 0x15, 0x1A), Color.FromRgb(0xFF, 0xFD, 0xF6)),
                     ("light", Color.FromRgb(0xF2, 0xF3, 0xF5), Color.FromRgb(0x15, 0x17, 0x1E)),
                 })
        {
            var sheet = new DrawingVisual();
            double width = labelW + cw * poses.Length, height = headH + cell * styles.Length;
            using (var dc = sheet.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
                var face = new Typeface("Segoe UI");
                var text = new SolidColorBrush(ink);
                for (int c = 0; c < poses.Length; c++)
                    dc.DrawText(new FormattedText(poses[c].Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 11, text, 1),
                        new Point(labelW + c * cw + 4, 4));
                for (int r = 0; r < styles.Length; r++)
                {
                    dc.DrawText(new FormattedText(styles[r].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 12, text, 1),
                        new Point(6, headH + r * cell + cell / 2 - 8));
                    var painter = MusicBar.Views.Face.FacePainters.For(styles[r]);
                    for (int c = 0; c < poses.Length; c++)
                    {
                        var frame = poses[c].Make(ink) with { Time = 1.3 + c * 0.37 };
                        double unit = Math.Min(cw / 116, cell / 88);
                        var m = Matrix.Identity;
                        m.Scale(unit, unit);
                        m.Translate(labelW + c * cw + (cw - 100 * unit) / 2, headH + r * cell + (cell - 72 * unit) / 2);
                        dc.PushClip(new RectangleGeometry(new Rect(labelW + c * cw, headH + r * cell, cw, cell)));
                        dc.PushTransform(new MatrixTransform(m));
                        painter.Draw(dc, frame);
                        MusicBar.Views.Face.FacePainters.DrawExtras(dc, frame);
                        dc.Pop();
                        dc.Pop();
                    }
                }
            }
            var rtb = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(sheet);
            SavePng(rtb, Path.Combine(outDir, $"faces-{name}.png"));
        }
        Console.WriteLine($"faces → {outDir}");
    }

    /// <summary>
    /// The face living for a while in real time, through a script of what happens around it (a
    /// poke, a burst of pokes, a rub, the scroll wheel, music, the user leaving), as a sheet of
    /// frames half a second apart.
    /// </summary>
    static async Task FaceLife(string[] a)
    {
        string outPath = Path.GetFullPath(a[1]);
        var style = Enum.Parse<MusicBar.Views.Face.FaceStyle>(a[2], true);
        const double w = 132, h = 96, step = 0.5;
        var view = new MusicBar.Views.Face.FaceView(3) { Look = style, Width = w, Height = h, Accent = Color.FromRgb(0xFF, 0x6B, 0x6B) };
        var host = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x1A)),
            Left = -20000, Top = -20000, Width = w, Height = h, ShowActivated = false, Content = view,
        };
        host.Show();
        var frames = new List<(string Label, BitmapSource Shot)>();
        var clock = Stopwatch.StartNew();
        double nextShot = 0;
        string label = "idle";
        while (clock.Elapsed.TotalSeconds < 19)
        {
            double t = clock.Elapsed.TotalSeconds;
            bool playing = t is >= 11 and < 14;
            double level = playing ? 0.35 + 0.35 * Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.9)) : -1;
            var idle = t >= 14 ? TimeSpan.FromMinutes(6) : TimeSpan.Zero;
            var senses = new MusicBar.Views.Face.FaceSenses(new DateTime(2026, 1, 5, 15, 30, 0), true, playing, level, idle, null, false);
            // The script.
            double now = view.Now;
            if (Cross(t, 1.0)) { view.Brain.Poke(now); label = "poke"; }
            if (t is >= 3 and < 3.7 && Every(t, 0.1)) { view.Brain.Poke(now); label = "pokes"; }
            if (t is >= 6.5 and < 8 && Every(t, 0.05)) { view.Brain.Rub(0.12); label = "rub"; }
            if (t is >= 9 and < 9.6 && Every(t, 0.08)) { view.Brain.Scroll(now, 120); label = "scroll"; }
            if (Cross(t, 11)) label = "music";
            if (Cross(t, 14)) label = "user away";
            view.Step(senses);
            if (t >= nextShot)
            {
                nextShot += step;
                host.UpdateLayout();
                frames.Add(($"{t:0.0}s {label}", Snap(view, 2)));
            }
            await Task.Delay(16);
        }
        host.Close();

        int columns = 8, rows = (frames.Count + columns - 1) / columns;
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x1A)), null, new Rect(0, 0, columns * w, rows * (h + 16)));
            var face = new Typeface("Segoe UI");
            for (int i = 0; i < frames.Count; i++)
            {
                double x = i % columns * w, y = i / columns * (h + 16);
                dc.DrawImage(frames[i].Shot, new Rect(x, y, w, h));
                dc.DrawText(new FormattedText(frames[i].Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10, Brushes.Gray, 1), new Point(x + 4, y + h));
            }
        }
        var rtb = new RenderTargetBitmap((int)(columns * w) * 2, (int)(rows * (h + 16)) * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(sheet);
        SavePng(rtb, outPath);
        Console.WriteLine($"face life → {outPath} ({frames.Count} frames)");

        bool Cross(double time, double at) => time >= at && time - at < 0.017;
        bool Every(double time, double period) => time % period < 0.017;
    }

    static MusicBar.Views.Face.FaceFrame Pose(Color ink, Color accent) => new()
    {
        Time = 1, Eyes = MusicBar.Views.Face.EyeShape.Open, LidLeft = 0.05, LidRight = 0.05, Look = new Vector(0, 0.1),
        Pupil = 1, Brows = 0, Mouth = MusicBar.Views.Face.MouthShape.Smile, Open = 0, Blush = 0.15,
        Extras = MusicBar.Views.Face.FaceExtras.None, Accent = accent, Ink = ink, Level = 0,
    };

    // ---------------------------------------------------------------- Wi-Fi and Bluetooth

    /// <summary>What the control center's Wi-Fi and Bluetooth lists would show, printed (read-only).</summary>
    static async Task Wireless()
    {
        var wifi = new WifiNetworks();
        Console.WriteLine($"wifi open: {await wifi.OpenAsync()}");
        var cached = await wifi.ListAsync(scan: false);
        Console.WriteLine($"wifi cached: {cached.Count} blocked={wifi.Blocked}");
        var networks = await wifi.ListAsync(scan: true);
        Console.WriteLine($"wifi scanned: {networks.Count} blocked={wifi.Blocked}");
        foreach (var n in networks)
            Console.WriteLine($"  {n.Bars} bars  {n.Security,-10} connected={n.Connected,-5} saved={n.Saved,-5} auto={n.AutoConnect,-5} {n.Ssid}");

        var bt = new BluetoothDevices(Dispatcher.CurrentDispatcher);
        bt.Start();
        await Task.Delay(8000);
        bt.Stop();
        var items = bt.Items();
        Console.WriteLine($"bluetooth: {items.Count}");
        foreach (var i in items)
        {
            bool audio = i.Paired && await BluetoothDevices.CanConnectAsync(i);
            Console.WriteLine($"  paired={i.Paired,-5} connected={i.Connected,-5} {i.Kind,-8} audio={audio,-5} {i.Address,-17} {i.Name}");
        }
    }

    // ---------------------------------------------------------------- settings window

    static async Task SettingsPages(string[] a)
    {
        string outDir = Directory.CreateDirectory(a[1]).FullName;
        var language = Enum.Parse<AppLanguage>(a[2], true);
        double scale = D(a[3]);
        var settings = new AppSettings { Language = language };
        MusicBar.Infrastructure.Loc.Instance.Language = language;
        var window = new SettingsWindow(settings, new MediaService(settings), new LyricsService(settings))
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1100, Height = 1300,
            ShowActivated = false,
        };
        window.Show();
        for (int page = 0; page < window.PageCount; page++)
        {
            window.Page = page;
            await Task.Delay(700);
            window.UpdateLayout();
            // The window's own dark backdrop isn't part of its content: lay the content over one.
            var content = Snap((FrameworkElement)window.Content, scale);
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                var box = new Rect(0, 0, content.PixelWidth / scale, content.PixelHeight / scale);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)), null, box);
                dc.DrawImage(content, box);
            }
            var rtb = new RenderTargetBitmap(content.PixelWidth, content.PixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(sheet);
            SavePng(rtb, Path.Combine(outDir, $"settings-{language}-{page}.png"));
        }
        window.Close();
        Console.WriteLine($"settings ({language}) → {outDir}");
    }

    // ---------------------------------------------------------------- control styles

    /// <summary>One sheet: a row per control style, the buttons paused and then playing, on a dark taskbar.</summary>
    static async Task ControlStyles(string[] a)
    {
        string outPath = Path.GetFullPath(a[1]);
        double h = D(a[2]), scale = D(a[3]);
        var accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        var glyph = new SolidColorBrush(Colors.White);
        var settings = new AppSettings { ShowAlbumArt = false };
        var widget = new WidgetWindow(new MediaService(settings), accent, glyph) { Width = 400, Height = h };
        widget.Show();
        widget.SetArt(DemoContent.CreateArt(DemoContent.Tracks[0]));
        widget.SetAccent(accent.Color);
        // The whole window is snapped (an element's own snapshot loses its layout offset) and the
        // right end, where the pill sits, is cropped out of it.
        var root = (FrameworkElement)widget.FindName("Root");
        var shell = (FrameworkElement)widget.FindName("Pill");
        var inner = (FrameworkElement)widget.FindName("ControlsShell");

        const double labelW = 250, cellW = 170, rowH = 64;
        var styles = Enum.GetValues<ControlStyle>();
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1C, 0x21)), null, new Rect(0, 0, labelW + cellW * 2, rowH * styles.Length));
            var face = new Typeface("Segoe UI");
            for (int i = 0; i < styles.Length; i++)
            {
                settings.ControlStyle = styles[i];
                widget.ApplyStyle(settings, h, textShadow: false, vertical: false);
                for (int state = 0; state < 2; state++)
                {
                    widget.SetPlaying(state == 1, animate: false);
                    widget.SetProgress(0.62);
                    widget.UpdateLayout();
                    await Task.Delay(120);
                    if (state == 0) Console.WriteLine($"{styles[i],-8} pill {shell.ActualWidth:F1}  shell {inner.ActualWidth:F1} (desired {inner.DesiredSize.Width:F1})");
                    var full = Snap(root, scale);
                    int cropW = (int)Math.Round((shell.ActualWidth + 6) * scale);
                    var img = new CroppedBitmap(full, new Int32Rect(full.PixelWidth - cropW, 0, cropW, full.PixelHeight));
                    double w = img.PixelWidth / scale, hh = img.PixelHeight / scale;
                    dc.DrawImage(img, new Rect(labelW + cellW * state + (cellW - w) / 2, rowH * i + (rowH - hh) / 2, w, hh));
                }
                var label = SettingsOptions.ControlStyles.First(o => (ControlStyle)o.Value == styles[i]).Label.Split(':')[0];
                dc.DrawText(new FormattedText(label, Inv, FlowDirection.LeftToRight, face, 15, Brushes.White, 1.0),
                    new Point(16, rowH * i + rowH / 2 - 10));
            }
        }
        var rtb = new RenderTargetBitmap((int)((labelW + cellW * 2) * scale), (int)(rowH * styles.Length * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(sheet);
        SavePng(rtb, outPath);
        widget.Close();
        Console.WriteLine($"{styles.Length} control styles → {outPath}");
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
