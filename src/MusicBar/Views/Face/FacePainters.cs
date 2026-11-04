using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using MusicBar.Infrastructure;

namespace MusicBar.Views.Face;

/// <summary>
/// The face sets, and the signs they share. Painters draw in design units (the promo film's
/// 100 x 72 face box). Brushes, pens and fixed shapes are built once and frozen; colors that change
/// at run time (ink, accent) are kept per color, so a frame only allocates a few small geometries
/// for the parts that move.
/// </summary>
public static class FacePainters
{
    static readonly FacePainter?[] Painters = new FacePainter?[Enum.GetValues<FaceStyle>().Length];

    /// <summary>The painter for a face set (cached: one instance per style).</summary>
    public static FacePainter For(FaceStyle style)
    {
        int i = (int)style;
        if (i < 0 || i >= Painters.Length) return For(FaceStyle.Classic);
        return Painters[i] ??= style switch
        {
            FaceStyle.ClassicGirl => new ClassicGirlFace(),
            FaceStyle.AnimeGirl => new AnimeGirlFace(),
            FaceStyle.AnimeBoy => new AnimeBoyFace(),
            FaceStyle.Alien => new AlienFace(),
            FaceStyle.Robot => new RobotFace(),
            FaceStyle.Cat => new CatFace(),
            FaceStyle.Derp => new DerpFace(),
            _ => new ClassicFace(),
        };
    }

    /// <summary>The shared signs around the face (<see cref="FaceExtras"/>), in design units, animated by <see cref="FaceFrame.Time"/>.</summary>
    public static void DrawExtras(DrawingContext dc, in FaceFrame f) => FaceSet.DrawSigns(dc, f);
}

/// <summary>
/// What every set shares: the drawing order, eyes that blink (see <see cref="Eye"/>), the eye shapes
/// most sets draw alike (hearts, stars, spirals, "^ ^", "&gt; &lt;", welling tears), brows, cheeks,
/// and the signs. A set supplies its open eye and its own features.
/// </summary>
abstract class FaceSet : FacePainter
{
    protected enum Mood { Plain, Wide, Teary }

    protected static readonly Brush White = Paints.Solid(0xFFFDF6);
    protected static readonly Brush Dark = Paints.Solid(0x15171E);
    protected static readonly Brush Shine = Paints.Solid(0xFFFFFF);
    protected static readonly Pen Rim = Paints.MakePen(Paints.Rgb(0x15171E), 2);
    static readonly Brush Welling = Paints.Solid(0x7CC8FF, 0.8);
    static readonly Brush HeartRed = Paints.Solid(0xFF4D7A);
    static readonly Brush StarGold = Paints.Solid(0xFFD23F);
    static readonly Brush[] Blushes = MakeBlushes();

    protected static readonly Geometry HeartShape = Parse("M0,0.95 C-0.3,0.72 -1,0.28 -1,-0.3 C-1,-0.72 -0.7,-0.96 -0.42,-0.96 C-0.2,-0.96 -0.06,-0.84 0,-0.64 C0.06,-0.84 0.2,-0.96 0.42,-0.96 C0.7,-0.96 1,-0.72 1,-0.3 C1,0.28 0.3,0.72 0,0.95 Z");
    protected static readonly Geometry StarShape = MakeStar(1, 0.47);
    protected static readonly Geometry SparkleShape = Parse("M0,-1 Q0.13,-0.13 1,0 Q0.13,0.13 0,1 Q-0.13,0.13 -1,0 Q-0.13,-0.13 0,-1 Z");
    protected static readonly Geometry DropShape = Parse("M0,-1.7 C0.4,-0.9 1,-0.25 1,0.35 A1,1 0 0 1 -1,0.35 C-1,-0.25 -0.4,-0.9 0,-1.7 Z");
    static readonly Geometry StarGlint = Parse("M-0.3,-0.58 Q-0.27,-0.33 -0.04,-0.3 Q-0.27,-0.27 -0.3,-0.02 Q-0.33,-0.27 -0.56,-0.3 Q-0.33,-0.33 -0.3,-0.58 Z");

    protected Eye Left = null!, Right = null!;
    protected MouthKit Mouth = null!;
    protected Point CheekLeft = new(29, 43), CheekRight = new(69, 43);
    protected double BlushBoost;

    public override void Draw(DrawingContext dc, in FaceFrame f)
    {
        DrawBehind(dc, f);
        DrawBlush(dc, f);
        DrawEye(dc, f, Left, f.LidLeft);
        DrawEye(dc, f, Right, f.LidRight);
        DrawBrows(dc, f);
        DrawMouth(dc, f);
        DrawFront(dc, f);
    }

    /// <summary>Features behind the face (ears, antennae).</summary>
    protected virtual void DrawBehind(DrawingContext dc, in FaceFrame f) { }

    /// <summary>Features over the face (hair, a bow, whiskers).</summary>
    protected virtual void DrawFront(DrawingContext dc, in FaceFrame f) { }

    /// <summary>The color of lines drawn straight on the taskbar.</summary>
    protected virtual Color LineInk(in FaceFrame f) => f.Ink;

