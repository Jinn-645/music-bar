using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

public readonly record struct Palette(Color Primary, Color Secondary);

/// <summary>
/// Picks a vibrant dominant color (and a contrasting partner for gradients)
/// from album art or an app icon. Hue buckets are weighted by saturation and
/// by how close lightness is to the middle, so big dull areas lose to
/// smaller vivid ones.
/// </summary>
public static class ColorExtractor
{
    const int Buckets = 24;

    public static Palette? FromBitmap(BitmapSource source)
    {
        try
        {
            BitmapSource s = source;
            double scale = 56.0 / Math.Max(source.PixelWidth, source.PixelHeight);
            if (scale < 1) s = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            var bgra = new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            return Analyze(px);
        }
        catch (Exception ex)
        {
            Log.Error("Color extraction failed", ex);
            return null;
        }
    }

    static Palette? Analyze(byte[] px)
    {
        var weight = new double[Buckets];
        var r = new double[Buckets];
        var g = new double[Buckets];
        var b = new double[Buckets];
        int opaque = 0, colorful = 0;

        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] < 128) continue;
            opaque++;
            var c = Color.FromRgb(px[i + 2], px[i + 1], px[i]);
            ColorUtil.ToHsl(c, out var hue, out var sat, out var light);
            if (sat < 0.22 || light < 0.12 || light > 0.9) continue;
            colorful++;
            double wgt = sat * sat * Math.Max(0.05, 1 - Math.Abs(light - 0.5) * 1.5);
            int k = (int)(hue / 360 * Buckets) % Buckets;
            weight[k] += wgt;
            r[k] += c.R * wgt;
            g[k] += c.G * wgt;
            b[k] += c.B * wgt;
        }

        if (opaque == 0 || colorful < opaque * 0.03) return null;

        var smooth = new double[Buckets];
        for (int k = 0; k < Buckets; k++)
            smooth[k] = weight[k] + 0.5 * (weight[(k + Buckets - 1) % Buckets] + weight[(k + 1) % Buckets]);

        int first = ArgMax(smooth, -1);
        int second = -1;
        double best = 0;
        for (int k = 0; k < Buckets; k++)
        {
            int dist = Math.Min(Math.Abs(k - first), Buckets - Math.Abs(k - first));
            if (dist >= 3 && smooth[k] > best && smooth[k] >= smooth[first] * 0.12)
            {
                best = smooth[k];
                second = k;
            }
        }

        var primary = ColorUtil.ForDarkSurface(Average(first, weight, r, g, b));
        var secondary = second >= 0
            ? ColorUtil.ForDarkSurface(Average(second, weight, r, g, b))
            : ColorUtil.ShiftHue(primary, 28, -0.04);
        return new Palette(primary, secondary);
    }

    static int ArgMax(double[] values, int skip)
    {
        int idx = 0;
        for (int k = 1; k < values.Length; k++)
            if (k != skip && values[k] > values[idx]) idx = k;
        return idx;
    }

    static Color Average(int k, double[] w, double[] r, double[] g, double[] b)
    {
        double sw = w[k];
        if (sw <= 0) return ColorUtil.Brand;
        return Color.FromRgb((byte)(r[k] / sw), (byte)(g[k] / sw), (byte)(b[k] / sw));
    }
}
