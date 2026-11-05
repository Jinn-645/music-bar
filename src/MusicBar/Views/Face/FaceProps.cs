using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using MusicBar.Infrastructure;

namespace MusicBar.Views.Face;

/// <summary>
/// Draws what the face wears (<see cref="FaceOutfit"/>) in design units, fitted to each set: a hat on
/// the set's own head (between the cat's ears, over the anime hair, under the alien's antennae),
/// glasses on its eyes, headphone cups at its sides, the promo film's mic at its right and the focus
/// timer in a top corner. Shapes are built once around their own origin and placed by each set's
/// <see cref="Fit"/>; colors that follow the song or the taskbar are kept per color, so a steady
/// frame only allocates for the parts that move (the mic, rabbit ears, the timer's pie).
/// </summary>
public static class FaceProps
{
    /// <summary>Parts behind the face's features (a headphone band, rabbit ears, a hat the antennae poke through).</summary>
    public static void DrawBehind(DrawingContext dc, FaceStyle style, in FaceFrame f, in FaceOutfit o)
    {
        var fit = FitFor(style);
        if (o.Headphones && !fit.BandInFront) DrawBand(dc, fit, f);
        if (o.Hat != FaceHat.None && fit.Behind(o.Hat)) DrawHat(dc, fit, f, o.Hat);
    }

    /// <summary>Parts over the features (hats, glasses, headphone cups, the mic, the timer).</summary>
    public static void DrawFront(DrawingContext dc, FaceStyle style, in FaceFrame f, in FaceOutfit o)
    {
        var fit = FitFor(style);
        if (o.Headphones && fit.BandInFront) DrawBand(dc, fit, f);
        if (o.Hat != FaceHat.None && !fit.Behind(o.Hat)) DrawHat(dc, fit, f, o.Hat);
        if (o.Glasses != FaceGlasses.None) DrawGlasses(dc, fit, f, o.Glasses);
        if (o.Headphones) DrawCups(dc, fit, f);
        if (o.Mic > 0.02) DrawMic(dc, fit, f, o.Mic);
        if (o.Timer is double left) DrawTimer(dc, fit, left, o.TimerBreak);
    }

    // The picture face (Custom) has no fit of its own: drawn as the classic face until it has pictures.
    static Fit FitFor(FaceStyle style) => (uint)style < (uint)Fits.Length && style != FaceStyle.Custom ? Fits[(int)style] : Fits[0];

    // ------------------------------------------------------------------ shared paint

    static readonly Color Outline = Paints.Rgb(0x15171E);
    static readonly Pen Rim = Paints.MakePen(Outline, 1.2);
    static readonly Pen FineRim = Paints.MakePen(Outline, 0.8);
    static readonly Brush Snow = Paints.Solid(0xFFFDF6);
    static readonly Pen Gleam = Paints.MakePen(Paints.Rgb(0xFFFFFF, 0.55), 1.3);

    /// <summary>A faint outline in the taskbar's ink, so dark props stand off a dark taskbar (as the sets' hair does).</summary>
    static Pen Halo(in FaceFrame f) => Paints.Stroke(f.Ink, 1.2, 0.5);

    // ------------------------------------------------------------------ hats

    static void DrawHat(DrawingContext dc, Fit fit, in FaceFrame f, FaceHat hat)
    {
        switch (hat)
        {
            case FaceHat.Cap: Cap(dc, fit, f.Accent); break;
            case FaceHat.Beanie: Beanie(dc, fit); break;
            case FaceHat.Crown: Crown(dc, fit); break;
            case FaceHat.Santa: Santa(dc, fit); break;
            case FaceHat.Witch: Witch(dc, fit); break;
            case FaceHat.Bunny: Bunny(dc, fit, f.Time); break;
            case FaceHat.Mai: Mai(dc, fit); break;
        }
    }

    /// <summary>One part of a hat, stretched onto the set's head; outlines keep their width.</summary>
    static void Part(DrawingContext dc, Fit fit, Geometry shape, Brush? fill, Pen? pen) => dc.DrawGeometry(fill, pen, fit.OnHead(shape));

    // The baseball cap: a dome in the song's color with a star on the front and a darker peak.
    static readonly Geometry CapDome = FaceSet.Parse("M-20,-6 C-20,-14.4 -11.5,-18.2 0,-18.2 C11.5,-18.2 20,-14.4 20,-6 Z");
    static readonly Geometry CapSeams = FaceSet.Parse("M0,-17.8 L0,-6.4 M-1.3,-17.8 Q-10,-14.8 -12.2,-6.6 M1.3,-17.8 Q10,-14.8 12.2,-6.6");
    static readonly Geometry CapPeak = FaceSet.Parse("M-23.5,-6.6 Q0,-3.4 23.5,-6.6 Q23,1.2 0,1.2 Q-23,1.2 -23.5,-6.6 Z");
    static readonly Geometry CapPeakEdge = FaceSet.Parse("M-21.6,-2.6 Q0,2.6 21.6,-2.6");
    static readonly Geometry CapButton = Paints.Frozen(new EllipseGeometry(new Point(0, -18.2), 2.1, 1.3));
    static readonly Geometry CapShine = FaceSet.Parse("M-16.2,-11.6 Q-13.2,-15.8 -6.6,-17.2");
    static readonly Geometry CapStar = Star(new Point(0, -11.6), 3.5, 1.5);
    static readonly ByColor<CapPaint> CapPaints = new(CapPaint.Make);

    static void Cap(DrawingContext dc, Fit fit, Color accent)
    {
        var p = CapPaints.Get(accent);
        Part(dc, fit, CapDome, p.Crown, Rim);
        Part(dc, fit, CapSeams, null, p.Seam);
        Part(dc, fit, CapShine, null, Gleam);
        Part(dc, fit, CapStar, Snow, null);
        Part(dc, fit, CapButton, p.Peak, FineRim);
        Part(dc, fit, CapPeak, p.Peak, Rim);
        Part(dc, fit, CapPeakEdge, null, p.Stitch);
    }