    protected virtual void DrawEye(DrawingContext dc, in FaceFrame f, Eye e, double lid)
    {
        switch (f.Eyes)
        {
            case EyeShape.Happy:
                DrawHappy(dc, f, e);
                break;
            case EyeShape.Squeeze:
                dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 3.2), e.Squeeze);
                break;
            case EyeShape.Spiral:
                DrawSpiral(dc, e, LineInk(f), f.Time);
                break;
            case EyeShape.Heart:
                DrawHeartEye(dc, e, f.Time);
                break;
            case EyeShape.Star:
                DrawStarEye(dc, e, f.Time);
                break;
            case EyeShape.Wide:
                dc.PushTransform(e.Wide);
                DrawOpen(dc, f, e, 0, Mood.Wide);
                dc.Pop();
                break;
            case EyeShape.Teary:
                DrawOpen(dc, f, e, Clamp01(lid), Mood.Teary);
                break;
            default:
                DrawOpen(dc, f, e, Clamp01(lid), Mood.Plain);
                break;
        }
    }

    /// <summary>An open eye cut by its lid: the set's own eye inside, then the shut line as the lid closes.</summary>
    protected void DrawOpen(DrawingContext dc, in FaceFrame f, Eye e, double lid, Mood mood, double shutWidth = 3)
    {
        var open = e.Visible(lid);
        if (open != null)
        {
            dc.DrawGeometry(EyeFill(f), null, open);
            dc.PushClip(open);
            DrawInside(dc, f, e, mood);
            if (mood == Mood.Teary) DrawWelling(dc, e, f.Time);
            dc.Pop();
            dc.DrawGeometry(null, EyeRim(f), open);
        }
        DrawLid(dc, f, e, lid);
        e.DrawShut(dc, LineInk(f), lid, shutWidth);
    }

    protected virtual Brush EyeFill(in FaceFrame f) => White;

    protected virtual Pen EyeRim(in FaceFrame f) => Rim;

    /// <summary>What shows inside the open eye (pupils, irises, highlights), clipped to it.</summary>
    protected abstract void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood);

    /// <summary>Anything that rides on the lid's edge (lashes); also called once the eye is shut.</summary>
    protected virtual void DrawLid(DrawingContext dc, in FaceFrame f, Eye e, double lid) { }

    protected virtual void DrawHappy(DrawingContext dc, in FaceFrame f, Eye e)
        => dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 3.5), e.Happy);

    protected virtual void DrawBrows(DrawingContext dc, in FaceFrame f)
    {
        double a = Ramp(Math.Abs(f.Brows), 0.05, 0.3);
        if (a > 0) DrawBrowPair(dc, f, Paints.Stroke(LineInk(f), 2.6, a), 11.5, 6.5, 1, 3.5);
    }

    protected virtual void DrawMouth(DrawingContext dc, in FaceFrame f) => Mouth.Draw(dc, f, f.Mouth, LineInk(f));

    protected virtual void DrawBlush(DrawingContext dc, in FaceFrame f)
    {
        double a = Clamp01(f.Blush + BlushBoost);
        if (a < 0.03) return;
        var brush = BlushBrush(a);
        dc.DrawEllipse(brush, null, CheekLeft, 5.5, 3.2);
        dc.DrawEllipse(brush, null, CheekRight, 5.5, 3.2);
    }

    // ------------------------------------------------------------------ shared parts

    protected static Vector Gaze(in FaceFrame f) => new(Math.Clamp(f.Look.X, -1, 1), Math.Clamp(f.Look.Y, -1, 1));

    protected static double PupilSize(in FaceFrame f, Mood mood)
        => Math.Clamp(f.Pupil, 0.5, 1.5) * mood switch { Mood.Wide => 0.68, Mood.Teary => 1.15, _ => 1 };

    /// <summary>The promo film's pupil: a dark oval looking along <see cref="FaceFrame.Look"/>, with a shine.</summary>
    protected static void DrawPupil(DrawingContext dc, in FaceFrame f, Eye e, Mood mood)
    {
        double size = PupilSize(f, mood);
        var g = Gaze(f);
        var p = new Point(e.C.X + g.X * 3, e.C.Y + 1 + g.Y * 2.5);
        dc.DrawEllipse(Dark, null, p, 4 * size, 5 * size);
        dc.DrawEllipse(Shine, null, new Point(p.X - 1.6 * size, p.Y - 2.2 * size), 1.5, 1.5);
        if (mood == Mood.Teary) dc.DrawEllipse(Shine, null, new Point(p.X + 1.7 * size, p.Y + 2.1 * size), 0.9, 0.9);
    }

    /// <summary>Tears welling up: a wavy pool of water across the bottom of the eye.</summary>
    protected static void DrawWelling(DrawingContext dc, Eye e, double t)
    {
        double top = e.C.Y + e.Ry * 0.4, wave = e.Ry * 0.075, half = e.Rx + 2;
        var pool = new StreamGeometry();
        using (var g = pool.Open())
        {
            g.BeginFigure(new Point(e.C.X - half, e.C.Y + e.Ry + 3), true, true);
            for (int i = 0; i <= 10; i++)
            {
                double u = i / 5.0 - 1;
                g.LineTo(new Point(e.C.X + u * half, top + wave * Math.Sin(u * 3.2 + t * 3.4)), false, true);
            }
            g.LineTo(new Point(e.C.X + half, e.C.Y + e.Ry + 3), false, true);
        }
        pool.Freeze();
        dc.DrawGeometry(Welling, null, pool);
        double k = e.Ry / 12;
        dc.DrawEllipse(Shine, null, new Point(e.C.X + e.Rx * 0.3 + 0.6 * k * Math.Sin(t * 2.1), top + 1.8 * k), 1.4 * k, 0.7 * k);
    }

    protected static void DrawHeartEye(DrawingContext dc, Eye e, double t)
    {
        double beat = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.2)), 4);
        dc.PushTransform(Place(e.C.X, e.C.Y + 0.5, e.Sign * (1 + 0.1 * beat), 0));
        dc.DrawGeometry(HeartRed, e.SignRim, HeartShape);
        dc.DrawEllipse(Shine, null, new Point(-0.46, -0.44), 0.2, 0.14);
        dc.Pop();
    }

    protected static void DrawStarEye(DrawingContext dc, Eye e, double t)
    {
        dc.PushTransform(Place(e.C.X, e.C.Y + 0.8, e.Sign * 1.08 * (1 + 0.06 * Math.Sin(t * 5)), 9 * Math.Sin(t * 2.2)));
        dc.DrawGeometry(StarGold, e.SignRim, StarShape);
        dc.DrawGeometry(Shine, null, StarGlint);
        dc.Pop();
    }

    protected static void DrawSpiral(DrawingContext dc, Eye e, Color ink, double t)
    {
        dc.PushTransform(Place(e.C.X, e.C.Y, 1, (e.IsLeft ? 1 : -1) * t * 260));
        dc.DrawGeometry(null, Paints.Stroke(ink, 2.2), e.Spiral);
        dc.Pop();
    }

    /// <summary>Short strokes from the outer part of the lid's edge, pointing up, and down once the eye is shut.</summary>
    protected static void DrawLashes(DrawingContext dc, Pen pen, Eye e, double lid, double[] at, double[] length, double spread)
    {
        double side = e.IsLeft ? -1 : 1;
        double down = Smooth(Ramp(lid, 0.45, 1));
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < at.Length; i++)
            {
                var p = e.EdgeAt(lid, side * at[i]);
                var d = new Vector(side * (0.3 + spread * at[i]), -1 + 1.9 * down);
                d.Normalize();
                g.BeginFigure(p + d * 1.2, false, false);
                g.LineTo(p + d * (1.2 + length[i]), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    /// <summary>A pair of brows; <see cref="FaceFrame.Brows"/> pulls the inner ends down (angry) or up (worried).</summary>
    protected void DrawBrowPair(DrawingContext dc, in FaceFrame f, Pen pen, double y, double half, double arch, double lift, double rest = 0)
    {
        double b = Math.Clamp(f.Brows, -1, 1);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            AddBrow(g, Left, b, y, half, arch, lift, rest);
            AddBrow(g, Right, b, y, half, arch, lift, rest);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    /// <param name="rest">How much higher the outer end sits when the brows are relaxed.</param>
    protected static void AddBrow(StreamGeometryContext g, Eye e, double b, double y, double half, double arch, double lift, double rest)
    {
        double inward = e.IsLeft ? 1 : -1;
        var outer = new Point(e.C.X - inward * half, y - rest + b * 1.2);
        var inner = new Point(e.C.X + inward * half, y - b * lift);
        g.BeginFigure(outer, false, false);
        g.QuadraticBezierTo(new Point((outer.X + inner.X) / 2, (outer.Y + inner.Y) / 2 - arch * 2), inner, true, true);
    }

    protected static Brush BlushBrush(double amount) => Blushes[(int)Math.Round(Clamp01(amount) * 16)];

    static Brush[] MakeBlushes()
    {
        // Seventeen strengths, so cheeks fade in without a brush per frame.
        var levels = new Brush[17];
        for (int i = 0; i < levels.Length; i++)
        {
            double a = i / 16.0;
            levels[i] = Paints.Frozen(new RadialGradientBrush(new GradientStopCollection
            {
                new(Paints.Rgb(0xFF7A9C, 0.8 * a), 0),
                new(Paints.Rgb(0xFF7A9C, 0.55 * a), 0.55),
                new(Paints.Rgb(0xFF7A9C, 0), 1),
            }));
        }
        return levels;
    }

    // ------------------------------------------------------------------ signs

    static readonly Color SignRed = Paints.Rgb(0xFF5A5A);
    static readonly Color SweatBlue = Paints.Rgb(0x8FD3FF);
    static readonly Color HeartPink = Paints.Rgb(0xFF6B9A);
    static readonly Color TearBlue = Paints.Rgb(0x4FAEF0);
    static readonly Color TearLight = Paints.Rgb(0xD8F1FF);
    static readonly Pen AngerPen = Paints.MakePen(SignRed, 2.3);
    static readonly Pen SweatEdge = Paints.MakePen(Paints.Rgb(0x3D8FD1), 0.9 / 2.8);
    static readonly Geometry NoteShape = Parse("F1 M-2.13,0.86 A2.3,1.65 -22 1 1 2.13,-0.86 A2.3,1.65 -22 1 1 -2.13,0.86 Z M1.15,-0.2 L1.15,-9 L2.25,-9 L2.25,-0.6 Z M2.25,-9 C2.6,-6.8 5.2,-6.2 4.5,-2.9 C4.3,-4.9 3.4,-5.4 2.25,-6.1 Z");
    static readonly Geometry QuestionHook = Parse("M-2.6,-3.4 C-2.6,-6.4 2.8,-6.8 2.8,-3.4 C2.8,-1.2 0.2,-0.9 0.2,1.4");
    static readonly Point[] SparkleSpots = { new(9, 8), new(91, 15), new(4, 47), new(95, 53) };

    internal static void DrawSigns(DrawingContext dc, in FaceFrame f)
    {
        var x = f.Extras;
        if (x == FaceExtras.None) return;
        double t = f.Time;
        var ink = f.Ink;
        if ((x & FaceExtras.Tears) != 0) Tears(dc, t);
        if ((x & FaceExtras.Steam) != 0) Steam(dc, ink, t);
        if ((x & FaceExtras.Sweat) != 0) Sweat(dc, t);
        if ((x & FaceExtras.Water) != 0) WaterGlass(dc, ink, t);
        if ((x & FaceExtras.NoSignal) != 0) NoSignal(dc, ink, t);
        if ((x & FaceExtras.Bolt) != 0) Bolt(dc, t);
        if ((x & FaceExtras.Sparkle) != 0) Sparkles(dc, ink, t);
        if ((x & FaceExtras.Hearts) != 0) Hearts(dc, t);
        if ((x & FaceExtras.Zzz) != 0) Zzz(dc, ink, t);
        if ((x & FaceExtras.Notes) != 0) Notes(dc, ink, f.Accent, t);
        if ((x & FaceExtras.Anger) != 0) Anger(dc, t);
        if ((x & FaceExtras.Question) != 0) Question(dc, ink, t);
        if ((x & FaceExtras.Exclaim) != 0) Exclaim(dc, ink, t);
    }

    /// <summary>Fades a looping sign in at the start of its loop and out at the end, so the loop has no seam.</summary>
    static double InOut(double p, double fadeIn = 0.15, double fadeOut = 0.68) => Ramp(p, 0, fadeIn) * (1 - Ramp(p, fadeOut, 1));

    static void Zzz(DrawingContext dc, Color ink, double t)
    {
        for (int i = 0; i < 3; i++)
        {
            double p = Frac(t / 3.0 + i / 3.0);
            double a = InOut(p);
            if (a < 0.02) continue;
            double h = (3.6 + p * 4.4) / 2;
            double cx = 76 + p * 20 + Math.Sin(p * Math.PI * 2) * 1.6, cy = 21 - p * 24;
            var z = new StreamGeometry();
            using (var g = z.Open())
            {
                g.BeginFigure(new Point(cx - h, cy - h), false, false);
                g.LineTo(new Point(cx + h, cy - h), true, true);
                g.LineTo(new Point(cx - h, cy + h), true, true);
                g.LineTo(new Point(cx + h, cy + h), true, true);
            }
            z.Freeze();
            dc.DrawGeometry(null, Paints.Stroke(ink, 1.5 + p, a), z);
        }
    }

    static void Notes(DrawingContext dc, Color ink, Color accent, double t)
    {
        var color = ColorUtil.Lerp(ink, ColorUtil.ForDarkSurface(accent), 0.7);
        for (int i = 0; i < 2; i++)
        {
            double p = Frac(t / 2.6 + i * 0.5);
            double a = InOut(p);
            if (a < 0.02) continue;
            double x = 81 + i * 9 + Math.Sin(p * Math.PI * 2 + i * 2) * 2.4, y = 22 - p * 20;
            dc.PushTransform(Place(x, y, 1 + 0.08 * i, 14 * Math.Sin(p * Math.PI * 2 + i)));
            dc.DrawGeometry(Paints.Fill(color, a), null, NoteShape);
            dc.Pop();
        }
    }

    static void Anger(DrawingContext dc, double t)
    {
        // The "popping vein": four corners bent toward the middle, swelling now and then.
        double s = 1 + 0.12 * Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.2)), 2);
        const double cx = 86, cy = 4;
        double gap = 1.2 * s, arm = 3.8 * s;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int q = 0; q < 4; q++)
            {
                double sx = q is 0 or 3 ? -1 : 1, sy = q < 2 ? -1 : 1;
                g.BeginFigure(new Point(cx + sx * gap, cy + sy * (gap + arm)), false, false);
                g.QuadraticBezierTo(new Point(cx + sx * gap, cy + sy * gap), new Point(cx + sx * (gap + arm), cy + sy * gap), true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, AngerPen, geo);
    }

    static void Sweat(DrawingContext dc, double t)
    {
        double p = Frac(t / 1.8);
        double a = InOut(p, 0.12, 0.8);
        if (a < 0.02) return;
        double y = 7 + 4 * Smooth(p);
        dc.PushTransform(Place(78, y, 2.8, 0));
        dc.DrawGeometry(Paints.Fill(SweatBlue, a), Paints.Fade(SweatEdge, a), DropShape);
        dc.Pop();
        dc.DrawEllipse(Paints.Fill(Colors.White, a * 0.9), null, new Point(76.9, y + 0.6), 0.7, 0.95);
    }

    static void Hearts(DrawingContext dc, double t)
    {
        for (int i = 0; i < 3; i++)
        {
            double p = Frac(t / 2.4 + i / 3.0);
            double a = InOut(p);
            if (a < 0.02) continue;
            double x = (i switch { 0 => 86, 1 => 12, _ => 95 }) + 2.2 * Math.Sin(p * Math.PI * 2 + i * 2);
            dc.PushTransform(Place(x, 20 - p * 23, 2.2 + 1.2 * p, 10 * Math.Sin(p * Math.PI * 2 + i)));
            dc.DrawGeometry(Paints.Fill(HeartPink, a), null, HeartShape);
            dc.Pop();
        }
    }

    static void Sparkles(DrawingContext dc, Color ink, double t)
    {
        var brush = Paints.Fill(ink);
        for (int i = 0; i < SparkleSpots.Length; i++)
        {
            // Each grows and shrinks in turn, a quarter of the loop after the last.
            double s = 4.2 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * 2 * (t / 1.8 - i / 4.0))), 1.4);
            if (s < 0.2) continue;
            dc.PushTransform(Place(SparkleSpots[i].X, SparkleSpots[i].Y, s, 15 * Math.Sin(t + i)));
            dc.DrawGeometry(brush, null, SparkleShape);
            dc.Pop();
        }
    }

    static void Question(DrawingContext dc, Color ink, double t)
    {
        dc.PushTransform(Place(86, 3 + 1.4 * Math.Sin(t * 4.2), 1, 6 * Math.Sin(t * 2.1)));
        dc.DrawGeometry(null, Paints.Stroke(ink, 2.4), QuestionHook);
        dc.DrawEllipse(Paints.Fill(ink), null, new Point(0.2, 4.4), 1.35, 1.35);
        dc.Pop();
    }

    static void Exclaim(DrawingContext dc, Color ink, double t)
    {
        double pop = 1 + 0.18 * Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 0.9)), 8);
        dc.PushTransform(Place(94, 2, pop, 7 * Math.Sin(t * 7)));
        dc.DrawLine(Paints.Stroke(ink, 2.8), new Point(0, -6.4), new Point(0, 0.8));
        dc.DrawEllipse(Paints.Fill(ink), null, new Point(0, 4.4), 1.5, 1.5);
        dc.Pop();
    }

    static void Tears(DrawingContext dc, double t)
    {
        var stream = Paints.Stroke(TearBlue, 3.4, 0.6);
        var core = Paints.Stroke(TearLight, 1.2, 0.9);
        for (int side = 0; side < 2; side++)
        {
            double x0 = side == 0 ? 36 : 62;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(x0 + Wobble(40), 40), false, false);
                for (int i = 1; i <= 12; i++)
                {
                    double y = 40 + i * 2.6;
                    g.LineTo(new Point(x0 + Wobble(y), y), true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, stream, geo);
            dc.DrawGeometry(null, core, geo);
            for (int k = 0; k < 3; k++)
            {
                double q = Frac(t / 0.9 + k / 3.0 + side * 0.17);
                double y = 41 + q * 29;
                dc.DrawEllipse(Paints.Fill(TearLight, 1 - Ramp(q, 0.75, 1)), null, new Point(x0 + Wobble(y), y), 1.6, 2.2);
            }
        }

        double Wobble(double y) => 0.9 * Math.Sin(y * 0.33 - t * 5);
    }

    static readonly Brush BoltYellow = Paints.Solid(0xFFD23F);
    static readonly Pen BoltEdge = Paints.MakePen(Paints.Rgb(0x15171E), 1.1);
    static readonly Pen BoltSpark = Paints.MakePen(Paints.Rgb(0xFFB800), 1.2);
    static readonly Geometry BoltShape = Parse("M1.6,-7 L-4,0.9 L-0.5,0.9 L-1.8,7 L4,-1.3 L0.5,-1.3 Z");
    static readonly Geometry BoltSparks = Parse("M-5.6,-4.2 L-7.8,-5.4 M5.8,2.6 L8,3.6 M4.6,-6.4 L5.8,-8.4 M-4.6,4.4 L-6,6.2");
    static readonly Pen SlashPen = Paints.MakePen(SignRed, 2.3);
    static readonly Geometry[] WifiArcs = { WifiArc(3.4), WifiArc(6.5), WifiArc(9.6) };
    static readonly Geometry WifiSlash = Parse("M-6.4,-10.2 L6.4,1.6");
    static readonly Geometry GlassShape = Parse("M-4.4,-5.6 L4.4,-5.6 L3.3,5.3 Q3.2,6.4 2.2,6.4 L-2.2,6.4 Q-3.2,6.4 -3.3,5.3 Z");
    static readonly Geometry GlassShine = Parse("M-2.9,-3.6 L-2.4,3.4");
    static readonly Brush WaterBlue = Paints.Solid(0x4FAEF0, 0.92);
    static readonly Pen ShinePen = Paints.MakePen(Paints.Rgb(0xFFFFFF, 0.8), 0.9);
    static readonly Color SteamWhite = Paints.Rgb(0xF4F6FA);

    static void Bolt(DrawingContext dc, double t)
    {
        // Bobs gently, and every so often zaps: a jolt, a flicker of sparks.
        double zap = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 / 1.6)), 10);
        dc.PushTransform(Place(90 + 0.8 * zap * Math.Sin(t * 70), 7 + 1.2 * Math.Sin(t * 2.6), 1.35 * (1 + 0.16 * zap), 12 + 5 * zap));
        dc.DrawGeometry(BoltYellow, BoltEdge, BoltShape);
        if (zap > 0.2) dc.DrawGeometry(null, Paints.Fade(BoltSpark, zap), BoltSparks);
        dc.Pop();
    }

    static void NoSignal(DrawingContext dc, Color ink, double t)
    {
        // The arcs light up from the dot outward, looking for a network; the red slash says there is none.
        dc.PushTransform(Place(89, 11 + Math.Sin(t * 2.2), 1.3, 0));
        double p = Frac(t / 1.8);
        dc.DrawEllipse(Paints.Fill(ink), null, new Point(0, 0), 1.4, 1.4);
        for (int i = 0; i < WifiArcs.Length; i++)
        {
            double lit = Math.Max(0, 1 - Math.Abs(p * 4.5 - 1 - i));
            dc.DrawGeometry(null, Paints.Stroke(ink, 1.9, 0.3 + 0.7 * lit), WifiArcs[i]);
        }
        dc.DrawGeometry(null, SlashPen, WifiSlash);
        dc.Pop();
    }

    static void WaterGlass(DrawingContext dc, Color ink, double t)
    {
        // A glass that bobs and sways a little, the water sloshing, a bubble rising now and then.
        dc.PushTransform(Place(91, 8 + 1.2 * Math.Sin(t * 2.4), 1.4, 5 * Math.Sin(t * 1.7)));
        dc.DrawGeometry(Paints.Fill(ink, 0.14), null, GlassShape);
        var water = new StreamGeometry();
        using (var g = water.Open())
        {
            g.BeginFigure(new Point(-5, 7), true, true);
            for (int i = 0; i <= 8; i++)
            {
                double x = -5 + i * 1.25;
                g.LineTo(new Point(x, -0.8 + 0.55 * Math.Sin(x * 0.9 + t * 4.2)), true, true);
            }
            g.LineTo(new Point(5, 7), true, true);
        }
        water.Freeze();
        dc.PushClip(GlassShape);
        dc.DrawGeometry(WaterBlue, null, water);
        double q = Frac(t / 1.3);
        dc.DrawEllipse(Paints.Fill(Colors.White, 0.85 * (1 - q)), null, new Point(0.9 + 0.7 * Math.Sin(q * 7), 5 - q * 5.2), 0.65, 0.65);
        dc.Pop();
        dc.DrawGeometry(null, ShinePen, GlassShine);
        dc.DrawGeometry(null, Paints.Stroke(ink, 1.2), GlassShape);
        dc.Pop();
    }

    static void Steam(DrawingContext dc, Color ink, double t)
    {
        // Puffs rise off both sides of the top of the head, swelling and drifting outward as they fade.
        for (int side = 0; side < 2; side++)
        {
            double dir = side == 0 ? -1 : 1;
            for (int k = 0; k < 2; k++)
            {
                // Puffs swell in and shrink away rather than fade, so the rim stays hidden where the lobes overlap.
                double p = Frac(t / 1.6 + k * 0.5 + side * 0.25);
                double size = (3 + 3.4 * p) * Smooth(Ramp(p, 0, 0.14)) * (1 - Smooth(Ramp(p, 0.62, 1)));
                if (size < 0.4) continue;
                var c = new Point(49 + dir * (20 + 10 * p) + 0.8 * Math.Sin(p * 6 + side), 9 - 13 * p);
                // White, rimmed in ink: the rim goes down first and the fill covers its inner arcs.
                var puff = Puff(c, size);
                dc.DrawGeometry(null, Paints.Stroke(ink, 1.5, 0.6), puff);
                dc.DrawGeometry(Paints.Fill(SteamWhite), null, puff);
            }
        }
    }

    /// <summary>A puff of steam: three round lobes as one shape, filled once so the overlaps don't show.</summary>
    static StreamGeometry Puff(Point c, double r)
    {
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var g = geo.Open())
        {
            Circle(g, new Point(c.X, c.Y - 0.2 * r), 0.62 * r);
            Circle(g, new Point(c.X - 0.55 * r, c.Y + 0.15 * r), 0.45 * r);
            Circle(g, new Point(c.X + 0.55 * r, c.Y + 0.18 * r), 0.42 * r);
        }
        geo.Freeze();
        return geo;

        static void Circle(StreamGeometryContext g, Point c, double r)
        {
            g.BeginFigure(new Point(c.X - r, c.Y), true, true);
            g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            g.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }
    }

    /// <summary>One Wi-Fi arc around the dot at the origin, a quarter turn wide, opening upward.</summary>
    static Geometry WifiArc(double r) => Build(g =>
    {
        double d = r * Math.Sqrt(0.5);
        g.BeginFigure(new Point(-d, -d), false, false);
        g.ArcTo(new Point(d, -d), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
    });

    // ------------------------------------------------------------------ helpers

    internal static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    internal static double Ramp(double v, double from, double to) => Math.Clamp((v - from) / (to - from), 0, 1);

    internal static double Smooth(double k) => k * k * (3 - 2 * k);

    internal static double Frac(double v) => v - Math.Floor(v);

    internal static double Hash(double n)
    {
        double s = Math.Sin(n * 127.1 + 311.7) * 43758.5453;
        return s - Math.Floor(s);
    }

    internal static Point Lerp(Point a, Point b, double k) => new(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);

    /// <summary>Places a shape drawn around the origin: scaled, turned (degrees, clockwise) and moved.</summary>
    internal static MatrixTransform Place(double x, double y, double scale, double degrees)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r) * scale, s = Math.Sin(r) * scale;
        return Paints.Frozen(new MatrixTransform(c, s, -s, c, x, y));
    }

    internal static Geometry Parse(string data) => Paints.Frozen(Geometry.Parse(data));

    internal static StreamGeometry Build(Action<StreamGeometryContext> draw)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open()) draw(g);
        geo.Freeze();
        return geo;
    }

    internal static StreamGeometry Polyline(IList<Point> points, bool closed = false)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(points[0], closed, closed);
            for (int i = 1; i < points.Count; i++) g.LineTo(points[i], true, true);
        }
        geo.Freeze();
        return geo;
    }

    internal static StreamGeometry Quad(Point a, Point control, Point b)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(a, false, false);
            g.QuadraticBezierTo(control, b, true, true);
        }
        geo.Freeze();
        return geo;
    }

    static Geometry MakeStar(double outer, double inner)
    {
        var points = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? outer : inner;
            points[i] = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
        }
        return Polyline(points, closed: true);
    }

    /// <summary>An eye profile: an oval <paramref name="ry"/> above and below the middle.</summary>
    protected static Func<double, (double Top, double Bottom)> Oval(double ry)
        => u =>
        {
            double h = ry * Math.Sqrt(Math.Max(0, 1 - u * u));
            return (-h, h);
        };

    /// <summary>
    /// An eye profile with pointed corners. <paramref name="slant"/> lifts the left corner (negative:
    /// the right one); <paramref name="swell"/> fattens the right end (negative: the left).
    /// </summary>
    protected static Func<double, (double Top, double Bottom)> Almond(double up, double down, double pTop, double pBottom, double slant, double swell = 0)
        => u =>
        {
            double s = Math.Max(0, 1 - u * u), fat = 1 + swell * u, tilt = slant * u;
            return (-up * fat * Math.Pow(s, pTop) + tilt, down * fat * Math.Pow(s, pBottom) + tilt);
        };
}

