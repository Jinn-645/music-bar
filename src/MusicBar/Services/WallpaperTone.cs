using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Interop;

namespace MusicBar.Services;

/// <summary>
/// How light the wallpaper is right behind a see-through taskbar, so text drawn there can go dark
/// on a bright picture and white on a dark one. The desktop window is asked to draw itself into a
/// bitmap (PrintWindow), which gives the wallpaper as shown, animated ones from Wallpaper Engine
/// included, without MusicBar's own windows on top. When that fails, the picture Windows names for
/// the monitor is read and laid out the way Windows lays it out (fill, fit, stretch, center, tile,
/// span, or a plain color).
/// </summary>
internal sealed class WallpaperTone
{
    const int MaxWidth = 640;
    const uint PW_RENDERFULLCONTENT = 2;

    bool _working;
    bool _logged;

    /// <summary>Perceived lightness behind the taskbar, 0 (black) to 100 (white); null until known or when Windows can't say.</summary>
    public double? Lightness { get; private set; }

    /// <summary>The lightness behind the taskbar changed noticeably.</summary>
    public event Action? Changed;

    /// <summary>Measures again, off the UI thread (a moving wallpaper can get lighter or darker any time).</summary>
    public void Check(Native.RECT taskbar, Native.RECT monitor)
    {
        if (_working) return;
        _working = true;
        Snapshot? shot = null;
        try
        {
            shot = Read(monitor);
        }
        catch (Exception ex)
        {
            if (!_logged) Log.Error("Reading the wallpaper settings failed", ex);
        }
        Task.Run(() =>
        {
            if (Capture(taskbar) is double seen) return (Value: (double?)seen, How: "desktop");
            return (Value: shot is { } s ? Measure(s, taskbar, monitor) : null, How: $"file {System.IO.Path.GetFileName(shot?.Path)}");
        }).ContinueWith(t =>
        {
            _working = false;
            if (t.Status != TaskStatus.RanToCompletion) return;
            var (value, how) = t.Result;
            if (!_logged && value is double v)
            {
                _logged = true;
                Log.Info($"Wallpaper behind the taskbar: lightness {v:0} (from the {how})");
            }
            bool changed = value.HasValue != Lightness.HasValue || (value is double a && Lightness is double b && Math.Abs(a - b) >= 2);
            Lightness = value;
            if (changed) Changed?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// The desktop as it is drawn, wallpaper and icons, without the windows above it; averaged over
    /// the taskbar. Null when the desktop can't be drawn (or comes back blank).
    /// </summary>
    static double? Capture(Native.RECT taskbar)
    {
        try
        {
            var desktop = FindWindow("Progman", null);
            if (desktop == IntPtr.Zero || !GetWindowRect(desktop, out var r) || r.Width <= 0 || r.Height <= 0) return null;
            using var bitmap = new System.Drawing.Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                var hdc = g.GetHdc();
                try
                {
                    if (!PrintWindow(desktop, hdc, PW_RENDERFULLCONTENT)) return null;
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            int left = Math.Max(0, taskbar.Left - r.Left), top = Math.Max(0, taskbar.Top - r.Top);
            int right = Math.Min(r.Width, taskbar.Right - r.Left), bottom = Math.Min(r.Height, taskbar.Bottom - r.Top);
            if (right - left < 4 || bottom - top < 2) return null;
            var area = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
            var data = bitmap.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[data.Stride];
                const int across = 160, down = 6;
                double sum = 0;
                int first = -1;
                bool uniform = true;
                for (int j = 0; j < down; j++)
                {
                    int y = (int)((j + 0.5) * area.Height / down);
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                    for (int i = 0; i < across; i++)
                    {
                        int at = (int)((i + 0.5) * area.Width / across) * 4;
                        int pixel = row[at] | row[at + 1] << 8 | row[at + 2] << 16;
                        if (first < 0) first = pixel;
                        else if (pixel != first) uniform = false;
                        sum += LightnessOf(row[at + 2], row[at + 1], row[at]);
                    }
                }
                // All one black or white: most likely nothing was drawn. A plain-color wallpaper is
                // read from its setting instead.
                if (uniform && (first == 0 || first == 0xFFFFFF)) return null;
                return sum / (across * down);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out Native.RECT rect);

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    readonly record struct Snapshot(string Path, Position Position, Color Background, Native.RECT Virtual);

    enum Position { Center, Tile, Stretch, Fit, Fill, Span }

    /// <summary>The picture on this monitor and how it is laid out, from Windows.</summary>
    static Snapshot? Read(Native.RECT monitor)
    {
        if (Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!) is not IDesktopWallpaper w) return null;
        try
        {
            string? id = null;
            long best = 0;
            if (w.GetMonitorDevicePathCount(out uint count) != 0) return null;
            for (uint i = 0; i < count; i++)
            {
                if (w.GetMonitorDevicePathAt(i, out var path) != 0 || path == null) continue;
                if (w.GetMonitorRECT(path, out var r) != 0) continue;
                long overlap = (long)Math.Max(0, Math.Min(r.Right, monitor.Right) - Math.Max(r.Left, monitor.Left))
                    * Math.Max(0, Math.Min(r.Bottom, monitor.Bottom) - Math.Max(r.Top, monitor.Top));
                if (overlap > best) (best, id) = (overlap, path);
            }
            string file = "";
            if (id != null && w.GetWallpaper(id, out var wp) == 0) file = wp ?? "";
            // An empty answer while a picture shows (a slideshow, Spotlight): Windows keeps the current one here.
            if (file.Length == 0 || !File.Exists(file))
            {
                var transcoded = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(transcoded) && Native.SystemParametersWallpaper().Length > 0) file = transcoded;
            }
            w.GetPosition(out int position);
            w.GetBackgroundColor(out uint bg);
            var color = Color.FromRgb((byte)(bg & 0xFF), (byte)((bg >> 8) & 0xFF), (byte)((bg >> 16) & 0xFF));
            var screen = new Native.RECT(
                Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN) + Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
                Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN) + Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
            return new Snapshot(file, Enum.IsDefined((Position)position) ? (Position)position : Position.Fill, color, screen);
        }
        finally
        {
            Marshal.ReleaseComObject(w);
        }
    }

    /// <summary>The average perceived lightness of the picture where the taskbar covers it.</summary>
    static double? Measure(Snapshot s, Native.RECT taskbar, Native.RECT monitor)
    {
        double background = LightnessOf(s.Background.R, s.Background.G, s.Background.B);
        if (s.Path.Length == 0 || !File.Exists(s.Path)) return background;
        try
        {
            BitmapSource image;
            int width, height;
            using (var stream = File.OpenRead(s.Path))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
                (width, height) = (frame.PixelWidth, frame.PixelHeight);
                double k = Math.Min(1, (double)MaxWidth / width);
                BitmapSource scaled = k < 1 ? new TransformedBitmap(frame, new ScaleTransform(k, k)) : frame;
                image = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            }
            int iw = image.PixelWidth, ih = image.PixelHeight;
            var pixels = new byte[iw * ih * 4];
            image.CopyPixels(pixels, iw * 4, 0);
            double toSmall = (double)iw / width;

            // Where the picture lands: its scale and top-left corner, in screen pixels.
            var area = s.Position == Position.Span ? s.Virtual : monitor;
            double aw = area.Width, ah = area.Height;
            double sx, sy;
            switch (s.Position)
            {
                case Position.Fill or Position.Span: sx = sy = Math.Max(aw / width, ah / height); break;
                case Position.Fit: sx = sy = Math.Min(aw / width, ah / height); break;
                case Position.Stretch: (sx, sy) = (aw / width, ah / height); break;
                default: sx = sy = 1; break;
            }
            double ox = area.Left + (aw - width * sx) / 2, oy = area.Top + (ah - height * sy) / 2;
            if (s.Position == Position.Stretch) (ox, oy) = (area.Left, area.Top);

            // A grid of samples over the taskbar.
            const int across = 96, down = 6;
            double sum = 0;
            for (int j = 0; j < down; j++)
            {
                for (int i = 0; i < across; i++)
                {
                    double x = taskbar.Left + (i + 0.5) * taskbar.Width / across;
                    double y = taskbar.Top + (j + 0.5) * taskbar.Height / down;
                    double u, v;
                    if (s.Position == Position.Tile)
                    {
                        u = Mod(x - monitor.Left, width);
                        v = Mod(y - monitor.Top, height);
                    }
                    else
                    {
                        u = (x - ox) / sx;
                        v = (y - oy) / sy;
                    }
                    if (u < 0 || v < 0 || u >= width || v >= height)
                    {
                        sum += background;
                        continue;
                    }
                    int px = Math.Min(iw - 1, (int)(u * toSmall)), py = Math.Min(ih - 1, (int)(v * toSmall));
                    int at = (py * iw + px) * 4;
                    sum += LightnessOf(pixels[at + 2], pixels[at + 1], pixels[at]);
                }
            }
            return sum / (across * down);
        }
        catch (Exception ex)
        {
            Log.Error($"Measuring the wallpaper {s.Path} failed", ex);
            return null;
        }
    }

    static double Mod(double a, double m) => ((a % m) + m) % m;

    /// <summary>CIE L* of an sRGB color: 0 black, 100 white, about 50 a middle grey to the eye.</summary>
    static double LightnessOf(byte r, byte g, byte b)
    {
        static double Linear(byte c)
        {
            double v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        double y = 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
        return y > 0.008856 ? 116 * Math.Cbrt(y) - 16 : 903.3 * y;
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string? wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string? monitorId);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Native.RECT rect);
        [PreserveSig] int SetBackgroundColor(uint color);
        [PreserveSig] int GetBackgroundColor(out uint color);
        [PreserveSig] int SetPosition(int position);
        [PreserveSig] int GetPosition(out int position);
    }
}