    sealed record CapPaint(Brush Crown, Brush Peak, Pen Seam, Pen Stitch)
    {
        public static CapPaint Make(Color accent)
        {
            var c = ColorUtil.ForDarkSurface(accent);
            return new CapPaint(Paints.Frozen(new SolidColorBrush(c)), Paints.Frozen(new SolidColorBrush(ColorUtil.Lerp(c, Colors.Black, 0.3))),
                Paints.MakePen(ColorUtil.WithAlpha(ColorUtil.Lerp(c, Colors.Black, 0.5), 0.7), 0.8),
                Paints.MakePen(ColorUtil.WithAlpha(ColorUtil.Lerp(c, Colors.White, 0.35), 0.8), 0.8));
        }
    }

    // The knitted beanie: ribbed teal with a turned-up cuff and a cream pom-pom.
    static readonly Brush Knit = Paints.Solid(0x2DB3A2);
    static readonly Brush KnitCuff = Paints.Solid(0x22968A);
    static readonly Pen KnitRib = Paints.MakePen(Paints.Rgb(0x0F5A52, 0.55), 0.9);
    static readonly Brush Pom = Paints.Solid(0xFFF1D6);
    static readonly Pen PomTuft = Paints.MakePen(Paints.Rgb(0xD9B98A), 0.7);
    static readonly Geometry BeanieBody = FaceSet.Parse("M-19.6,-6 C-20,-12.4 -12,-15.4 0,-15.4 C12,-15.4 20,-12.4 19.6,-6 Z");
    static readonly Geometry BeanieRibs = FaceSet.Parse("M-13,-6.8 Q-13.6,-10.6 -10.2,-13.4 M-4.4,-6.8 Q-4.6,-11.2 -3.2,-14.8 M4.4,-6.8 Q4.6,-11.2 3.2,-14.8 M13,-6.8 Q13.6,-10.6 10.2,-13.4");
    static readonly Geometry BeanieCuff = Paints.Frozen(new RectangleGeometry(new Rect(-21.5, -7.2, 43, 7.2), 2.8, 2.8));
    static readonly Geometry BeanieCuffRibs = Ribs(-18, 18, 3.6, -5.8, -1.4);
    static readonly Geometry BeaniePom = Paints.Frozen(new EllipseGeometry(new Point(0, -15.2), 3.6, 3.5));
    static readonly Geometry PomTufts = FaceSet.Parse("M-1.9,-16.4 l0.9,0.5 M0.5,-17.3 l0.5,0.8 M0.8,-14.4 l1,0.3 M-1.7,-13.9 l0.7,-0.6");

    static void Beanie(DrawingContext dc, Fit fit)
    {
        Part(dc, fit, BeanieBody, Knit, Rim);
        Part(dc, fit, BeanieRibs, null, KnitRib);
        Part(dc, fit, BeanieCuff, KnitCuff, Rim);
        Part(dc, fit, BeanieCuffRibs, null, KnitRib);
        Part(dc, fit, BeaniePom, Pom, Rim);
        Part(dc, fit, PomTufts, null, PomTuft);
    }

    // The crown: three gold points with pearls, a band with three gems.
    static readonly Brush Gold = Paints.Solid(0xFFC83D);
    static readonly Brush GoldDeep = Paints.Solid(0xE69A1C);
    static readonly Pen GoldEdge = Paints.MakePen(Paints.Rgb(0x6B4200), 1.1);
    static readonly Brush Ruby = Paints.Solid(0xE8384F);
    static readonly Brush Sapphire = Paints.Solid(0x46A8FF);
    static readonly Geometry CrownBody = FaceSet.Parse("M-14,0 L-14.6,-5.2 L-17.4,-13.8 L-8.6,-8.2 L0,-15.8 L8.6,-8.2 L17.4,-13.8 L14.6,-5.2 L14,0 Z");
    static readonly Geometry CrownBand = FaceSet.Parse("M-14,0 L-14.6,-5.2 L14.6,-5.2 L14,0 Z");
    static readonly Geometry CrownPearls = Dots((new Point(-17.4, -13.8), 1.8), (new Point(0, -16), 2), (new Point(17.4, -13.8), 1.8));
    static readonly Geometry CrownRuby = Paints.Frozen(new EllipseGeometry(new Point(0, -2.6), 2.3, 1.7));
    static readonly Geometry CrownSapphires = Dots((new Point(-8.4, -2.6), 1.3), (new Point(8.4, -2.6), 1.3));
    static readonly Geometry CrownShine = FaceSet.Parse("M-13.4,-6.6 L-15.2,-11.8 M-1.8,-7.2 L-0.5,-12.8");

    static void Crown(DrawingContext dc, Fit fit)
    {
        Part(dc, fit, CrownBody, Gold, GoldEdge);
        Part(dc, fit, CrownBand, GoldDeep, GoldEdge);
        Part(dc, fit, CrownShine, null, Gleam);
        Part(dc, fit, CrownPearls, Gold, GoldEdge);
        Part(dc, fit, CrownRuby, Ruby, FineRim);
        Part(dc, fit, CrownSapphires, Sapphire, FineRim);
    }

    // Christmas: a red Santa hat flopping to the right, a white fur trim and bobble.
    static readonly Brush SantaRed = Paints.Solid(0xE8403A);
    static readonly Pen SantaFold = Paints.MakePen(Paints.Rgb(0x9E1E1E, 0.8), 0.9);
    static readonly Geometry SantaBody = FaceSet.Parse("M-19.5,-5 C-19,-14 -9.6,-18.4 1,-18.4 C11,-18.4 19.6,-14.6 25.4,-8 C21.2,-10.2 19.2,-9.6 19.2,-5 Z");
    static readonly Geometry SantaFolds = FaceSet.Parse("M10,-17 Q16.6,-14.2 20,-9.2");
    static readonly Geometry SantaTrim = Paints.Frozen(new RectangleGeometry(new Rect(-22.5, -6.9, 45, 7), 3.4, 3.4));
    static readonly Geometry SantaFur = FaceSet.Parse("M-17,-2.8 l0.1,0 M-10.2,-4.4 l0.1,0 M-3.4,-2.6 l0.1,0 M3.4,-4.4 l0.1,0 M10.2,-2.6 l0.1,0 M17,-4.2 l0.1,0");
    static readonly Geometry SantaBobble = Paints.Frozen(new EllipseGeometry(new Point(26, -7.2), 3.7, 3.7));
    static readonly Pen FurDots = Paints.MakePen(Paints.Rgb(0xC3CAD6), 1.6);