/// <summary>
/// One eye, as top and bottom edges sampled across its width. A lid sweeps the top edge down to a
/// shut-eye curve while, near the end, the bottom edge rises to meet it; any eye shape blinks alike.
/// Also keeps the set-independent shapes that fit this eye ("^", "&gt;", a spiral).
/// </summary>
sealed class Eye
{
    const int N = 24;
    const double ShutDepth = 0.62;

    public readonly Point C;
    public readonly double Rx, Ry;
    public readonly bool IsLeft;
    /// <summary>The whole eye, the shut-eye line, "^", "&gt;" or "&lt;", and a spiral around the origin.</summary>
    public readonly Geometry Shape, Shut, Happy, Squeeze, Spiral;
    /// <summary>Opens the eye a little wider, for <see cref="EyeShape.Wide"/>.</summary>
    public readonly Transform Wide;
    /// <summary>How far a unit heart or star is scaled to fill the eye, and an outline that stays 1.8 wide at that scale.</summary>
    public readonly double Sign;
    public readonly Pen SignRim;

    readonly Point[] _top = new Point[N + 1], _bottom = new Point[N + 1], _shut = new Point[N + 1];
    readonly Point[] _ring = new Point[2 * N + 2];
    readonly Geometry _topEdge;

    public Eye(Point c, double rx, bool isLeft, Func<double, (double Top, double Bottom)> profile)
    {
        C = c;
        Rx = rx;
        IsLeft = isLeft;
        double high = 0, low = 0;
        for (int k = 0; k <= N; k++)
        {
            // Cosine spacing: more samples near the corners, where the edges turn fastest.
            double u = -Math.Cos(Math.PI * k / N);
            var (top, bottom) = profile(u);
            double x = c.X + u * rx, mid = (top + bottom) / 2;
            _top[k] = new Point(x, c.Y + top);
            _bottom[k] = new Point(x, c.Y + bottom);
            _shut[k] = new Point(x, c.Y + mid + (bottom - mid) * ShutDepth);
            high = Math.Min(high, top);
            low = Math.Max(low, bottom);
        }
        Ry = (low - high) / 2;
        Shape = Outline(0)!;
        _topEdge = FaceSet.Polyline(_top);
        Shut = FaceSet.Polyline(new ArraySegment<Point>(_shut, 4, N - 7));

        double w = rx, h = Math.Min(rx, Ry), s = isLeft ? 1 : -1;
        Happy = FaceSet.Quad(new Point(c.X - 0.78 * w, c.Y + 0.44 * h), new Point(c.X, c.Y - 0.78 * h), new Point(c.X + 0.78 * w, c.Y + 0.44 * h));
        Squeeze = FaceSet.Polyline(new[]
        {
            new Point(c.X - s * 0.62 * w, c.Y - 0.62 * h), new Point(c.X + s * 0.5 * w, c.Y), new Point(c.X - s * 0.62 * w, c.Y + 0.62 * h),
        });
        Spiral = MakeSpiral(h * 0.92, s);
        Wide = Paints.Frozen(new MatrixTransform(1.08, 0, 0, 1.08, -0.08 * c.X, -0.08 * c.Y));
        Sign = h * 0.98;
        SignRim = Paints.MakePen(Paints.Rgb(0x15171E), 1.8 / Sign);
    }

