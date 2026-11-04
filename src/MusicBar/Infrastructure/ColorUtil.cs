using System;
using System.Windows.Media;

namespace MusicBar.Infrastructure;

public static class ColorUtil
{
    public static readonly Color Brand = Color.FromRgb(0xFF, 0x6B, 0x6B);

    public static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static void ToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2;
        if (max - min < 1e-6)
        {
            h = 0; s = 0;
            return;
        }
        double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60;
    }

    public static Color FromHsl(double h, double s, double l, byte a = 255)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-6)
        {
            byte v = (byte)Math.Round(l * 255);
            return Color.FromArgb(a, v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromArgb(a,
            (byte)Math.Round(Hue(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255),
            (byte)Math.Round(Hue(p, q, h - 1.0 / 3) * 255));
    }

    static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    /// <summary>Clamp saturation/lightness so the color reads well on a near-black bar.</summary>
    public static Color ForDarkSurface(Color c)
    {
        ToHsl(c, out var h, out var s, out var l);
        if (s < 0.08) return FromHsl(h, s, Math.Clamp(l, 0.72, 0.85));
        return FromHsl(h, Math.Clamp(s, 0.55, 0.95), Math.Clamp(l, 0.58, 0.70));
    }

    public static Color ShiftHue(Color c, double degrees, double lightnessDelta = 0)
    {
        ToHsl(c, out var h, out var s, out var l);
        return FromHsl(h + degrees, s, Math.Clamp(l + lightnessDelta, 0, 1));
    }

    public static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        try
        {
            if (ColorConverter.ConvertFromString(t) is Color c)
            {
                color = c;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        return false;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color SystemAccent()
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            var a = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2);
            return Color.FromRgb(a.R, a.G, a.B);
        }
        catch (Exception)
        {
            return Color.FromRgb(0x60, 0xCD, 0xFF);
        }
    }
}