    static void Santa(DrawingContext dc, Fit fit)
    {
        Part(dc, fit, SantaBody, SantaRed, Rim);
        Part(dc, fit, SantaFolds, null, SantaFold);
        Part(dc, fit, SantaTrim, Snow, Rim);
        Part(dc, fit, SantaFur, null, FurDots);
        Part(dc, fit, SantaBobble, Snow, Rim);
    }

    // Halloween: a pointed hat, purple so it shows on a dark taskbar, with an orange band and a buckle.
    static readonly Brush WitchPurple = Paints.Solid(0x8A5CF5);
    static readonly Brush WitchBrimFill = Paints.Solid(0x6B3FD8);
    static readonly Brush WitchBandFill = Paints.Solid(0xFF972E);
    static readonly Pen Buckle = Paints.MakePen(Paints.Rgb(0xFFE066), 1);
    static readonly Geometry WitchBrim = Paints.Frozen(new EllipseGeometry(new Point(0, -3.3), 28.5, 3.4));
    static readonly Geometry WitchCone = FaceSet.Parse("M-11,-4.8 C-8.4,-9.6 -4,-14.2 1.8,-17.4 Q7.2,-20 10.8,-15.6 Q6.8,-15.9 5.2,-13.4 C4.8,-10.4 7.4,-7.4 11,-4.8 Q0,-2.8 -11,-4.8 Z");
    static readonly Geometry WitchBand = Paints.Frozen(Geometry.Combine(WitchCone, new RectangleGeometry(new Rect(-13, -8.8, 26, 4.6)), GeometryCombineMode.Intersect, null));
    static readonly Geometry WitchBuckle = Paints.Frozen(new RectangleGeometry(new Rect(-2.4, -8.3, 4.8, 3.3), 0.6, 0.6));

    static void Witch(DrawingContext dc, Fit fit)
    {
        Part(dc, fit, WitchBrim, WitchBrimFill, Rim);
        Part(dc, fit, WitchCone, WitchPurple, Rim);
        Part(dc, fit, WitchBand, WitchBandFill, null);
        Part(dc, fit, WitchBuckle, null, Buckle);
        Part(dc, fit, WitchCone, null, Rim);
    }

    // Mid-Autumn: the jade rabbit's ears, white with pink insides; the right one twitches now and then.
    static readonly Brush EarPink = Paints.Solid(0xFFA8C0);
    static readonly Geometry Ear = FaceSet.Parse("M0,0 C-3.9,-1.6 -5,-10 -4,-14.6 C-3,-19 3,-19 4,-14.6 C5,-10 3.9,-1.6 0,0 Z");
    static readonly Geometry EarInside = FaceSet.Parse("M0,-2.8 C-2,-4.4 -2.6,-9.8 -2.1,-13.2 C-1.6,-16.2 1.6,-16.2 2.1,-13.2 C2.6,-9.8 2,-4.4 0,-2.8 Z");