    /// <summary>The open part of the eye with the lid at <paramref name="lid"/> (0 open, 1 shut), or null once nothing shows.</summary>
    public Geometry? Visible(double lid) => lid <= 0.001 ? Shape : Outline(lid);

    /// <summary>The lid's edge (the top of the open part), corner to corner.</summary>
    public Geometry TopEdge(double lid)
    {
        if (lid <= 0.001) return _topEdge;
        for (int k = 0; k <= N; k++) _ring[k] = FaceSet.Lerp(_top[k], _shut[k], lid);
        return FaceSet.Polyline(new ArraySegment<Point>(_ring, 0, N + 1));
    }

    /// <summary>A point on the lid's edge, <paramref name="u"/> from -1 (left corner) to 1 (right corner).</summary>
    public Point EdgeAt(double lid, double u)
    {
        int k = (int)Math.Round(Math.Acos(Math.Clamp(-u, -1, 1)) / Math.PI * N);
        return FaceSet.Lerp(_top[k], _shut[k], lid);
    }

    /// <summary>The shut eye as a line, fading in over the last stretch of the lid's travel.</summary>
    public void DrawShut(DrawingContext dc, Color ink, double lid, double width)
    {
        double a = FaceSet.Smooth(FaceSet.Ramp(lid, 0.8, 1));
        if (a > 0.01) dc.DrawGeometry(null, Paints.Stroke(ink, width, a), Shut);
    }

    StreamGeometry? Outline(double lid)
    {
        double rise = FaceSet.Smooth(FaceSet.Ramp(lid, 0.55, 1));
        double open = 0;
        for (int k = 0; k <= N; k++)
        {
            var top = FaceSet.Lerp(_top[k], _shut[k], lid);
            var bottom = FaceSet.Lerp(_bottom[k], _shut[k], rise);
            _ring[k] = top;
            _ring[2 * N + 1 - k] = bottom;
            open = Math.Max(open, bottom.Y - top.Y);
        }
        if (open < 0.4) return null;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(_ring[0], true, true);
            g.PolyLineTo(new ArraySegment<Point>(_ring, 1, _ring.Length - 1), true, true);
        }
        geo.Freeze();
        return geo;
    }

    static Geometry MakeSpiral(double r, double hand)
    {
        const double turns = 2.4;
        double end = turns * 2 * Math.PI;
        var points = new List<Point>();
        for (double a = 0; a <= end + 1e-6; a += 0.2)
        {
            double rr = r * a / end;
            points.Add(new Point(hand * Math.Cos(a) * rr, Math.Sin(a) * rr));
        }
        return FaceSet.Polyline(points);
    }
}

/// <summary>
/// The promo film's mouths, laid out in its 40 x 18 mouth box and placed (for small mouths, scaled)
/// on a face. Shapes that never change are built once; the open ones follow <see cref="FaceFrame.Open"/>.
/// </summary>
sealed class MouthKit
{
    internal static readonly Brush Hole = Paints.Solid(0x120C10);
    internal static readonly Brush TongueFill = Paints.Solid(0xFF7B8A);
    internal static readonly Brush Enamel = Paints.Solid(0xFFFDF6);
    internal static readonly Pen Gaps = Paints.MakePen(Paints.Rgb(0x15171E), 1.1);
    static readonly Pen Groove = Paints.MakePen(Paints.Rgb(0xD9506A), 1.1);

    readonly Point _at;
    readonly double _s, _line;
    readonly Geometry _flat, _smile, _grin, _frown, _wavy, _cat, _tongue, _groove, _teethGaps;
    readonly Rect _teeth;

    /// <param name="at">Where the middle of the box (20, 9) lands on the face.</param>
    /// <param name="smirk">A lopsided, confident smile and grin.</param>
    public MouthKit(Point at, double scale = 1, double line = 3, bool smirk = false)
    {
        _at = at;
        _s = scale;
        _line = line;
        _flat = FaceSet.Quad(P(12, 8), P(20, 9.5), P(28, 8));
        var (s0, s1, s2) = smirk ? (P(11, 7), P(21, 12.5), P(29, 4)) : (P(9, 5), P(20, 16), P(31, 5));
        _smile = FaceSet.Quad(s0, s1, s2);
        _grin = smirk
            ? FaceSet.Build(g =>
            {
                g.BeginFigure(P(8, 6), false, false);
                g.QuadraticBezierTo(P(21, 15), P(31, 3), true, true);
                g.BeginFigure(P(32.5, 4.3), false, false);
                g.LineTo(P(29.5, 1.7), true, false);
            })
            : FaceSet.Build(g =>
            {
                g.BeginFigure(P(5, 5), false, false);
                g.QuadraticBezierTo(P(20, 17), P(35, 5), true, true);
                g.BeginFigure(P(3.8, 6.6), false, false);
                g.LineTo(P(6.2, 3.4), true, false);
                g.BeginFigure(P(36.2, 6.6), false, false);
                g.LineTo(P(33.8, 3.4), true, false);
            });
        _frown = FaceSet.Quad(P(11, 13), P(20, 5), P(29, 13));
        var wave = new Point[25];
        for (int i = 0; i < wave.Length; i++) wave[i] = P(8 + i, 9 - 2.4 * Math.Sin(i / 24.0 * Math.PI * 4));
        _wavy = FaceSet.Polyline(wave);
        _cat = FaceSet.Build(g =>
        {
            g.BeginFigure(P(12, 6), false, false);
            g.QuadraticBezierTo(P(16, 12), P(20, 7), true, true);
            g.QuadraticBezierTo(P(24, 12), P(28, 6), true, true);
        });
        // The tongue hangs from the smile: its top corners sit on the smile line, which hides them.
        var left = OnQuad(s0, s1, s2, P(19, 0).X);
        var right = OnQuad(s0, s1, s2, P(27, 0).X);
        _tongue = FaceSet.Build(g =>
        {
            g.BeginFigure(left, true, true);
            g.LineTo(P(19, 13.2), true, true);
            g.QuadraticBezierTo(P(19, 17.6), P(23, 17.6), true, true);
            g.QuadraticBezierTo(P(27, 17.6), P(27, 13.2), true, true);
            g.LineTo(right, true, true);
        });
        _groove = FaceSet.Polyline(new[] { P(23, 12), P(23, 14.8) });
        _teeth = new Rect(P(10, 3), P(30, 14));
        _teethGaps = FaceSet.Build(g =>
        {
            g.BeginFigure(P(10.6, 8.5), false, false);
            g.LineTo(P(29.4, 8.5), true, false);
            foreach (double x in new[] { 15.0, 20.0, 25.0 })
            {
                g.BeginFigure(P(x, 3.7), false, false);
                g.LineTo(P(x, 13.3), true, false);
            }
        });
    }

    double HoleLine => _line * 0.8;

    Point P(double x, double y) => new(_at.X + (x - 20) * _s, _at.Y + (y - 9) * _s);

    public void Draw(DrawingContext dc, in FaceFrame f, MouthShape shape, Color ink)
    {
        var line = Paints.Stroke(ink, _line);
        switch (shape)
        {
            case MouthShape.Smile: dc.DrawGeometry(null, line, _smile); break;
            case MouthShape.Grin: dc.DrawGeometry(null, line, _grin); break;
            case MouthShape.Frown: dc.DrawGeometry(null, line, _frown); break;
            case MouthShape.Wavy: dc.DrawGeometry(null, line, _wavy); break;
            case MouthShape.Cat: dc.DrawGeometry(null, line, _cat); break;
            case MouthShape.O: DrawO(dc, FaceSet.Clamp01(f.Open), ink); break;
            case MouthShape.Open: DrawOpen(dc, FaceSet.Clamp01(f.Open), ink); break;
            case MouthShape.Yawn: DrawYawn(dc, FaceSet.Clamp01(f.Open), ink); break;
            case MouthShape.Tongue:
                dc.DrawGeometry(TongueFill, Paints.Stroke(ink, _line * 0.6), _tongue);
                dc.DrawGeometry(null, Groove, _groove);
                dc.DrawGeometry(null, line, _smile);
                break;
            case MouthShape.Teeth:
                dc.DrawRoundedRectangle(Enamel, Paints.Stroke(ink, _line * 0.8), _teeth, 3.5 * _s, 3.5 * _s);
                dc.DrawGeometry(null, Gaps, _teethGaps);
                break;
            default: dc.DrawGeometry(null, line, _flat); break;
        }
    }

    void DrawO(DrawingContext dc, double o, Color ink)
        => dc.DrawEllipse(Hole, Paints.Stroke(ink, HoleLine), P(20, 8), (2.6 + 1.6 * o) * _s, (1.3 + 3.7 * o) * _s);

    void DrawOpen(DrawingContext dc, double o, Color ink)
    {
        double b = 3 + 14 * (0.14 + 0.86 * o);
        var hole = new StreamGeometry();
        using (var g = hole.Open())
        {
            g.BeginFigure(P(8, 3), true, true);
            g.LineTo(P(32, 3), true, true);
            g.QuadraticBezierTo(P(30, b), P(20, b), true, true);
            g.QuadraticBezierTo(P(10, b), P(8, 3), true, true);
        }
        hole.Freeze();
        dc.DrawGeometry(Hole, null, hole);
        double k = FaceSet.Ramp(b, 6, 14);
        if (k > 0)
        {
            double ry = 2.6 * k;
            dc.PushClip(hole);
            dc.DrawEllipse(TongueFill, null, P(20, b - 0.9 - ry), 5 * (0.6 + 0.4 * k) * _s, ry * _s);
            dc.Pop();
        }
        dc.DrawGeometry(null, Paints.Stroke(ink, HoleLine), hole);
    }

    void DrawYawn(DrawingContext dc, double o, Color ink)
    {
        // The top stays put while the jaw drops.
        double ry = 3 + 5.5 * o, rx = 5.2 + 1.8 * o;
        var hole = Paints.Frozen(new EllipseGeometry(P(20, 0.5 + ry), rx * _s, ry * _s));
        dc.DrawGeometry(Hole, null, hole);
        double k = FaceSet.Ramp(ry, 4, 8.5);
        if (k > 0)
        {
            dc.PushClip(hole);
            dc.DrawEllipse(TongueFill, null, P(20, 0.5 + 2 * ry - 3.5), 4 * rx / 7 * _s, 2.4 * k * _s);
            dc.Pop();
        }
        dc.DrawGeometry(null, Paints.Stroke(ink, HoleLine), hole);
    }

    static Point OnQuad(Point a, Point c, Point b, double x)
    {
        Point best = a;
        for (double t = 0; t <= 1; t += 0.01)
        {
            double u = 1 - t;
            var p = new Point(u * u * a.X + 2 * u * t * c.X + t * t * b.X, u * u * a.Y + 2 * u * t * c.Y + t * t * b.Y);
            if (Math.Abs(p.X - x) < Math.Abs(best.X - x)) best = p;
        }
        return best;
    }
}

// ====================================================================== the sets

/// <summary>1. The promo film's face.</summary>
class ClassicFace : FaceSet
{
    public ClassicFace() : this(9, 12) { }

    protected ClassicFace(double rx, double ry)
    {
        Left = new Eye(new Point(36, 29), rx, true, Oval(ry));
        Right = new Eye(new Point(62, 29), rx, false, Oval(ry));
        Mouth = new MouthKit(new Point(49, 54));
    }

    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood) => DrawPupil(dc, f, e, mood);
}

/// <summary>2. The promo face with lashes, rounder eyes, rosier cheeks and a bow.</summary>
sealed class ClassicGirlFace : ClassicFace
{
    static readonly double[] LashAt = { 0.95, 0.78, 0.58 }, LashLength = { 3.4, 3.3, 2.9 };
    static readonly Geometry BowTails = Parse("M13.4,4.4 L10.4,9.8 L12.2,9.2 L13.1,10.4 L14.6,4.8 Z M14.6,4.4 L17.6,9.8 L15.8,9.2 L14.9,10.4 L13.4,4.8 Z");
    static readonly Geometry BowWings = Parse("M14,3 C11,-1.5 6.4,-4.2 5.8,0.4 C5.3,4.6 10,7 14,3 Z M14,3 C17,-1.5 21.6,-4.2 22.2,0.4 C22.7,4.6 18,7 14,3 Z");
    static readonly Geometry BowFolds = Parse("M12.2,2.6 Q10,1.2 8.4,1 M15.8,2.6 Q18,1.2 19.6,1");
    static readonly Geometry BowGlint = Parse("M8.2,-1.2 Q9.4,-2.4 10.8,-1.6");
    static readonly Pen GlintPen = Paints.MakePen(Paints.Rgb(0xFFFFFF, 0.75), 0.9);

    readonly ByColor<Bow> _bow = new(Bow.Make);
    readonly Geometry _happyLashesLeft, _happyLashesRight;

    public ClassicGirlFace() : base(9.5, 11.5)
    {
        BlushBoost = 0.25;
        _happyLashesLeft = HappyLashes(Left);
        _happyLashesRight = HappyLashes(Right);
    }

    protected override void DrawLid(DrawingContext dc, in FaceFrame f, Eye e, double lid)
        => DrawLashes(dc, Paints.Stroke(LineInk(f), 1.7), e, lid, LashAt, LashLength, 0.9);

    protected override void DrawHappy(DrawingContext dc, in FaceFrame f, Eye e)
    {
        base.DrawHappy(dc, f, e);
        dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 1.7), e.IsLeft ? _happyLashesLeft : _happyLashesRight);
    }

    protected override void DrawFront(DrawingContext dc, in FaceFrame f)
    {
        var bow = _bow.Get(f.Accent);
        dc.DrawGeometry(bow.Fill, bow.Edge, BowTails);
        dc.DrawGeometry(bow.Fill, bow.Edge, BowWings);
        dc.DrawGeometry(null, bow.Fold, BowFolds);
        dc.DrawEllipse(bow.Fill, bow.Edge, new Point(14, 3), 2.3, 2.5);
        dc.DrawGeometry(null, GlintPen, BowGlint);
    }

    /// <summary>Two lashes off the outer end of "^".</summary>
    static Geometry HappyLashes(Eye e)
    {
        double side = e.IsLeft ? -1 : 1, w = e.Rx, h = Math.Min(e.Rx, e.Ry);
        var end = new Point(e.C.X + side * 0.78 * w, e.C.Y + 0.44 * h);
        var top = new Point(e.C.X, e.C.Y - 0.78 * h);
        return FaceSet.Build(g =>
        {
            foreach (var (t, dx, dy, len) in new[] { (0.1, 1.0, -0.25, 3.0), (0.27, 0.65, -1.0, 2.8) })
            {
                double u = 1 - t;
                // On the quadratic from the outer end toward the top (the control point is straight above the middle).
                var p = new Point(u * u * end.X + 2 * u * t * e.C.X + t * t * (2 * e.C.X - end.X),
                                  u * u * end.Y + 2 * u * t * top.Y + t * t * end.Y);
                var d = new Vector(side * dx, dy);
                d.Normalize();
                g.BeginFigure(p + d * 1.8, false, false);
                g.LineTo(p + d * (1.8 + len), true, false);
            }
        });
    }

    sealed record Bow(Brush Fill, Pen Edge, Pen Fold)
    {
        public static Bow Make(Color accent)
        {
            var c = ColorUtil.ForDarkSurface(accent);
            var dark = ColorUtil.Lerp(c, Colors.Black, 0.5);
            return new Bow(Paints.Frozen(new SolidColorBrush(c)), Paints.MakePen(dark, 1.1), Paints.MakePen(ColorUtil.WithAlpha(dark, 0.8), 0.9));
        }
    }
}

/// <summary>An anime iris in the song's color: darker under the lashes, lighter below, with a deep pupil.</summary>
sealed record IrisPaint(Brush Iris, Pen Ring, Brush Pupil)
{
    public static IrisPaint Make(Color accent, Color toward)
    {
        var mid = Toward(accent, toward, 0.55);
        var iris = new LinearGradientBrush(new GradientStopCollection
        {
            new(ColorUtil.Lerp(mid, Colors.Black, 0.4), 0),
            new(mid, 0.55),
            new(ColorUtil.Lerp(mid, Colors.White, 0.35), 1),
        }, 90);
        return new IrisPaint(Paints.Frozen(iris), Paints.MakePen(ColorUtil.Lerp(mid, Colors.Black, 0.55), 1),
            Paints.Frozen(new SolidColorBrush(ColorUtil.Lerp(mid, Color.FromRgb(0x14, 0x0E, 0x24), 0.8))));
    }

    /// <summary>
    /// Turns the song's color part of the way to <paramref name="target"/> around the color wheel; mixing
    /// in RGB would make greys of opposite colors (red toward teal). A grey song takes the target's hue.
    /// </summary>
    static Color Toward(Color accent, Color target, double k)
    {
        ColorUtil.ToHsl(accent, out double h, out double s, out double l);
        ColorUtil.ToHsl(target, out double th, out double ts, out double tl);
        if (s < 0.12) h = th;
        double turn = ((th - h) % 360 + 540) % 360 - 180;
        return ColorUtil.FromHsl(h + turn * k, Math.Clamp(s + (ts - s) * k, 0.45, 0.8), Math.Clamp(l + (tl - l) * k, 0.45, 0.62));
    }
}

/// <summary>3. Big tall anime eyes with a violet-leaning iris, thin high brows, a tiny mouth and a fringe.</summary>
sealed class AnimeGirlFace : FaceSet
{
    static readonly Color Violet = Paints.Rgb(0x8B6FE8);
    static readonly Pen LashLine = Paints.MakePen(Paints.Rgb(0x15171E), 3.4);
    static readonly double[] FlickAt = { 1.0, 0.9 }, FlickLength = { 3.8, 3.0 };
    static readonly Brush Hair = Paints.Solid(0x4A3027);
    static readonly Pen HairSheen = Paints.MakePen(Paints.Rgb(0x9A6A55, 0.7), 1.5);
    static readonly Geometry Fringe = MakeFringe();
    static readonly Geometry SheenArc = Parse("M17,-2.4 Q29,-5.4 44,-5.6 M54,-5.6 Q69,-5.4 81,-2.4");
    static readonly Geometry Hatch = Parse("M25,48.6 L27,45.4 M27.6,48.6 L29.6,45.4 M30.2,48.6 L32.2,45.4 M65.8,48.6 L67.8,45.4 M68.4,48.6 L70.4,45.4 M71,48.6 L73,45.4");
    static readonly Color HatchPink = Paints.Rgb(0xF2557E);

    readonly ByColor<IrisPaint> _iris = new(c => IrisPaint.Make(c, Violet));

    public AnimeGirlFace()
    {
        // Tall, with a flat top under the lash line and a round bottom.
        static (double, double) Profile(double u) => (-13 * Math.Sqrt(Math.Max(0, 1 - Math.Pow(Math.Abs(u), 3))), 14 * Math.Pow(Math.Max(0, 1 - u * u), 0.6));
        Left = new Eye(new Point(36, 29.5), 10, true, Profile);
        Right = new Eye(new Point(62, 29.5), 10, false, Profile);
        Mouth = new MouthKit(new Point(49, 50.5), 0.7, 2.5);
        CheekLeft = new Point(28.6, 47);
        CheekRight = new Point(69.4, 47);
    }

    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood)
    {
        var paint = _iris.Get(f.Accent);
        var g = Gaze(f);
        double k = mood == Mood.Wide ? 0.82 : 0.9 + 0.1 * Math.Clamp(f.Pupil, 0.6, 1.4);
        var c = new Point(e.C.X + g.X * 2.4, e.C.Y + 2.2 + g.Y * 2);
        dc.DrawEllipse(paint.Iris, paint.Ring, c, 7.2 * k, 9.6 * k);
        double p = PupilSize(f, mood);
        dc.DrawEllipse(paint.Pupil, null, new Point(c.X, c.Y + 0.8), 3.3 * p, 4.4 * p);
        dc.DrawEllipse(Shine, null, new Point(c.X - 2.7 * k, c.Y - 4 * k), 2.5, 3);
        dc.DrawEllipse(Shine, null, new Point(c.X + 2.8 * k, c.Y + 3.8 * k), 1.3, 1.3);
        if (mood == Mood.Teary) dc.DrawEllipse(Shine, null, new Point(c.X + 2.6 * k, c.Y - 2.4 * k), 1, 1);
    }

    protected override void DrawLid(DrawingContext dc, in FaceFrame f, Eye e, double lid)
    {
        if (lid < 0.999) dc.DrawGeometry(null, LashLine, e.TopEdge(lid));
        DrawLashes(dc, Paints.Stroke(LineInk(f), 1.8), e, lid, FlickAt, FlickLength, 1.4);
    }

    protected override void DrawBrows(DrawingContext dc, in FaceFrame f)
        => DrawBrowPair(dc, f, Paints.Stroke(LineInk(f), 1.8), 10.5, 6, 1.6, 3);

    protected override void DrawBlush(DrawingContext dc, in FaceFrame f)
    {
        base.DrawBlush(dc, f);
        double a = Ramp(f.Blush, 0.25, 0.7);
        if (a > 0) dc.DrawGeometry(null, Paints.Stroke(HatchPink, 1, a), Hatch);
    }

    protected override void DrawFront(DrawingContext dc, in FaceFrame f)
    {
        dc.DrawGeometry(Hair, Paints.Stroke(LineInk(f), 1.1, 0.45), Fringe);
        dc.DrawGeometry(null, HairSheen, SheenArc);
    }

    static Geometry MakeFringe()
    {
        // The right half of the hairline, from the centre parting outward: the gaps between locks
        // and the locks' tips. Each lock bows out from the gap on its inner side to a point, then
        // runs back up to the next gap; the left half is its mirror image.
        var gaps = new[] { new Point(49, -1.5), new Point(59, 0), new Point(68, 0.5), new Point(77, 0), new Point(85.5, 0.5) };
        var tips = new[] { new Point(55.5, 5.8), new Point(64.5, 4.5), new Point(73.5, 6), new Point(82, 4.8), new Point(90, 6) };
        var side = new Point(93, -1);
        var right = new List<(Point Control, Point End)>();
        for (int i = 0; i < tips.Length; i++)
        {
            Point from = gaps[i], tip = tips[i], next = i + 1 < gaps.Length ? gaps[i + 1] : side;
            right.Add((new Point(from.X + (tip.X - from.X) * 0.1, tip.Y - 0.6), tip));
            right.Add((new Point(tip.X + (next.X - tip.X) * 0.4, tip.Y - (tip.Y - next.Y) * 0.3), next));
        }
        static Point Mirror(Point p) => new(98 - p.X, p.Y);
        return Build(g =>
        {
            g.BeginFigure(Mirror(side), true, true);
            for (int i = right.Count - 1; i >= 0; i--)
                g.QuadraticBezierTo(Mirror(right[i].Control), Mirror(i == 0 ? gaps[0] : right[i - 1].End), true, true);
            foreach (var (control, end) in right) g.QuadraticBezierTo(control, end, true, true);
            // The crown, back over the top.
            g.BezierTo(new Point(87, -6.5), new Point(68, -8), new Point(49, -8), true, true);
            g.BezierTo(new Point(30, -8), new Point(11, -6.5), Mirror(side), true, true);
        });
    }
}

/// <summary>4. Narrow, slanted anime eyes with a teal-leaning iris, straight brows, spiky hair and a smirk.</summary>
sealed class AnimeBoyFace : FaceSet
{
    static readonly Color Teal = Paints.Rgb(0x23A99C);
    static readonly Pen LashLine = Paints.MakePen(Paints.Rgb(0x15171E), 3.1);
    static readonly double[] FlickAt = { 1.0 }, FlickLength = { 3.4 };
    static readonly Brush Hair = Paints.Solid(0x262B36);
    static readonly Geometry Spikes = MakeSpikes();
    static readonly Geometry Streaks = Parse("M27,-5 L31.5,-1 M46,-6.6 L50.5,-2.4 M65,-6.4 L69.5,-2.2 M82,-4.6 L85.5,-1.4");
    static readonly Pen StreakPen = Paints.MakePen(Paints.Rgb(0x56607A), 1.2);