    static void Bunny(DrawingContext dc, Fit fit, double t)
    {
        var (x, y, apart, scale, splay, tilt, _) = fit.Ears;
        double twitch = 10 * Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 / 3.7)), 24);
        double r = tilt * Math.PI / 180;
        for (int i = 0; i < 2; i++)
        {
            double side = i == 0 ? -1 : 1;
            var at = new Point(x + side * apart * Math.Cos(r), y + side * apart * Math.Sin(r));
            double turn = tilt + side * splay + (i == 1 ? twitch : 0);
            dc.PushTransform(FaceSet.Place(at.X, at.Y, scale, turn));
            dc.DrawGeometry(Snow, Rim, Ear);
            dc.DrawGeometry(EarPink, null, EarInside);
            dc.Pop();
        }
    }

    // Lunar New Year: a sprig of hoa mai, yellow five-petal apricot blossoms, tucked at one side of the head.
    static readonly Brush MaiYellow = Paints.Solid(0xFFD53D);
    static readonly Pen MaiEdge = Paints.MakePen(Paints.Rgb(0xB87800), 0.8);
    static readonly Brush MaiHeart = Paints.Solid(0xE8601C);
    static readonly Pen Twig = Paints.MakePen(Paints.Rgb(0x6E4126), 1.5);
    static readonly Brush LeafGreen = Paints.Solid(0x5DB651);
    static readonly Geometry MaiTwig = FaceSet.Parse("M0,0 Q3.6,-2.6 8.8,-5.4 Q12.2,-7.2 16,-8 M6.4,-4.2 Q6.8,-8.2 4.8,-11.6 M12.2,-6.9 Q13.8,-10.2 13.4,-12.8");
    static readonly Geometry MaiLeaf = FaceSet.Parse("M1.6,-0.9 Q3.2,-4.4 6.4,-4 Q4.8,-1.2 1.6,-0.9 Z");
    static readonly Geometry MaiFlowers = new GeometryGroup
    {
        Children = { Blossom(new Point(8.8, -5.4), 4.4, 12), Blossom(new Point(4.8, -11.6), 3.5, -10) },
    }.Frozen();
    static readonly Geometry MaiHearts = Dots((new Point(8.8, -5.4), 1.25), (new Point(4.8, -11.6), 1));
    static readonly Geometry MaiBuds = Dots((new Point(16.2, -8.2), 1.5), (new Point(13.4, -13.2), 1.3));

    static void Mai(DrawingContext dc, Fit fit)
    {
        dc.PushTransform(fit.Mai);
        dc.DrawGeometry(LeafGreen, FineRim, MaiLeaf);
        dc.DrawGeometry(null, Twig, MaiTwig);
        dc.DrawGeometry(MaiYellow, MaiEdge, MaiBuds);
        dc.DrawGeometry(MaiYellow, MaiEdge, MaiFlowers);
        dc.DrawGeometry(MaiHeart, null, MaiHearts);
        dc.Pop();
    }

    // ------------------------------------------------------------------ glasses

    static readonly Brush LensDark = Paints.Frozen(new LinearGradientBrush(Paints.Rgb(0x5A637C), Paints.Rgb(0x0C0D12), new Point(0.2, 0), new Point(0.7, 1)));
    static readonly Pen ShadeBar = Paints.MakePen(Paints.Rgb(0x1B1D24), 2.4);
    static readonly Pen Glint = Paints.MakePen(Paints.Rgb(0xFFFFFF, 0.85), 1.4);
    static readonly Pen RoundGlare = Paints.MakePen(Paints.Rgb(0xFFFFFF, 0.7), 1.1);
    static readonly Brush PixelBlack = Paints.Solid(0x0B0C10);
    static readonly Brush PixelWhite = Paints.Solid(0xFFFFFF);
    static readonly Geometry Twinkle = FaceSet.Parse("M0,-1 Q0.13,-0.13 1,0 Q0.13,0.13 0,1 Q-0.13,0.13 -1,0 Q-0.13,-0.13 0,-1 Z");

    static void DrawGlasses(DrawingContext dc, Fit fit, in FaceFrame f, FaceGlasses kind)
    {
        var g = fit.Glasses;
        switch (kind)
        {
            case FaceGlasses.Round:
                dc.DrawGeometry(null, Paints.Stroke(f.Ink, 1.9), g.RoundFrame);
                dc.DrawGeometry(null, RoundGlare, g.RoundGlare);
                break;
            case FaceGlasses.Shades:
                dc.DrawGeometry(null, Paints.Stroke(f.Ink, 4, 0.5), g.ShadeArms);
                dc.DrawGeometry(null, ShadeBar, g.ShadeArms);
                foreach (var lens in g.ShadeLenses) dc.DrawGeometry(LensDark, Paints.Stroke(f.Ink, 1.3, 0.65), lens);
                dc.DrawGeometry(null, Glint, g.ShadeGlints);
                // Now and then a twinkle off the top of a lens.
                double s = 3.6 * Math.Pow(Math.Max(0, Math.Sin(f.Time * Math.PI * 2 / 4.3)), 30);
                if (s > 0.2)
                {
                    dc.PushTransform(FaceSet.Place(g.TwinkleAt.X, g.TwinkleAt.Y, s, 20 * f.Time));
                    dc.DrawGeometry(PixelWhite, null, Twinkle);
                    dc.Pop();
                }
                break;
            case FaceGlasses.Pixel:
                dc.DrawGeometry(Paints.Fill(f.Ink, 0.5), null, g.PixelHalo);
                dc.DrawGeometry(PixelBlack, null, g.PixelBody);
                dc.DrawGeometry(PixelWhite, null, g.PixelGlints);
                break;
        }
    }

    /// <summary>
    /// Where glasses sit on a set: the middle between the eyes, the frame's tilt, how far each lens
    /// is from the middle, each lens's half width and height, how far the lenses turn outer corner up,
    /// how long the arms are, and (for square eyes) rounded-square frames and a one-piece visor.
    /// </summary>
    readonly record struct EyesFit(Point Middle, double Tilt, double Apart, Size Left, Size Right,
        double Slant = 0, double Arms = 6, double Corner = 0, bool Visor = false);

    /// <summary>A set's glasses, built once: every shape already in place on the face.</summary>
    sealed class GlassesKit
    {
        // A wayfarer lens in a 2 x 2 box, its outer side at x = -1: a flat top, a round bottom.
        static readonly Geometry Wayfarer = FaceSet.Parse("M-1.08,-1.06 L0.84,-0.94 Q1,-0.93 0.99,-0.76 L0.93,0.14 C0.87,0.8 0.48,1 0.02,1 C-0.58,1 -0.94,0.72 -0.99,0.12 L-1.06,-0.84 Q-1.1,-1.03 -1.08,-1.06 Z");
        static readonly Geometry WayfarerGlints = FaceSet.Parse("M-0.62,-0.12 L-0.14,-0.7 M-0.66,0.36 L-0.5,0.18");
        static readonly string[] PixelLens = { "######", "#ww###", "##ww##", "######", "######", "######", ".####." };

        public readonly Geometry RoundFrame, RoundGlare, ShadeArms, ShadeGlints, PixelBody, PixelHalo, PixelGlints;
        public readonly Geometry[] ShadeLenses;
        public readonly Point TwinkleAt;

        public GlassesKit(EyesFit e)
        {
            var frame = Matrix.Identity;
            frame.Rotate(e.Tilt);
            frame.Translate(e.Middle.X, e.Middle.Y);
            var along = frame.Transform(new Vector(1, 0));

            // Round: thin frames around the eyes (rounded squares for square eyes), a bridge, arms.
            var rims = new GeometryGroup();
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                var size = left ? e.Left : e.Right;
                Geometry rim = e.Corner > 0
                    ? new RectangleGeometry(new Rect(-size.Width, -size.Height, 2 * size.Width, 2 * size.Height), e.Corner, e.Corner)
                    : new EllipseGeometry(new Point(0, 0), size.Width, size.Height);
                var m = Matrix.Identity;
                m.Rotate(left ? e.Slant : -e.Slant);
                m.Translate(left ? -e.Apart : e.Apart, 0);
                m.Append(frame);
                rims.Children.Add(Placed(rim, m));
            }
            var bridgeLeft = LensPoint(e, frame, true, 0.92, -0.4);
            var bridgeRight = LensPoint(e, frame, false, 0.92, -0.4);
            var armLeft = LensPoint(e, frame, true, -0.98, -0.3);
            var armRight = LensPoint(e, frame, false, -0.98, -0.3);
            rims.Children.Add(FaceSet.Build(g =>
            {
                var mid = FaceSet.Lerp(bridgeLeft, bridgeRight, 0.5);
                g.BeginFigure(bridgeLeft, false, false);
                g.QuadraticBezierTo(new Point(mid.X, Math.Min(bridgeLeft.Y, bridgeRight.Y) - 2.6), bridgeRight, true, true);
                g.BeginFigure(armLeft, false, false);
                g.LineTo(armLeft - along * e.Arms + new Vector(0, -1), true, true);
                g.BeginFigure(armRight, false, false);
                g.LineTo(armRight + along * e.Arms + new Vector(0, -1), true, true);
            }));
            RoundFrame = rims.Frozen();
            RoundGlare = Paints.Frozen(new GeometryGroup
            {
                Children =
                {
                    Placed(Glare, LensBox(e, frame, true, mirror: false)),
                    Placed(Glare, LensBox(e, frame, false, mirror: false)),
                },
            });

            // Shades: dark lenses that cover the eyes, joined by a bar, with glints; a visor on the robot.
            Point barLeft, barRight, outerLeft, outerRight;
            if (e.Visor)
            {
                double w = e.Apart + e.Left.Width, h = e.Left.Height;
                var visor = Placed(new RectangleGeometry(new Rect(-w, -h, 2 * w, 2 * h), 6.5, 6.5), frame);
                ShadeLenses = new[] { visor };
                var box = Matrix.Identity;
                box.Scale(e.Left.Width, e.Left.Height);
                box.Translate(-e.Apart, 0);
                box.Append(frame);
                ShadeGlints = Placed(WayfarerGlints, box);
                barLeft = barRight = frame.Transform(new Point(0, -h + 1));
                outerLeft = frame.Transform(new Point(-w, -h * 0.55));
                outerRight = frame.Transform(new Point(w, -h * 0.55));
                TwinkleAt = frame.Transform(new Point(w - 4, -h + 3));
            }
            else
            {
                ShadeLenses = new[] { Placed(Wayfarer, LensBox(e, frame, true)), Placed(Wayfarer, LensBox(e, frame, false)) };
                ShadeGlints = Paints.Frozen(new GeometryGroup
                {
                    Children =
                    {
                        Placed(WayfarerGlints, LensBox(e, frame, true, mirror: false)),
                        Placed(WayfarerGlints, LensBox(e, frame, false, mirror: false)),
                    },
                });
                barLeft = LensBox(e, frame, true).Transform(new Point(0.86, -0.9));
                barRight = LensBox(e, frame, false).Transform(new Point(0.86, -0.9));
                outerLeft = LensBox(e, frame, true).Transform(new Point(-1, -0.8));
                outerRight = LensBox(e, frame, false).Transform(new Point(-1, -0.8));
                TwinkleAt = LensBox(e, frame, false).Transform(new Point(-0.82, -0.86));
            }
            ShadeArms = FaceSet.Build(g =>
            {
                if (!e.Visor)
                {
                    g.BeginFigure(barLeft, false, false);
                    g.LineTo(barRight, true, true);
                }
                g.BeginFigure(outerLeft, false, false);
                g.LineTo(outerLeft - along * e.Arms + new Vector(0, -0.8), true, true);
                g.BeginFigure(outerRight, false, false);
                g.LineTo(outerRight + along * e.Arms + new Vector(0, -0.8), true, true);
            });

            // Pixel: the "deal with it" shades, square pixels big enough for the grid to cover the larger eye.
            int cols = PixelLens[0].Length;
            double p = Math.Max(2 * Math.Max(e.Left.Height, e.Right.Height) / PixelLens.Length, 2 * Math.Max(e.Left.Width, e.Right.Width) / cols);
            var body = new List<Rect>();
            var glints = new List<Rect>();
            double top = -PixelLens.Length * p / 2;
            double leftX = -e.Apart - cols * p / 2, rightX = e.Apart - cols * p / 2;
            foreach (double x0 in new[] { leftX, rightX })
            {
                for (int r = 0; r < PixelLens.Length; r++)
                {
                    var line = PixelLens[r];
                    for (int c = 0; c < cols; c++)
                    {
                        var cell = new Rect(x0 + c * p, top + r * p, p, p);
                        if (line[c] == '#') body.Add(cell);
                        else if (line[c] == 'w') glints.Add(cell);
                    }
                }
            }
            // The bar across the top runs over the nose and two pixels past each lens.
            body.Add(new Rect(leftX - 2 * p, top, rightX + cols * p + 4 * p - leftX, p));
            body.Add(new Rect(leftX + cols * p, top + p, Math.Max(0, rightX - leftX - cols * p), p));
            PixelBody = Cells(body, frame, 0);
            PixelHalo = Cells(body, frame, 0.9);
            PixelGlints = Cells(glints, frame, 0);
        }

        /// <summary>A lens's 2 x 2 box: outer side at x = -1 unless <paramref name="mirror"/> is off (shine falls the same way on both).</summary>
        static Matrix LensBox(EyesFit e, Matrix frame, bool left, bool mirror = true)
        {
            var size = left ? e.Left : e.Right;
            var m = Matrix.Identity;
            m.Scale(size.Width * (left || !mirror ? 1 : -1), size.Height);
            m.Rotate(left ? e.Slant : -e.Slant);
            m.Translate(left ? -e.Apart : e.Apart, 0);
            m.Append(frame);
            return m;
        }

        /// <summary>A point on a lens's edge, <paramref name="x"/> across from the outer side (-1) to the nose (1).</summary>
        static Point LensPoint(EyesFit e, Matrix frame, bool left, double x, double y) => LensBox(e, frame, left).Transform(new Point(x, y));

        static readonly Geometry Glare = FaceSet.Parse("M-0.74,-0.2 A0.76,0.76 0 0 1 -0.3,-0.68");

        static Geometry Cells(List<Rect> cells, Matrix frame, double grow)
        {
            var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var g = geo.Open())
            {
                foreach (var c in cells)
                {
                    var r = c;
                    r.Inflate(grow, grow);
                    g.BeginFigure(frame.Transform(r.TopLeft), true, true);
                    g.LineTo(frame.Transform(r.TopRight), false, false);
                    g.LineTo(frame.Transform(r.BottomRight), false, false);
                    g.LineTo(frame.Transform(r.BottomLeft), false, false);
                }
            }
            geo.Freeze();
            return geo;
        }
    }

    // ------------------------------------------------------------------ headphones

    static readonly Brush PhoneBody = Paints.Solid(0x2C303B);
    static readonly Pen PhoneBand = Paints.MakePen(PhoneBody, 3.2);
    static readonly Pen PhoneRidge = Paints.MakePen(Paints.Rgb(0x5A6175), 0.9);
    static readonly Brush Cushion = Paints.Solid(0x464C5C);
    static readonly Geometry Cup = Paints.Frozen(new RectangleGeometry(new Rect(-4.6, -8.6, 9.2, 17.2), 4.4, 4.4));
    static readonly Geometry CupCushion = FaceSet.Parse("M2.2,-7.6 Q4.6,-7.6 4.6,-4 L4.6,4 Q4.6,7.6 2.2,7.6 Z");
    static readonly Geometry CupShine = FaceSet.Parse("M-2.6,-6 Q-2.4,-7.4 -0.8,-7.6");
    static readonly ByColor<Pen> CupRing = new(c => Paints.MakePen(ColorUtil.ForDarkSurface(c), 1.5));

    static void DrawBand(DrawingContext dc, Fit fit, in FaceFrame f)
    {
        dc.DrawGeometry(null, Paints.Stroke(f.Ink, 4.8, 0.5), fit.Band);
        dc.DrawGeometry(null, PhoneBand, fit.Band);
        dc.DrawGeometry(null, PhoneRidge, fit.Band);
    }

    static void DrawCups(DrawingContext dc, Fit fit, in FaceFrame f)
    {
        var ring = CupRing.Get(f.Accent);
        foreach (var at in fit.Cups)
        {
            dc.PushTransform(at);
            dc.DrawGeometry(PhoneBody, Halo(f), Cup);
            dc.DrawGeometry(Cushion, null, CupCushion);
            dc.DrawEllipse(null, ring, new Point(-0.9, 0), 2.3, 5.2);
            dc.DrawGeometry(null, Gleam, CupShine);
            dc.Pop();
        }
    }

    // ------------------------------------------------------------------ mic

    // The promo film's mic (viewBox 0 0 26 70), with the bottom of the handle at the origin.
    static readonly Geometry MicHandle = Paints.Frozen(new RectangleGeometry(new Rect(-4, -42, 8, 42), 4, 4));
    static readonly Geometry MicBand = Paints.Frozen(new RectangleGeometry(new Rect(-5.5, -39, 11, 4), 2, 2));
    static readonly Geometry MicGrille = Paints.Frozen(new EllipseGeometry(new Point(0, -54), 12, 12));
    static readonly Geometry MicLines = FaceSet.Parse("M-9,-58 H9 M-10,-53 H10 M-8,-48 H8 M-4,-64.5 V-43.5 M0,-66 V-42 M4,-64.5 V-43.5");
    static readonly Brush MicHandleFill = Paints.Solid(0x1D1F27);
    static readonly Pen MicHandleEdge = Paints.MakePen(Paints.Rgb(0x0E1015), 1.2);
    static readonly Brush MicGrilleFill = Paints.Solid(0xC9CDD6);
    static readonly Pen MicGrilleEdge = Paints.MakePen(Paints.Rgb(0x0E1015), 1.4);
    static readonly Pen MicMesh = Paints.MakePen(Paints.Rgb(0x8E94A3), 1);
    static readonly Brush MicShine = Paints.Solid(0xFFFFFF, 0.7);
    static readonly ByColor<Brush> MicStripe = new(c => Paints.Frozen(new SolidColorBrush(ColorUtil.ForDarkSurface(c))));

    static void DrawMic(DrawingContext dc, Fit fit, in FaceFrame f, double amount)
    {
        // Grows from the bottom of the handle, leaning its head toward the mouth; louder music lifts it.
        double level = FaceSet.Clamp01(f.Level);
        var (x, y, scale, tilt) = fit.Mic;
        dc.PushTransform(FaceSet.Place(x, y - 2.4 * level, scale * Math.Min(amount, 1.25), tilt - 5 * level));
        dc.DrawGeometry(null, Paints.Stroke(f.Ink, 3.8, 0.45), MicHandle);
        dc.DrawGeometry(MicHandleFill, MicHandleEdge, MicHandle);
        dc.DrawGeometry(MicStripe.Get(f.Accent), null, MicBand);
        dc.DrawGeometry(MicGrilleFill, MicGrilleEdge, MicGrille);
        dc.DrawGeometry(null, MicMesh, MicLines);
        dc.DrawEllipse(MicShine, null, new Point(-4, -59), 2.4, 2.4);
        dc.Pop();
    }

    // ------------------------------------------------------------------ focus timer

    const double TimerRadius = 8, PieRadius = 5.9;
    static readonly Pen TimerEdge = Paints.MakePen(Outline, 1.4);
    static readonly Brush Tomato = Paints.Solid(0xFF5A4A);
    static readonly Brush Mint = Paints.Solid(0x34D399);
    static readonly Pen PieEdge = Paints.MakePen(Paints.Rgb(0x15171E, 0.55), 0.7);
    static readonly Brush Pin = Paints.Solid(0x15171E);
    /// <summary>A tomato's stalk, its foot at the origin.</summary>
    static readonly Geometry Stalk = FaceSet.Parse("M0,-1 Q-3.4,-2.6 -4.4,-0.8 Q-2.6,0.4 0,-1 Z M0,-1 Q3.4,-2.6 4.4,-0.8 Q2.6,0.4 0,-1 Z M0,-1 L0.6,-3.2");
    static readonly Pen StalkEdge = Paints.MakePen(Paints.Rgb(0x2F7D32), 0.7);

    static void DrawTimer(DrawingContext dc, Fit fit, double left, bool rest)
    {
        // A small dial: the part still to go, from where the hand has got to round to twelve o'clock.
        var c = fit.Timer;
        left = FaceSet.Clamp01(left);
        dc.DrawEllipse(Snow, TimerEdge, c, TimerRadius, TimerRadius);
        var fill = rest ? Mint : Tomato;
        if (left > 0.995)
        {
            dc.DrawEllipse(fill, PieEdge, c, PieRadius, PieRadius);
        }
        else if (left > 0.005)
        {
            double a = (1 - left) * Math.PI * 2;
            var hand = new Point(c.X + Math.Sin(a) * PieRadius, c.Y - Math.Cos(a) * PieRadius);
            var pie = new StreamGeometry();
            using (var g = pie.Open())
            {
                g.BeginFigure(c, true, true);
                g.LineTo(hand, true, true);
                g.ArcTo(new Point(c.X, c.Y - PieRadius), new Size(PieRadius, PieRadius), 0, left > 0.5, SweepDirection.Clockwise, true, true);
            }
            pie.Freeze();
            dc.DrawGeometry(fill, PieEdge, pie);
        }
        dc.DrawEllipse(Pin, null, c, 0.9, 0.9);
        // A focus timer is a tomato: a green stalk on top.
        if (!rest) dc.DrawGeometry(LeafGreen, StalkEdge, fit.TimerStalk);
    }

    // ------------------------------------------------------------------ the sets' fits

    /// <summary>
    /// Where one set wears things. Hats are drawn on a standard head (bottom middle at the origin,
    /// about 44 wide and up to 19 tall) and stretched onto the set's; everything else is placed whole.
    /// </summary>
    sealed class Fit
    {
        readonly Dictionary<Geometry, Geometry> _onHead = new();
        readonly bool _hatsBehind;
        readonly Matrix _head;

        public readonly (double X, double Y, double Apart, double Scale, double Splay, double Tilt, bool InFront) Ears;
        public readonly Transform Mai;
        public readonly GlassesKit Glasses;
        public readonly Geometry Band;
        public readonly bool BandInFront;
        public readonly Transform[] Cups;
        public readonly Point Timer;
        public readonly Geometry TimerStalk;
        public readonly (double X, double Y, double Scale, double Tilt) Mic;

        /// <param name="head">Where hats sit: the bottom middle, the stretch across and up, the tilt.</param>
        /// <param name="hatsBehind">Hats go behind the set's features (so antennae poke through them).</param>
        /// <param name="ears">The rabbit ears: their middle, half the gap between them, size, how far they lean apart, tilt, and whether they lie over the hair.</param>
        /// <param name="mai">The blossom sprig's stem, its size and turn, and whether it leans left.</param>
        /// <param name="phones">The headphone cups' middles, how high the band reaches, whether it lies over the hair.</param>
        public Fit((double X, double Y, double Wide, double High, double Tilt) head, bool hatsBehind,
            (double X, double Y, double Apart, double Scale, double Splay, double Tilt, bool InFront) ears,
            (double X, double Y, double Scale, double Tilt, bool Left) mai,
            EyesFit eyes, (Point Left, Point Right, double Top, bool InFront) phones, Point timer,
            (double X, double Y, double Scale, double Tilt) mic)
        {
            _head = Matrix.Identity;
            _head.Scale(head.Wide, head.High);
            _head.Rotate(head.Tilt);
            _head.Translate(head.X, head.Y);
            _hatsBehind = hatsBehind;
            Ears = ears;

            var m = Matrix.Identity;
            m.Scale(mai.Left ? -mai.Scale : mai.Scale, mai.Scale);
            m.Rotate(mai.Tilt);
            m.Translate(mai.X, mai.Y);
            Mai = Paints.Frozen(new MatrixTransform(m));

            Glasses = new GlassesKit(eyes);

            // The band rises from the top of each cup and peaks at phones.Top over the middle.
            var (l, r) = (phones.Left + new Vector(0, -7.6), phones.Right + new Vector(0, -7.6));
            double lift = (8 * phones.Top - l.Y - r.Y) / 6;
            Band = FaceSet.Build(g =>
            {
                g.BeginFigure(l, false, false);
                g.BezierTo(new Point(l.X - 1, lift), new Point(r.X + 1, lift), r, true, true);
            });
            BandInFront = phones.InFront;
            Cups = new Transform[]
            {
                Paints.Frozen(new MatrixTransform(1, 0, 0, 1, phones.Left.X, phones.Left.Y)),
                Paints.Frozen(new MatrixTransform(-1, 0, 0, 1, phones.Right.X, phones.Right.Y)),
            };
            Timer = timer;
            TimerStalk = Placed(Stalk, new Matrix(1, 0, 0, 1, timer.X, timer.Y - TimerRadius + 0.6));
            Mic = mic;
        }

        public bool Behind(FaceHat hat) => hat switch
        {
            FaceHat.Bunny => !Ears.InFront,
            FaceHat.Mai => false,
            _ => _hatsBehind,
        };

        /// <summary>A hat's shape moved onto this set's head (made once per shape).</summary>
        public Geometry OnHead(Geometry shape)
        {
            if (!_onHead.TryGetValue(shape, out var placed)) _onHead[shape] = placed = Placed(shape, _head);
            return placed;
        }
    }

    static readonly Fit[] Fits = MakeFits();

    static Fit[] MakeFits()
    {
        // The mic's foot stays above y 70 (the window's bottom edge cuts lower down on small taskbars).
        var mic = (X: 99.0, Y: 67.5, Scale: 0.62, Tilt: -21.0);
        var fits = new Fit[Enum.GetValues<FaceStyle>().Length];
        // 1. Classic: no head is drawn, so hats rest just above the eyes and the brows tuck under the brim.
        fits[(int)FaceStyle.Classic] = new Fit((49, 11.5, 1.04, 1, 0), false, (49, 11, 7, 1.04, 13, 0, false), (31, 11, 1.2, 0, true),
            new EyesFit(new Point(49, 29), 0, 13, new Size(11, 13.5), new Size(11, 13.5)),
            (new Point(17, 30), new Point(81, 30), -4, false), new Point(4, 2.5), mic);
        // 2. ClassicGirl: the bow sits top left, so hats sit a little right of it, cocked, and the blossom goes right.
        fits[(int)FaceStyle.ClassicGirl] = new Fit((52, 11.5, 1, 1, 4), false, (52, 11, 7, 1, 13, 4, false), (68, 10, 1.2, 0, false),
            new EyesFit(new Point(49, 29), 0, 13, new Size(11.2, 13.2), new Size(11.2, 13.2)),
            (new Point(17, 30), new Point(81, 30), -4, false), new Point(1, 19), mic);
        // 3. AnimeGirl: the hair fills y -8 to 6, so hats come down over the fringe (its ends show at the
        // sides) and rabbit ears and the headphone band lie over the hair.
        fits[(int)FaceStyle.AnimeGirl] = new Fit((49, 12.5, 1.32, 1.07, 0), false, (49, 9, 9, 0.98, 16, 0, true), (28, 10, 1.1, -15, true),
            new EyesFit(new Point(49, 30), 0, 13, new Size(11.5, 15), new Size(11.5, 15)),
            (new Point(11, 27), new Point(87, 27), -6, true), new Point(3, 12), mic);
        // 4. AnimeBoy: as the girl; his brows sit low on the eyes, so hats can come down a little further.
        fits[(int)FaceStyle.AnimeBoy] = new Fit((49, 13.5, 1.32, 1.1, 0), false, (49, 9, 9, 0.98, 16, 0, true), (28, 11, 1.1, -15, true),
            new EyesFit(new Point(49, 29.8), 0, 13, new Size(12, 10.8), new Size(12, 10.8)),
            (new Point(11, 28), new Point(87, 28), -6, true), new Point(3, 12), mic);
        // 5. Alien: hats sit behind its features, above the third eye, so the antennae poke out of them.
        fits[(int)FaceStyle.Alien] = new Fit((49, 7.3, 0.86, 0.8, 0), true, (49, 7, 6, 0.82, 9, 0, false), (25, 11, 1.05, -10, true),
            new EyesFit(new Point(49, 29), 0, 14, new Size(12.4, 11.8), new Size(12.4, 11.8), Slant: 11),
            (new Point(14, 29), new Point(84, 29), -6, false), new Point(4, 2.5), mic);
        // 6. Robot: between the antenna rods; square panels get rounded-square frames and a visor.
        fits[(int)FaceStyle.Robot] = new Fit((49, 10.6, 1.12, 0.96, 0), false, (49, 10, 8, 0.98, 12, 0, false), (22, 10, 1.15, 0, false),
            new EyesFit(new Point(49, 29), 0, 13, new Size(11.8, 13.8), new Size(11.8, 13.8), Corner: 5, Visor: true),
            (new Point(13, 29), new Point(85, 29), -5, false), new Point(2, 3), mic);
        // 7. Cat: hats between the ears; the blossom is tucked by the left ear; the timer moves below that ear.
        fits[(int)FaceStyle.Cat] = new Fit((49, 11.5, 1, 1, 0), false, (49, 11, 7, 1, 12, 0, false), (24, 6, 1.1, -8, false),
            new EyesFit(new Point(49, 29.5), 0, 13, new Size(11.2, 11.6), new Size(11.2, 11.6)),
            (new Point(16, 30), new Point(82, 30), -3, false), new Point(1, 15), mic);
        // 8. Derp: everything a bit crooked: the hat cocked over the small eye, lenses as lopsided as the eyes.
        fits[(int)FaceStyle.Derp] = new Fit((50, 10.5, 1.05, 1, 10), false, (50, 10.5, 7, 1, 13, 10, false), (29, 10, 1.2, 0, true),
            new EyesFit(new Point(49.25, 29.5), 5.6, 15.3, new Size(11.6, 13.2), new Size(10.2, 10.2)),
            (new Point(12, 28), new Point(84, 32), -4, false), new Point(4, 2.5), mic);
        return fits;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A copy of a shape with a transform of its own: placed, but outlines drawn on it keep their width.</summary>
    static Geometry Placed(Geometry shape, Matrix m)
    {
        var copy = shape.CloneCurrentValue();
        var own = shape.Transform?.Value ?? Matrix.Identity;
        copy.Transform = new MatrixTransform(own * m);
        copy.Freeze();
        return copy;
    }

    static T Frozen<T>(this T freezable) where T : Freezable => Paints.Frozen(freezable);

    /// <summary>Circles as one shape.</summary>
    static Geometry Dots(params (Point Centre, double Radius)[] dots)
    {
        var group = new GeometryGroup();
        foreach (var (c, r) in dots) group.Children.Add(new EllipseGeometry(c, r, r));
        return group.Frozen();
    }

    /// <summary>Short upright strokes, for knitted ribs.</summary>
    static Geometry Ribs(double from, double to, double step, double top, double bottom) => FaceSet.Build(g =>
    {
        for (double x = from; x <= to + 1e-6; x += step)
        {
            g.BeginFigure(new Point(x, top), false, false);
            g.LineTo(new Point(x, bottom), true, false);
        }
    });

    static Geometry Star(Point c, double outer, double inner)
    {
        var points = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? outer : inner;
            points[i] = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
        }
        return FaceSet.Polyline(points, closed: true);
    }

    /// <summary>A five-petal blossom: round petals merged into one outline.</summary>
    static Geometry Blossom(Point c, double radius, double degrees)
    {
        Geometry petals = Geometry.Empty;
        for (int i = 0; i < 5; i++)
        {
            double a = (degrees - 90 + i * 72) * Math.PI / 180;
            var petal = new EllipseGeometry(new Point(c.X + Math.Cos(a) * radius * 0.5, c.Y + Math.Sin(a) * radius * 0.5), radius * 0.52, radius * 0.52);
            petals = Geometry.Combine(petals, petal, GeometryCombineMode.Union, null);
        }
        return petals.Frozen();
    }
}