    readonly ByColor<IrisPaint> _iris = new(c => IrisPaint.Make(c, Teal));

    public AnimeBoyFace()
    {
        Left = new Eye(new Point(36, 30.5), 10.5, true, Almond(7.6, 6.2, 0.55, 0.8, 1.8));
        Right = new Eye(new Point(62, 30.5), 10.5, false, Almond(7.6, 6.2, 0.55, 0.8, -1.8));
        Mouth = new MouthKit(new Point(49, 52.5), 0.8, 2.7, smirk: true);
    }

    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood)
    {
        var paint = _iris.Get(f.Accent);
        var g = Gaze(f);
        double k = mood == Mood.Wide ? 0.82 : 0.92 + 0.08 * Math.Clamp(f.Pupil, 0.6, 1.4);
        var c = new Point(e.C.X + g.X * 2.6, e.C.Y + 0.8 + g.Y * 1.6);
        dc.DrawEllipse(paint.Iris, paint.Ring, c, 5.4 * k, 6.6 * k);
        double p = PupilSize(f, mood);
        dc.DrawEllipse(paint.Pupil, null, new Point(c.X, c.Y + 0.4), 2.5 * p, 3.3 * p);
        dc.DrawEllipse(Shine, null, new Point(c.X - 1.9 * k, c.Y - 2.5 * k), 1.5, 1.5);
        if (mood == Mood.Teary) dc.DrawEllipse(Shine, null, new Point(c.X + 2 * k, c.Y + 2.2 * k), 0.9, 0.9);
    }

    protected override void DrawLid(DrawingContext dc, in FaceFrame f, Eye e, double lid)
    {
        if (lid < 0.999) dc.DrawGeometry(null, LashLine, e.TopEdge(lid));
        DrawLashes(dc, Paints.Stroke(LineInk(f), 1.8), e, lid, FlickAt, FlickLength, 1.6);
    }

    protected override void DrawBrows(DrawingContext dc, in FaceFrame f)
        => DrawBrowPair(dc, f, Paints.Stroke(LineInk(f), 2.8), 18.6, 8, 0, 3.6, rest: 1);

    protected override void DrawFront(DrawingContext dc, in FaceFrame f)
    {
        dc.DrawGeometry(Hair, Paints.Stroke(LineInk(f), 1.1, 0.45), Spikes);
        dc.DrawGeometry(null, StreakPen, Streaks);
    }

    static Geometry MakeSpikes()
    {
        // Tips and notches from right to left. Every spike is swept to the left: it falls in a curve
        // from the notch on its right and rises straight to the next one.
        var points = new[]
        {
            new Point(91, 4.5), new Point(86.5, -2), new Point(80.5, 4.5), new Point(76.5, -2.5), new Point(69.5, 3.5),
            new Point(65.5, -3), new Point(58, 4.5), new Point(54.5, -3), new Point(46, 4), new Point(42.5, -2.5),
            new Point(34.5, 4.5), new Point(30.5, -2), new Point(22, 4), new Point(18.5, -1.5), new Point(10, 4.5),
        };
        Point right = new(93, -1), left = new(5.5, -1);
        return Build(g =>
        {
            g.BeginFigure(right, true, true);
            var from = right;
            for (int i = 0; i < points.Length; i++)
            {
                if (i % 2 == 0) g.QuadraticBezierTo(new Point(from.X - 0.6, points[i].Y - 2.2), points[i], true, false);
                else g.LineTo(points[i], true, false);
                from = points[i];
            }
            g.LineTo(left, true, false);
            g.BezierTo(new Point(12, -6.5), new Point(30, -8), new Point(49, -8), true, true);
            g.BezierTo(new Point(68, -8), new Point(86, -6.5), right, true, true);
        });
    }
}

/// <summary>5. Big glossy black eyes slanting up, a third eye, antennae that glow with the music.</summary>
sealed class AlienFace : FaceSet
{
    static readonly Color Green = Paints.Rgb(0x7CF2A0);
    static readonly Pen GreenRim = Paints.MakePen(Paints.Rgb(0x9DF2B5, 0.85), 1.5);
    static readonly Brush Gloss = Paints.Frozen(new RadialGradientBrush(new GradientStopCollection
    {
        new(Paints.Rgb(0x2C3240), 0),
        new(Paints.Rgb(0x0B0D12), 0.7),
        new(Paints.Rgb(0x050608), 1),
    }) { GradientOrigin = new Point(0.4, 0.3), Center = new Point(0.45, 0.4) });
    static readonly Geometry Stalks = Parse("M41,4 Q40,-1 33,-3.5 M57,4 Q58,-1 65,-3.5");

    readonly Eye _third = new(new Point(49, 10.5), 4.2, true, Almond(2.9, 2.4, 0.7, 0.85, 0));
    readonly ByColor<Color> _ink = new(c => ColorUtil.Lerp(c, Green, 0.25));
    readonly ByColor<Brush> _sheen = new(c => Glow(ColorUtil.ForDarkSurface(c), 0.6));
    readonly ByColor<Brush> _halo = new(c => Glow(ColorUtil.ForDarkSurface(c), 0.75));
    readonly ByColor<Brush> _bulb = new(c => Paints.Frozen(new SolidColorBrush(ColorUtil.Lerp(ColorUtil.ForDarkSurface(c), Colors.White, 0.25))));

    public AlienFace()
    {
        Left = new Eye(new Point(35, 29.5), 11, true, Almond(8.4, 7, 0.7, 0.9, 2.6, 0.25));
        Right = new Eye(new Point(63, 29.5), 11, false, Almond(8.4, 7, 0.7, 0.9, -2.6, -0.25));
        Mouth = new MouthKit(new Point(49, 51), 0.72, 2.1);
    }

    protected override Color LineInk(in FaceFrame f) => _ink.Get(f.Ink);

    protected override Brush EyeFill(in FaceFrame f) => Gloss;

    protected override Pen EyeRim(in FaceFrame f) => GreenRim;

    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood)
    {
        var g = Gaze(f);
        double k = e.Rx / 11, p = PupilSize(f, mood);
        // A soft reflection of the song's color low in the eye, a white glint that follows the gaze.
        dc.DrawEllipse(_sheen.Get(f.Accent), null, new Point(e.C.X + g.X * 1.2 * k, e.C.Y + 3.4 * k), 7 * k, 3.6 * k);
        var glint = new Point(e.C.X + (-3 + g.X * 1.6) * k, e.C.Y + (-2.4 + g.Y * 1.2) * k);
        dc.DrawEllipse(Shine, null, glint, 2.3 * k * p, 1.7 * k * p);
        dc.DrawEllipse(Shine, null, new Point(glint.X + 4.2 * k, glint.Y + 2.8 * k), 0.8 * k, 0.8 * k);
    }

    protected override void DrawBrows(DrawingContext dc, in FaceFrame f)
    {
        double a = Ramp(Math.Abs(f.Brows), 0.05, 0.3);
        if (a > 0) DrawBrowPair(dc, f, Paints.Stroke(LineInk(f), 1.8, a), 17, 5, 0.6, 3);
    }

    protected override void DrawBehind(DrawingContext dc, in FaceFrame f)
    {
        dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 1.6), Stalks);
        var halo = _halo.Get(f.Accent);
        var bulb = _bulb.Get(f.Accent);
        for (int i = 0; i < 2; i++)
        {
            var tip = new Point(i == 0 ? 33 : 65, -3.5);
            double pulse = 0.5 + 0.5 * Math.Sin(f.Time * 3.2 + i * 1.7);
            double r = Math.Min(4.5, 2.8 + 1.1 * pulse + 1.6 * Clamp01(f.Level));
            dc.DrawEllipse(halo, null, tip, r, r);
            dc.DrawEllipse(bulb, null, tip, 1.9, 1.9);
            dc.DrawEllipse(Shine, null, new Point(tip.X - 0.6, tip.Y - 0.6), 0.6, 0.6);
        }
        DrawThirdEye(dc, f);
    }

    /// <summary>The small eye on the forehead blinks with the others and squints when they do anything else.</summary>
    void DrawThirdEye(DrawingContext dc, in FaceFrame f)
    {
        switch (f.Eyes)
        {
            case EyeShape.Open: DrawOpen(dc, f, _third, Clamp01((f.LidLeft + f.LidRight) / 2), Mood.Plain, 2); break;
            case EyeShape.Teary: DrawOpen(dc, f, _third, Clamp01((f.LidLeft + f.LidRight) / 2), Mood.Teary, 2); break;
            case EyeShape.Wide: DrawOpen(dc, f, _third, 0, Mood.Wide, 2); break;
            case EyeShape.Happy: dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 2), _third.Happy); break;
            default: _third.DrawShut(dc, LineInk(f), 1, 2); break;
        }
    }

    static Brush Glow(Color c, double alpha) => Paints.Frozen(new RadialGradientBrush(new GradientStopCollection
    {
        new(ColorUtil.WithAlpha(c, alpha), 0),
        new(ColorUtil.WithAlpha(c, alpha * 0.4), 0.5),
        new(ColorUtil.WithAlpha(c, 0), 1),
    }));
}

/// <summary>
/// 6. LED panels for eyes and an LED equalizer for a mouth. Every shape is a pattern of lit dots;
/// each lit dot has a soft halo under a bright core.
/// </summary>
sealed class RobotFace : FaceSet
{
    const int Cols = 5, Rows = 6, Bars = 9, Steps = 5;
    const double Pitch = 3.5, Dot = 2.7;
    static readonly Brush Glass = Paints.Solid(0x0D1117, 0.92);
    static readonly Pen Bezel = Paints.MakePen(Paints.Rgb(0x8E97AA), 1.4);
    static readonly Brush Metal = Paints.Solid(0x9AA3B5);
    static readonly Pen MetalEdge = Paints.MakePen(Paints.Rgb(0x2A2F3A), 1);
    static readonly Pen Rod = Paints.MakePen(Paints.Rgb(0x8E97AA), 1.6);
    static readonly Geometry Rods = Parse("M18,9 L13,-3 M80,9 L85,-3");
    static readonly Geometry Slots = Parse("M16.6,9 L19.4,9 M18,7.6 L18,10.4 M78.6,9 L81.4,9 M80,7.6 L80,10.4");
    static readonly Color Pink = Paints.Rgb(0xFF5C8D), Gold = Paints.Rgb(0xFFD23F), Water = Paints.Rgb(0x6CC6FF);

    static readonly string[] OpenMap = { ".###.", "#####", "#####", "#####", "#####", ".###." };
    static readonly string[] FullMap = { "#####", "#####", "#####", "#####", "#####", "#####" };
    static readonly string[] ShutMap = { ".....", ".....", ".....", "#...#", ".###.", "....." };
    static readonly string[] HappyMap = { ".....", "..#..", ".#.#.", "#...#", ".....", "....." };
    static readonly string[] HeartMap = { ".....", ".#.#.", "#####", "#####", ".###.", "..#.." };
    static readonly string[] StarMap = { "..#..", "..#..", "#####", ".###.", ".#.#.", "#...#" };
    static readonly string[] SqueezeMap = { ".....", "#....", ".##..", "...##", ".##..", "#...." };
    static readonly string[] TeethMap = { ".........", "#########", "#.#.#.#.#", "#########", "........." };
    static readonly string[] CatMap = { ".........", "#...#...#", ".#.#.#.#.", "..#...#..", "........." };

    readonly ByColor<Color> _led = new(ColorUtil.ForDarkSurface);

    public RobotFace()
    {
        Left = new Eye(new Point(36, 29), 9, true, Oval(12));
        Right = new Eye(new Point(62, 29), 9, false, Oval(12));
    }

    // The panels light their own dots; there is no eye white to fill.
    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood) { }

    protected override void DrawEye(DrawingContext dc, in FaceFrame f, Eye e, double lid)
    {
        dc.DrawRoundedRectangle(Glass, Bezel, new Rect(e.C.X - 10, e.C.Y - 12, 20, 24), 4.5, 4.5);
        var led = _led.Get(f.Accent);
        var shape = f.Eyes;
        double t = f.Time;
        lid = shape is EyeShape.Open or EyeShape.Teary ? Clamp01(lid) : 0;
        bool mirror = !e.IsLeft;
        var g = Gaze(f);
        double pc = 2 + g.X * 1.3, pr = 2.5 + g.Y * 1.3;
        double size = Math.Clamp(f.Pupil, 0.5, 1.5) * (shape == EyeShape.Wide ? 0.6 : 1);
        double shut = Smooth(Ramp(lid, 0.8, 1));
        double beat = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.2)), 4);
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                double b;
                var color = led;
                switch (shape)
                {
                    case EyeShape.Happy: b = Lit(HappyMap, col, row, false); break;
                    case EyeShape.Squeeze: b = Lit(SqueezeMap, col, row, mirror); break;
                    case EyeShape.Heart: b = Lit(HeartMap, col, row, false) * (0.8 + 0.2 * beat); color = Pink; break;
                    case EyeShape.Star: b = Lit(StarMap, col, row, false) * (0.75 + 0.25 * Math.Sin(t * 7 + col + row)); color = Gold; break;
                    case EyeShape.Spiral: b = SpinnerAt(col, row, t, mirror); break;
                    default:
                        // Lids switch rows off from the top; the pupil is a white-hot spot that follows the gaze.
                        b = Lit(shape == EyeShape.Wide ? FullMap : OpenMap, col, row, false) * Clamp01(row + 1 - lid * Rows);
                        double dx = (col - pc) / (1.05 * size), dy = (row - pr) / (1.3 * size);
                        double hot = Clamp01(1.5 - Math.Sqrt(dx * dx + dy * dy));
                        if (shape == EyeShape.Teary && row >= 4)
                        {
                            color = Water;
                            b *= 0.75 + 0.25 * Math.Sin(t * 7 + col * 1.3);
                        }
                        else if (hot > 0)
                        {
                            color = ColorUtil.Lerp(led, Colors.White, Math.Round(hot * 4) / 4 * 0.8);
                        }
                        b = Math.Max(b, shut * Lit(ShutMap, col, row, false));
                        break;
                }
                var p = new Point(e.C.X + (col - 2) * Pitch, e.C.Y + (row - 2.5) * Pitch);
                Led(dc, new Rect(p.X - Dot / 2, p.Y - Dot / 2, Dot, Dot), b, color, led);
            }
        }
    }

    protected override void DrawMouth(DrawingContext dc, in FaceFrame f)
    {
        dc.DrawRoundedRectangle(Glass, Bezel, new Rect(27, 43.5, 44, 18.5), 5, 5);
        var led = _led.Get(f.Accent);
        double t = f.Time, level = Clamp01(f.Level), open = Clamp01(f.Open);
        for (int col = 0; col < Bars; col++)
        {
            for (int row = 0; row < Steps; row++)
            {
                double b;
                var color = led;
                switch (f.Mouth)
                {
                    case MouthShape.Teeth: b = Lit(TeethMap, col, row, false) * (0.85 + 0.15 * Math.Sin(t * 31 + col)); break;
                    case MouthShape.Cat: b = Lit(CatMap, col, row, false); break;
                    case MouthShape.Wavy: b = Clamp01(1 - Math.Abs(row - (2 + 1.3 * Math.Sin(col * 1.25 - t * 5)))); break;
                    default: b = Bar(f.Mouth, col, row, t, level, open, ref color); break;
                }
                var p = new Point(49 + (col - 4) * 4.4, 52.75 + (row - 2) * 3.2);
                Led(dc, new Rect(p.X - 1.7, p.Y - 1.2, 3.4, 2.4), b, color, led);
            }
        }
    }

    /// <summary>
    /// One segment of the equalizer: each bar bounces with the music around a line that smiles,
    /// frowns or lies flat; open mouths keep the bars tall even in silence.
    /// </summary>
    static double Bar(MouthShape shape, int col, int row, double t, double level, double open, ref Color color)
    {
        var (from, to, depth, floor, bounce) = shape switch
        {
            MouthShape.Smile => (1, 7, 2.0, 0.0, 1.0),
            MouthShape.Grin => (0, 8, 2.0, 0.0, 1.0),
            MouthShape.Tongue => (1, 7, 2.0, 0.0, 0.6),
            MouthShape.Frown => (1, 7, -2.0, 0.0, 0.0),
            MouthShape.O => (3, 5, 0.0, 0.4 + 0.9 * open, 0.5),
            MouthShape.Open => (1, 7, 0.6, 1.6 * open, 1.0),
            MouthShape.Yawn => (2, 6, 0.0, 0.6 + 1.6 * open, 0.3),
            _ => (0, 8, 0.0, 0.0, 1.0),
        };
        if (col < from || col > to) return 0;
        double half = (to - from) / 2.0, x = half > 0 ? (col - (from + to) / 2.0) / half : 0;
        double center = 2 + depth * (0.5 - x * x);
        double wave = 0.35 + 0.65 * Math.Abs(Math.Sin(t * (6.5 + 2.7 * Hash(col)) + col * 1.7));
        double reach = Math.Max(level * bounce * wave * 2.2 * (1 - 0.35 * x * x), floor * (1 - 0.3 * x * x));
        if (shape == MouthShape.Tongue && col is 4 or 5 && row > center + 0.5 && row < center + 2.6)
        {
            color = Pink;
            return 1;
        }
        return Clamp01(1 + reach - Math.Abs(row - center));
    }

    protected override void DrawBrows(DrawingContext dc, in FaceFrame f)
    {
        double a = Ramp(Math.Abs(f.Brows), 0.05, 0.3);
        if (a <= 0) return;
        double b = Math.Clamp(f.Brows, -1, 1);
        var led = _led.Get(f.Accent);
        Brow(dc, Left, b, a, led);
        Brow(dc, Right, b, a, led);
    }

    /// <summary>Four dashes from the outer end to the inner one, which drops (angry) or rises (worried).</summary>
    static void Brow(DrawingContext dc, Eye e, double b, double a, Color led)
    {
        double inward = e.IsLeft ? 1 : -1;
        for (int i = 0; i < 4; i++)
        {
            double k = i / 3.0;
            var p = new Point(e.C.X + inward * (-7.5 + 15 * k), 13 + b - 4 * b * k);
            Led(dc, new Rect(p.X - 1.6, p.Y - 0.9, 3.2, 1.8), a, led, led);
        }
    }

    protected override void DrawBlush(DrawingContext dc, in FaceFrame f)
    {
        double a = Clamp01(f.Blush);
        if (a < 0.03) return;
        for (int i = 0; i < 3; i++)
        {
            Led(dc, new Rect(15.8 + i * 3.4, 41.5, 2.4, 1.6), a, Pink, Pink);
            Led(dc, new Rect(73.8 + i * 3.4, 41.5, 2.4, 1.6), a, Pink, Pink);
        }
    }

    protected override void DrawBehind(DrawingContext dc, in FaceFrame f)
    {
        // Two antennae on bolts at the top corners, their tips blinking in turn.
        dc.DrawGeometry(null, Rod, Rods);
        dc.DrawEllipse(Metal, MetalEdge, new Point(18, 9), 2.6, 2.6);
        dc.DrawEllipse(Metal, MetalEdge, new Point(80, 9), 2.6, 2.6);
        dc.DrawGeometry(null, MetalEdge, Slots);
        var led = _led.Get(f.Accent);
        for (int i = 0; i < 2; i++)
        {
            double b = 0.5 + 0.5 * Math.Sin(f.Time * 4 + i * Math.PI);
            var tip = new Point(i == 0 ? 13 : 85, -3.5);
            dc.DrawEllipse(Paints.Fill(led, 0.3 * b), null, tip, 3.2, 3.2);
            dc.DrawEllipse(Paints.Fill(led, 0.4 + 0.6 * b), MetalEdge, tip, 1.8, 1.8);
        }
    }

    static void Led(DrawingContext dc, Rect core, double b, Color color, Color off)
    {
        if (b < 0.04)
        {
            dc.DrawRoundedRectangle(Paints.Fill(off, 0.13), null, core, 0.8, 0.8);
            return;
        }
        var halo = core;
        halo.Inflate(1.1, 1.1);
        dc.DrawRoundedRectangle(Paints.Fill(color, 0.25 * b), null, halo, 1.8, 1.8);
        dc.DrawRoundedRectangle(Paints.Fill(color, 0.3 + 0.7 * b), null, core, 0.8, 0.8);
    }

    static double Lit(string[] map, int col, int row, bool mirror)
    {
        var line = map[row];
        return line[mirror ? line.Length - 1 - col : col] == '#' ? 1 : 0;
    }

    /// <summary>Dizzy: a comet chasing round the panel's edge and another round the inner ring, the other way.</summary>
    static double SpinnerAt(int col, int row, double t, bool mirror)
    {
        if (mirror) col = Cols - 1 - col;
        if (Ring(col, row, 0, 0, Cols - 1, Rows - 1, out double u)) return Comet(t * 1.1 - u);
        if (Ring(col, row, 1, 1, Cols - 2, Rows - 2, out u)) return 0.85 * Comet(u + t * 1.6);
        return 0.35;
    }

    /// <summary>Where a dot sits on a rectangle's border, 0 to 1 clockwise from its top left corner.</summary>
    static bool Ring(int col, int row, int left, int top, int right, int bottom, out double u)
    {
        double w = right - left, h = bottom - top, along;
        if (row == top && col >= left && col <= right) along = col - left;
        else if (col == right && row > top && row <= bottom) along = w + row - top;
        else if (row == bottom && col >= left && col < right) along = w + h + right - col;
        else if (col == left && row > top && row < bottom) along = 2 * w + h + bottom - row;
        else
        {
            u = 0;
            return false;
        }
        u = along / (2 * (w + h));
        return true;
    }

    /// <summary>A bright head with a tail fading behind it; <paramref name="behind"/> is how far behind the head, in loops.</summary>
    static double Comet(double behind)
    {
        double d = Frac(behind) / 0.6;
        return d >= 1 ? 0 : (1 - d) * (1 - d);
    }
}

/// <summary>7. Slit-pupil cat eyes, ears, whiskers, a pink nose and a ":3" smile.</summary>
sealed class CatFace : FaceSet
{
    static readonly Brush Iris = Paints.Frozen(new RadialGradientBrush(Paints.Rgb(0xF7FA9E), Paints.Rgb(0xA2D64A)));
    static readonly Brush Nose = Paints.Solid(0xFF8FA8);
    static readonly Pen NoseEdge = Paints.MakePen(Paints.Rgb(0xC9566F), 0.9);
    static readonly Brush Fur = Paints.Solid(0x2B2E38);
    static readonly Brush InnerEar = Paints.Solid(0xFF9DB5);
    static readonly Geometry NoseShape = Parse("M45.8,42.6 Q49,42.1 52.2,42.6 Q51.2,45 49,45.8 Q46.8,45 45.8,42.6 Z");
    static readonly Geometry CatSmile = Parse("M49,45.8 L49,47.6 M41.5,46.8 Q45,51.6 49,47.6 Q53,51.6 56.5,46.8");
    static readonly Geometry Whiskers = Parse("M23,44.5 Q15,42 7,41.5 M23,47.5 Q15,47.2 6,48.2 M23,50.5 Q15,52 8,55 M75,44.5 Q83,42 91,41.5 M75,47.5 Q83,47.2 92,48.2 M75,50.5 Q83,52 90,55");
    static readonly Geometry Ears = Parse("M5,6 L11,-7 L24,1.5 Z M93,6 L87,-7 L74,1.5 Z");
    static readonly Geometry EarInsides = Parse("M9.2,3.6 L12.2,-2.9 L18.7,1.3 Z M88.8,3.6 L85.8,-2.9 L79.3,1.3 Z");

    public CatFace()
    {
        Left = new Eye(new Point(36, 29.5), 9.6, true, Almond(9.4, 8.4, 0.6, 0.6, 1.4));
        Right = new Eye(new Point(62, 29.5), 9.6, false, Almond(9.4, 8.4, 0.6, 0.6, -1.4));
        Mouth = new MouthKit(new Point(49, 56.5));
        CheekLeft = new Point(28, 43);
        CheekRight = new Point(70, 43);
    }

    protected override Brush EyeFill(in FaceFrame f) => Iris;

    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood)
    {
        // A vertical slit that opens into a round pupil as Pupil grows (or the eye fills with tears).
        var g = Gaze(f);
        double width = (1.2 + 3.8 * Ramp(Math.Clamp(f.Pupil, 0.5, 1.5), 0.6, 1.4)) * mood switch { Mood.Wide => 0.55, Mood.Teary => 1.7, _ => 1 };
        double h = e.Ry * 0.88;
        var c = new Point(e.C.X + g.X * 2.6, e.C.Y + 0.6 + g.Y * 1.4);
        var slit = new StreamGeometry();
        using (var s = slit.Open())
        {
            s.BeginFigure(new Point(c.X, c.Y - h), true, true);
            s.QuadraticBezierTo(new Point(c.X + width, c.Y), new Point(c.X, c.Y + h), false, true);
            s.QuadraticBezierTo(new Point(c.X - width, c.Y), new Point(c.X, c.Y - h), false, true);
        }
        slit.Freeze();
        dc.DrawGeometry(Dark, null, slit);
        dc.DrawEllipse(Shine, null, new Point(c.X - 2.8, e.C.Y - 3.4), 1.6, 1.6);
        if (mood == Mood.Teary) dc.DrawEllipse(Shine, null, new Point(c.X + 2.4, e.C.Y + 2.2), 0.9, 0.9);
    }

    protected override void DrawBehind(DrawingContext dc, in FaceFrame f)
    {
        dc.DrawGeometry(Fur, Paints.Stroke(LineInk(f), 1.4, 0.6), Ears);
        dc.DrawGeometry(InnerEar, null, EarInsides);
    }

    protected override void DrawMouth(DrawingContext dc, in FaceFrame f)
    {
        var ink = LineInk(f);
        if (f.Mouth is MouthShape.Smile or MouthShape.Cat)
            dc.DrawGeometry(null, Paints.Stroke(ink, 2.4), CatSmile);
        else
            Mouth.Draw(dc, f, f.Mouth, ink);
        dc.DrawGeometry(Nose, NoseEdge, NoseShape);
    }

    protected override void DrawFront(DrawingContext dc, in FaceFrame f)
        => dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 1.5), Whiskers);
}

/// <summary>8. Goofy: googly eyes of two sizes with wobbling pupils, a droopy lid, a toothy grin.</summary>
sealed class DerpFace : FaceSet
{
    static readonly Transform Askew = Paints.Frozen(new RotateTransform(-7, 49, 54));
    static readonly Color DroolBlue = Paints.Rgb(0xA8DCFF);
    static readonly Pen DroolEdge = Paints.MakePen(Paints.Rgb(0x4C9BD6), 0.8 / 1.5);
    static readonly Geometry Flat = Parse("M36,53 Q47,51 62,56");
    static readonly Geometry Frown = Parse("M37,59 Q43,50 51,54 Q57,57 61,58.5");
    static readonly Geometry TongueSmile = Parse("M36,50 Q49,60 62,49");
    static readonly Geometry TongueOut = Parse("M50,54.2 L50,58.2 Q50,62.6 54,62.6 Q58,62.6 58,58.2 L58,52.4 Z");
    static readonly Geometry TongueGroove = Parse("M54,57 L54,59.8");
    static readonly Pen Groove = Paints.MakePen(Paints.Rgb(0xD9506A), 1.1);

    public DerpFace()
    {
        Left = new Eye(new Point(34, 28), 11.5, true, Oval(11.5));
        Right = new Eye(new Point(64.5, 31), 8, false, Oval(8));
        Mouth = new MouthKit(new Point(49, 54));
        CheekLeft = new Point(27, 44);
        CheekRight = new Point(72, 43.5);
    }

    // The small eye never quite opens.
    protected override void DrawEye(DrawingContext dc, in FaceFrame f, Eye e, double lid)
        => base.DrawEye(dc, f, e, e == Right ? 0.22 + 0.78 * Clamp01(lid) : lid);

    protected override void DrawInside(DrawingContext dc, in FaceFrame f, Eye e, Mood mood)
    {
        // Googly pupils: heavy, lagging behind the gaze, wobbling, sinking toward the bottom.
        bool big = e == Left;
        double r = (big ? 5.4 : 3.8) * PupilSize(f, mood);
        double phase = big ? 0 : 2.1, t = f.Time;
        var g = Gaze(f);
        var off = new Vector(g.X * 3 + 1.3 * Math.Sin(t * 5.3 + phase), g.Y * 2.5 + 1.6 + 0.9 * Math.Sin(t * 4.1 + phase * 1.7));
        double reach = e.Rx - r - 1.2;
        if (off.Length > reach) off *= reach / off.Length;
        var p = e.C + off;
        dc.DrawEllipse(Dark, null, p, r, r);
        dc.DrawEllipse(Shine, null, new Point(p.X - r * 0.4, p.Y - r * 0.45), r * 0.28, r * 0.28);
    }

    protected override void DrawBrows(DrawingContext dc, in FaceFrame f)
    {
        double a = Ramp(Math.Abs(f.Brows), 0.05, 0.3);
        if (a <= 0) return;
        double b = Math.Clamp(f.Brows, -1, 1);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            AddBrow(g, Left, b, 11.5, 8, 1.4, 3.5, 0);
            AddBrow(g, Right, b, 18.5, 5.5, 0.4, 2.5, -1);
        }
        geo.Freeze();
        dc.DrawGeometry(null, Paints.Stroke(LineInk(f), 2.6, a), geo);
    }

    protected override void DrawMouth(DrawingContext dc, in FaceFrame f)
    {
        var ink = LineInk(f);
        switch (f.Mouth)
        {
            case MouthShape.Smile: DrawGrin(dc, ink, 13, false); break;
            case MouthShape.Grin: DrawGrin(dc, ink, 15, false); break;
            case MouthShape.Open: DrawGrin(dc, ink, 9 + 9 * Clamp01(f.Open), true); break;
            case MouthShape.Flat: dc.DrawGeometry(null, Paints.Stroke(ink, 3), Flat); break;
            case MouthShape.Frown: dc.DrawGeometry(null, Paints.Stroke(ink, 3), Frown); break;
            case MouthShape.Tongue:
                dc.DrawGeometry(MouthKit.TongueFill, Paints.Stroke(ink, 1.8), TongueOut);
                dc.DrawGeometry(null, Groove, TongueGroove);
                dc.DrawGeometry(null, Paints.Stroke(ink, 3), TongueSmile);
                DrawDrool(dc, f.Time);
                break;
            default:
                dc.PushTransform(Askew);
                Mouth.Draw(dc, f, f.Mouth, ink);
                dc.Pop();
                break;
        }
    }

    /// <summary>A wide, lopsided D of a mouth with a row of square teeth along the top.</summary>
    static void DrawGrin(DrawingContext dc, Color ink, double depth, bool tongue)
    {
        static Point B(double x, double y) => new(29 + x, 45 + y);
        var hole = new StreamGeometry();
        using (var g = hole.Open())
        {
            g.BeginFigure(B(3, 4), true, true);
            g.QuadraticBezierTo(B(20, 7.5), B(37, 2.5), true, true);
            g.QuadraticBezierTo(B(35.5, depth), B(20, depth), true, true);
            g.QuadraticBezierTo(B(5, depth), B(3, 4), true, true);
        }
        hole.Freeze();
        dc.DrawGeometry(MouthKit.Hole, null, hole);
        dc.PushClip(hole);
        double bottom = Math.Min(9.5, depth - 1.6);
        for (int i = 0; i < 6; i++)
            dc.DrawRectangle(MouthKit.Enamel, MouthKit.Gaps, new Rect(B(5.6 + i * 4.8, 0.5), B(10.4 + i * 4.8, bottom)));
        if (tongue && depth > 12) dc.DrawEllipse(MouthKit.TongueFill, null, B(20, depth - 2.4), 6.5, 3.2);
        dc.Pop();
        dc.DrawGeometry(null, Paints.Stroke(ink, 2.6), hole);
    }

    /// <summary>A drop slides down a thread from the corner of the mouth, lets go and falls.</summary>
    static void DrawDrool(DrawingContext dc, double t)
    {
        double p = Frac(t / 2.4);
        double a = Ramp(p, 0, 0.1) * (1 - Ramp(p, 0.82, 1));
        if (a < 0.02) return;
        var corner = new Point(37, 50.8);
        double fall = Ramp(p, 0.7, 1);
        double y = corner.Y + 1.8 + Smooth(Ramp(p, 0, 0.7)) * 5.5 + fall * fall * 9;
        if (fall == 0) dc.DrawLine(Paints.Stroke(DroolBlue, 0.9, 0.8 * a), corner, new Point(corner.X, y - 1.5));
        dc.PushTransform(Place(corner.X, y, 1.5, 0));
        dc.DrawGeometry(Paints.Fill(DroolBlue, 0.9 * a), Paints.Fade(DroolEdge, a), DropShape);
        dc.Pop();
    }
}

// ====================================================================== paint

/// <summary>
/// Frozen brushes and pens. Colors that change at run time (ink, accent, fades) are kept per color,
/// alphas in 1/32 steps and widths in 1/8 units, so a steady frame creates none.
/// </summary>
static class Paints
{
    const int Limit = 2048;
    [ThreadStatic] static Dictionary<uint, SolidColorBrush>? _fills;
    [ThreadStatic] static Dictionary<ulong, Pen>? _strokes;
    [ThreadStatic] static Dictionary<(Pen, int), Pen>? _fades;

    public static SolidColorBrush Fill(Color c)
    {
        var fills = _fills ??= new Dictionary<uint, SolidColorBrush>();
        uint key = Key(c);
        if (fills.TryGetValue(key, out var brush)) return brush;
        if (fills.Count >= Limit) fills.Clear();
        return fills[key] = Frozen(new SolidColorBrush(c));
    }

    public static SolidColorBrush Fill(Color c, double alpha) => Fill(Fade(c, alpha));

    /// <summary>A pen with round caps and joins.</summary>
    public static Pen Stroke(Color c, double width)
    {
        var strokes = _strokes ??= new Dictionary<ulong, Pen>();
        int w = Math.Max(1, (int)Math.Round(width * 8));
        ulong key = (ulong)Key(c) << 16 | (uint)w;
        if (strokes.TryGetValue(key, out var pen)) return pen;
        if (strokes.Count >= Limit) strokes.Clear();
        return strokes[key] = MakePen(Fill(c), w / 8.0);
    }

    public static Pen Stroke(Color c, double width, double alpha) => Stroke(Fade(c, alpha), width);

    /// <summary>A fixed pen (any width, for shapes drawn at a scale) faded to <paramref name="alpha"/>.</summary>
    public static Pen Fade(Pen pen, double alpha)
    {
        int step = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 32);
        if (step == 32) return pen;
        var fades = _fades ??= new Dictionary<(Pen, int), Pen>();
        if (fades.TryGetValue((pen, step), out var faded)) return faded;
        if (fades.Count >= Limit) fades.Clear();
        var color = ((SolidColorBrush)pen.Brush).Color;
        return fades[(pen, step)] = MakePen(Fill(Fade(color, step / 32.0)), pen.Thickness);
    }

    public static Color Fade(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(c.A * Math.Round(Math.Clamp(alpha, 0, 1) * 32) / 32), c.R, c.G, c.B);

    public static Color Rgb(uint rgb, double alpha = 1)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static SolidColorBrush Solid(uint rgb, double alpha = 1) => Frozen(new SolidColorBrush(Rgb(rgb, alpha)));

    public static Pen MakePen(Color c, double width) => MakePen(Frozen(new SolidColorBrush(c)), width);

    public static Pen MakePen(Brush brush, double width)
        => Frozen(new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    public static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    static uint Key(Color c) => (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
}

/// <summary>One value made from a color (an accent's iris, a tinted ink), made again only when the color changes.</summary>
sealed class ByColor<T>
{
    readonly Func<Color, T> _make;
    Color _color;
    T _value = default!;
    bool _made;

    public ByColor(Func<Color, T> make) => _make = make;

    public T Get(Color c)
    {
        if (!_made || c != _color)
        {
            _color = c;
            _value = _make(c);
            _made = true;
        }
        return _value;
    }
}
