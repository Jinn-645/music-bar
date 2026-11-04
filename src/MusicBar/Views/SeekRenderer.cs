using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// Draws the playback progress across the whole taskbar in one of several
/// styles. Everything is drawn with DrawingContext (GPU), redrawn ~30 times a
/// second only while an animated style is playing, otherwise only when the
/// playhead moves a fraction of a pixel.
/// </summary>
public sealed class SeekRenderer : FrameworkElement
{
    const double FrameInterval = 1 / 30.0;
    const double ColorFade = 0.9;

    static readonly Color White = Colors.White;
    static readonly Color Black = Colors.Black;

    /// <summary>Hairline grain of holographic foil: a faint vertical line every 3 px.</summary>
    static readonly LinearGradientBrush Lenticular = Gradient(new Point(0, 0), new Point(3, 0),
        new[] { (Colors.White, 0.0, 0.0), (Colors.White, 0.05, 0.5), (Colors.White, 0.0, 1.0) }, GradientSpreadMethod.Repeat);

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly float[] _bands = new float[AudioMeter.BandCount];

    SeekStyle _style = SeekStyle.Aurora;
    SeekDirection _direction = SeekDirection.FromStart;
    double _progress;
    bool _timeline;
    bool _playing;
    bool _edgeAtTop = true;
    double _intensity = 0.34;

    Color _fromA = ColorUtil.Brand, _fromB = ColorUtil.Brand, _toA = ColorUtil.Brand, _toB = ColorUtil.Brand;
    double _colorStart = -10;

    Color[]? _strip;
    BitmapSource? _blurArt;

    float _level;
    bool _liveAudio;
    float[] _peaks = [];

    double[] _pseudoWave = [];
    float[] _recorded = [];
    int _waveSeed = 1;

    WriteableBitmap? _silk;
    float[] _silkLight = [];
    byte[] _silkPixels = [];
    // The render pass the threads were last spun in: the two halves of a split bar share one spin.
    int _renderPass, _silkPass = -1, _peaksPass = -1;

    readonly List<(double Left, double Right)> _quietZones = [];

    double _lastFrame = -1;
    double _lastPx = -1;
    bool _dirty = true;

    // The styles' own clock: the wall clock less the time it ran slow (or stood) while paused.
    const double IdleSpeed = 0.3;
    double _lastTick = -1, _pace = 1, _lag;

    public SeekRenderer()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        SizeChanged += (_, _) => _dirty = true;
        IsHitTestVisible = false;
    }

    /// <summary>Source of live audio; null makes the music-driven styles move on their own.</summary>
    public AudioMeter? Audio { get; set; }

    /// <summary>
    /// Paused, the animated styles keep drifting in place, slower and calmer, instead of standing
    /// still. The owner should keep ticking (at a lower rate is fine).
    /// </summary>
    public bool IdleMotion { get; set; } = true;

    public SeekStyle Look
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            if (value != SeekStyle.Silk)
            {
                _silk = null;
                _silkLight = [];
                _silkPixels = [];
            }
            _dirty = true;
        }
    }

    public SeekDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value) return;
            _direction = value;
            _dirty = true;
        }
    }

    bool Split => _direction is SeekDirection.FromCenter or SeekDirection.FromEnds;

    /// <summary>How far the playhead travels: the whole width, or half of it when the bar is split.</summary>
    double Run => Split ? ActualWidth / 2 : ActualWidth;

    public static bool UsesAudio(SeekStyle s) => s is not (SeekStyle.Classic or SeekStyle.Ribbon or SeekStyle.Cinema);

    static bool IsAnimated(SeekStyle s) => s != SeekStyle.Classic;

    /// <summary>Slow, drifting styles look the same at 20 fps and cost a third less.</summary>
    static double FrameIntervalFor(SeekStyle s) =>
        s is SeekStyle.Cinema or SeekStyle.Ribbon or SeekStyle.Mesh or SeekStyle.Ink ? 1 / 20.0 : FrameInterval;

    public void SetState(double progress, bool timeline, bool playing)
    {
        _progress = Math.Clamp(progress, 0, 1);
        if (timeline != _timeline || playing != _playing) _dirty = true;
        _timeline = timeline;
        _playing = playing;
    }

    public void SetEdge(bool edgeAtTop)
    {
        if (_edgeAtTop == edgeAtTop) return;
        _edgeAtTop = edgeAtTop;
        _dirty = true;
    }

    public double Intensity
    {
        get => _intensity;
        set
        {
            _intensity = Math.Clamp(value, 0.05, 1);
            _dirty = true;
        }
    }

    public void SetColors(Color primary, Color secondary, bool animate)
    {
        double now = Now;
        var (a, b) = CurrentColors(now);
        _fromA = animate ? a : primary;
        _fromB = animate ? b : secondary;
        _toA = primary;
        _toB = secondary;
        _colorStart = animate ? now : -10;
        _dirty = true;
    }

    /// <summary>Album art for the Ribbon and Cinema styles.</summary>
    public void SetArt(BitmapSource? art)
    {
        _strip = null;
        _blurArt = null;
        _dirty = true;
        if (art == null) return;
        try
        {
            _strip = BuildStrip(art, 28);
            _blurArt = BuildBlur(art, 40, 10);
        }
        catch (Exception ex)
        {
            Log.Error("Preparing art for the seekbar failed", ex);
        }
    }

    /// <summary>
    /// Horizontal ranges (in this element's units) where text sits on top of the
    /// taskbar; the progress visuals fade almost away there so the text stays readable.
    /// </summary>
    public void SetQuietZones(IReadOnlyList<(double Left, double Right)> zones)
    {
        bool same = zones.Count == _quietZones.Count;
        for (int i = 0; same && i < zones.Count; i++)
            same = Math.Abs(zones[i].Left - _quietZones[i].Left) < 1 && Math.Abs(zones[i].Right - _quietZones[i].Right) < 1;
        if (same) return;
        _quietZones.Clear();
        _quietZones.AddRange(zones);
        _dirty = true;
    }

    LinearGradientBrush QuietMask(double width)
    {
        // The zone hugs the text now, so the seekbar can stay a little visible under it.
        const double feather = 28, quiet = 0.3;
        var stops = new GradientStopCollection { new(Colors.Black, 0), new(Colors.Black, 1) };
        foreach (var (left, right) in _quietZones)
        {
            stops.Add(new GradientStop(Colors.Black, (left - feather) / width));
            stops.Add(new GradientStop(ColorUtil.WithAlpha(Colors.Black, quiet), left / width));
            stops.Add(new GradientStop(ColorUtil.WithAlpha(Colors.Black, quiet), right / width));
            stops.Add(new GradientStop(Colors.Black, (right + feather) / width));
        }
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(width, 0)) { MappingMode = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    /// <summary>A new song: fresh pseudo-waveform, nothing recorded yet.</summary>
    public void SetTrack(string key)
    {
        _waveSeed = key.GetHashCode();
        _pseudoWave = [];
        _recorded = [];
        _dirty = true;
    }

    /// <summary>Overrides the wall clock, so frames can be rendered at exact times (video capture).</summary>
    public Func<double>? Clock { get; set; }

    double Now => Clock?.Invoke() ?? _clock.Elapsed.TotalSeconds;

    (Color A, Color B) CurrentColors(double now)
    {
        double k = Math.Clamp((now - _colorStart) / ColorFade, 0, 1);
        k = k * k * (3 - 2 * k);
        return (ColorUtil.Lerp(_fromA, _toA, k), ColorUtil.Lerp(_fromB, _toB, k));
    }

    /// <summary>Called every frame by the owner; decides whether a redraw is due.</summary>
    public void Tick()
    {
        double now = Now;
        double dt = _lastTick < 0 ? 0 : Math.Clamp(now - _lastTick, 0, 0.5);
        _lastTick = now;
        // Playing, the styles move with the wall clock; paused, they ease down to a slow drift (or
        // to a stop without idle motion) over about half a second, and back up when it plays again.
        double pace = _playing ? 1 : IdleMotion && IsAnimated(_style) ? IdleSpeed : 0;
        _pace += (pace - _pace) * (1 - Math.Exp(-dt * 5));
        _lag += dt * (1 - _pace);

        var audio = Audio;
        _liveAudio = audio is { IsCapturing: true };
        if (_liveAudio)
        {
            audio!.CopyBands(_bands);
            _level = audio.Level;
        }
        else if (_playing)
        {
            Synthesize(now);
        }
        else
        {
            Breathe(now - _lag, dt);
        }

        if (_style == SeekStyle.Waveform) RecordWaveform();

        bool fading = now - _colorStart < ColorFade;
        bool animating = (IsAnimated(_style) && (_playing || IdleMotion || _pace > 0.01)) || fading;
        double px = _progress * Run;
        if (animating)
        {
            if (now - _lastFrame >= FrameIntervalFor(_style)) InvalidateVisual();
        }
        else if (_dirty || Math.Abs(px - _lastPx) >= 1)
        {
            // Static styles: one redraw per pixel of progress is plenty (a few per second).
            InvalidateVisual();
        }
    }

    /// <summary>Music-like motion for when no live audio is available (previews, capture off).</summary>
    void Synthesize(double t)
    {
        double beat = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.9)), 6);
        for (int b = 0; b < _bands.Length; b++)
        {
            double tilt = 1 - b / (double)_bands.Length * 0.55;
            double v = 0.32 + 0.28 * Noise(t * 2.3 + b * 0.37) + 0.22 * Math.Sin(t * 3.1 + b * 0.5) * Noise(t * 0.8 + b) + beat * 0.25 * tilt;
            _bands[b] = (float)Math.Clamp(v * tilt + 0.05, 0, 1);
        }
        _level = (float)(0.45 + 0.25 * Noise(t * 1.7) + beat * 0.3);
    }

    /// <summary>
    /// Paused: the music-driven styles settle from wherever the music left them to a low, slow
    /// breath, so they idle in place instead of dancing to nothing.
    /// </summary>
    void Breathe(double t, double dt)
    {
        double k = 1 - Math.Exp(-dt * 2.5);
        for (int b = 0; b < _bands.Length; b++)
        {
            double calm = 0.1 + 0.07 * Noise(t * 0.45 + b * 0.37) + 0.03 * Math.Sin(t * 0.9 + b * 0.5);
            _bands[b] += (float)((calm - _bands[b]) * k);
        }
        double level = 0.16 + 0.05 * Math.Sin(t * 0.8) + 0.04 * Noise(t * 0.5);
        _level += (float)((level - _level) * k);
    }

    // ------------------------------------------------------------------ rendering

    protected override void OnRender(DrawingContext dc)
    {
        double now = Now;
        _lastFrame = now;
        _dirty = false;
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;
        _renderPass++;

        double run = Run;
        double x = _timeline ? _progress * run : 0;
        _lastPx = x;
        var (a, b) = CurrentColors(now);
        var c = ColorUtil.ShiftHue(ColorUtil.Lerp(a, b, 0.5), 48, 0.04);
        double t = now - _lag;
        var f = new Frame(dc, run, h, x, a, b, c, _intensity, _edgeAtTop, t, _level);

        // Quiet zones are in this element's units, so the mask goes on before any mirroring.
        bool quiet = _quietZones.Count > 0;
        if (quiet) dc.PushOpacityMask(QuietMask(w));
        // Every style draws from x = 0 rightwards; the other directions are that drawing
        // mirrored, or two half-width copies of it back to back.
        switch (_direction)
        {
            case SeekDirection.FromStart:
                DrawStyle(f);
                break;
            case SeekDirection.FromEnd:
                DrawStyle(f, new Matrix(-1, 0, 0, 1, w, 0));
                break;
            case SeekDirection.FromCenter:
                DrawStyle(f, new Matrix(1, 0, 0, 1, run, 0));
                DrawStyle(f, new Matrix(-1, 0, 0, 1, run, 0));
                break;
            case SeekDirection.FromEnds:
                DrawStyle(f, Matrix.Identity);
                DrawStyle(f, new Matrix(-1, 0, 0, 1, w, 0));
                break;
        }
        if (quiet) dc.Pop();
    }

    /// <summary>One copy of the style, placed by <paramref name="place"/> and kept inside its own run.</summary>
    void DrawStyle(Frame f, Matrix place)
    {
        f.Dc.PushTransform(new MatrixTransform(place));
        f.Dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.W, f.H)));
        DrawStyle(f);
        f.Dc.Pop();
        f.Dc.Pop();
    }

    void DrawStyle(Frame f)
    {
        switch (_style)
        {
            case SeekStyle.Classic: DrawClassic(f); break;
            case SeekStyle.Aurora: DrawAurora(f); break;
            case SeekStyle.Liquid: DrawLiquid(f); break;
            case SeekStyle.Neon: DrawNeon(f); break;
            case SeekStyle.Spectrum: DrawSpectrum(f); break;
            case SeekStyle.Waveform: DrawWaveform(f); break;
            case SeekStyle.Ribbon: DrawRibbon(f); break;
            case SeekStyle.Cinema: DrawCinema(f); break;
            case SeekStyle.Mosaic: DrawMosaic(f); break;
            case SeekStyle.Mesh: DrawMesh(f); break;
            case SeekStyle.Silk: DrawSilk(f); break;
            case SeekStyle.Holo: DrawHolo(f); break;
            case SeekStyle.Bokeh: DrawBokeh(f); break;
            case SeekStyle.Ink: DrawInk(f); break;
        }
    }

    readonly record struct Frame(
        DrawingContext Dc, double W, double H, double X, Color A, Color B, Color C,
        double F, bool Top, double T, double Level)
    {
        /// <summary>The taskbar edge that faces the desktop.</summary>
        public double EdgeY => Top ? 0 : H;
        public double Inward => Top ? 1 : -1;
    }

    // 1. Classic: tinted fill, soft glow at the head, thin line along the edge.
    static void DrawClassic(Frame f)
    {
        var dc = f.Dc;
        if (f.X > 0.5)
        {
            dc.DrawRectangle(Horizontal(0, f.X, (f.B, f.F * 0.12, 0), (ColorUtil.Lerp(f.B, f.A, 0.6), f.F * 0.55, 0.72), (f.A, f.F, 1)),
                null, new Rect(0, 0, f.X, f.H));
            dc.DrawRectangle(Radial(f.X, f.H / 2, f.H * 2.3, f.H, (ColorUtil.Lerp(f.A, White, 0.15), Math.Min(0.85, 0.3 + f.F), 0), (f.A, 0, 1)),
                null, new Rect(f.X - f.H * 2.3, -f.H, f.H * 4.6, f.H * 3));
            dc.DrawRectangle(Vertical(f.EdgeY, f.EdgeY + f.Inward * f.H, (f.A, 1, 0), (f.A, 0.33, 0.22), (f.A, 0, 0.5)),
                null, new Rect(Math.Round(f.X) - 1, 0, 2, f.H));
        }
        double ey = f.Top ? 0 : f.H - 2;
        dc.DrawRectangle(Solid(White, 0.09), null, new Rect(0, ey, f.W, 2));
        if (f.X > 0) dc.DrawRectangle(Solid(f.A, 0.95), null, new Rect(0, ey, f.X, 2));
    }

    // 2. Aurora: three translucent ribbons drifting like northern lights.
    static void DrawAurora(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double fadeEnd = Math.Max(0, (f.X - 18) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.25, 0), (Black, 1, 0.35), (Black, 1, fadeEnd), (Black, 0.3, 1)));

        var colors = new[] { f.B, f.A, f.C };
        double amp = f.H * 0.2 * (1 + f.Level * 0.9);
        for (int i = 0; i < 3; i++)
        {
            double k = 2 * Math.PI / (f.W * (0.16 + 0.07 * i));
            double speed = 0.35 + 0.17 * i;
            double phase = i * 1.9;
            double thickness = f.H * (0.42 + 0.08 * i);
            var band = Vertical(0, f.H, (colors[i], 0, 0), (colors[i], 1, 0.5), (colors[i], 0, 1));
            for (int layer = 0; layer < 2; layer++)
            {
                double widen = layer == 0 ? 1.7 : 0.75;
                double alpha = layer == 0 ? f.F * 0.45 : f.F * 0.85;
                var geo = Ribbon(0, f.X + 8, 6,
                    xp => f.H * 0.5 + amp * (0.8 + 0.25 * i) * Math.Sin(k * xp + f.T * speed + phase)
                          + amp * 0.35 * Math.Sin(k * 2.3 * xp - f.T * speed * 1.4 + phase * 2),
                    xp => thickness * widen * (0.75 + 0.35 * Math.Sin(k * 0.6 * xp - f.T * speed * 0.7 + i)));
                dc.PushOpacity(Math.Min(1, alpha));
                dc.DrawGeometry(band, null, geo);
                dc.Pop();
            }
        }
        dc.Pop();
        dc.Pop();

        dc.DrawRectangle(Horizontal(f.X - 46, f.X, (f.A, 0, 0), (ColorUtil.Lerp(f.A, White, 0.4), 0.2 + f.F * 0.3, 1)),
            null, new Rect(Math.Max(0, f.X - 46), 0, Math.Min(46, f.X), f.H));
        dc.DrawRectangle(Vertical(0, f.H, (White, 0, 0), (White, 0.75, 0.5), (White, 0, 1)), null, new Rect(f.X - 0.75, 0, 1.5, f.H));
    }

    // 3. Liquid: wobbling meniscus at the head, bubbles rising through the fill.
    static void DrawLiquid(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        // One long swell plus a gentle ripple travelling along the surface: a smooth meniscus, not a zigzag.
        double amp = 2.4 * (1 + f.Level * 1.4);
        double Edge(double y) => f.X + amp * Math.Sin(y * 0.16 + f.T * 2.4) + amp * 0.3 * Math.Sin(y * 0.34 - f.T * 1.6)
                                 + amp * 0.35 * Math.Sin(f.T * 1.1);

        var body = new StreamGeometry();
        var surface = new StreamGeometry();
        using (var g = body.Open())
        using (var s = surface.Open())
        {
            g.BeginFigure(new Point(0, 0), true, true);
            s.BeginFigure(new Point(Edge(0), 0), false, false);
            for (double y = 0; y <= f.H + 0.01; y += 1.5)
            {
                var p = new Point(Edge(y), y);
                g.LineTo(p, false, false);
                s.LineTo(p, true, true);
            }
            g.LineTo(new Point(0, f.H), false, false);
        }
        body.Freeze();
        surface.Freeze();

        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.35, 0), (Black, 1, 0.3), (Black, 1, 1)));
        dc.DrawGeometry(Vertical(0, f.H, (ColorUtil.Lerp(f.A, White, 0.18), f.F * 1.15, 0), (f.A, f.F, 0.45), (f.B, f.F * 0.95, 1)), null, body);

        // Light caustics drifting inside the liquid.
        for (int i = 0; i < 2; i++)
        {
            double cx = (f.T * (18 + i * 11) + i * 300) % (f.X + 120) - 60;
            dc.DrawRectangle(Horizontal(cx - 50, cx + 50, (White, 0, 0), (White, 0.07, 0.5), (White, 0, 1)), null,
                new Rect(Math.Max(0, cx - 50), 0, Math.Max(0, Math.Min(100, f.X - cx + 50)), f.H));
        }

        for (int j = 0; j < 16; j++)
        {
            double speed = 5 + Hash(j * 3 + 2) * 9;
            double bx = Hash(j * 7 + 1) * Math.Max(0, f.X - 10) + 3 + Math.Sin(f.T * 1.7 + j) * 1.5;
            double by = f.H + 2 - ((f.T * speed / f.H + Hash(j * 11 + 5)) % 1.0) * (f.H + 4);
            double r = 0.7 + Hash(j * 5 + 9) * 1.3;
            if (bx > Edge(by) - 3) continue;
            dc.DrawEllipse(null, new Pen(Solid(White, 0.38), 0.6), new Point(bx, by), r, r);
            dc.DrawEllipse(Solid(White, 0.5), null, new Point(bx - r * 0.35, by - r * 0.35), r * 0.25, r * 0.25);
        }
        dc.Pop();

        dc.DrawGeometry(null, MakePen(ColorUtil.Lerp(f.A, White, 0.5), 0.25, 4), surface);
        dc.DrawGeometry(null, MakePen(White, 0.55, 1.1), surface);
    }

    // 4. Neon: a thin neon tube along the edge that hums and spills light.
    static void DrawNeon(Frame f)
    {
        var dc = f.Dc;
        double y = f.Top ? 3.5 : f.H - 3.5;
        double flicker = 0.9 + 0.1 * Noise(f.T * 9);
        if (Noise(f.T * 0.5 + 3) > 0.9) flicker *= 0.55 + 0.45 * Noise(f.T * 40);
        double glow = Math.Clamp(0.55 + f.F, 0.6, 1.2) * flicker * (0.9 + f.Level * 0.35);

        dc.DrawLine(MakePen(White, 0.08, 3), new Point(6, y), new Point(f.W - 6, y));
        if (f.X <= 6) return;

        var head = new Point(f.X, y);
        dc.DrawRectangle(Vertical(y, y + f.Inward * f.H * 0.95, (f.A, 0.42 * f.F * glow, 0), (f.A, 0, 1)), null, new Rect(0, 0, f.X, f.H));
        dc.DrawLine(MakePen(f.A, 0.07 * glow, 14), new Point(6, y), head);
        dc.DrawLine(MakePen(f.A, 0.16 * glow, 7), new Point(6, y), head);
        dc.DrawLine(MakePen(f.A, 0.45 * glow, 3.2), new Point(6, y), head);
        dc.DrawLine(MakePen(ColorUtil.Lerp(f.A, White, 0.65), 0.95, 1.4), new Point(6, y), head);

        dc.DrawEllipse(Radial(f.X, y, 12, 12, (White, 0.9 * flicker, 0), (f.A, 0.5, 0.3), (f.A, 0, 1)), null, head, 12, 12);
        for (int i = 0; i < 4; i++)
        {
            double sx = f.X + (Noise(f.T * 13 + i * 7) - 0.5) * 12;
            double sy = y + (Noise(f.T * 11 + i * 3) - 0.5) * 8;
            dc.DrawEllipse(Solid(White, 0.6 * Noise(f.T * 17 + i)), null, new Point(sx, sy), 0.7, 0.7);
        }
    }

    // 5. Spectrum: live frequency bars standing on the screen edge.
    void DrawSpectrum(Frame f)
    {
        var dc = f.Dc;
        const double barWidth = 4, gap = 2.2;
        int n = (int)((f.W + gap) / (barWidth + gap));
        if (n < 2) return;
        if (_peaks.Length != n) _peaks = new float[n];
        // The second half of a split bar draws the same peaks; they fall once per frame.
        bool fall = _peaksPass != _renderPass;
        _peaksPass = _renderPass;
        double offset = (f.W - (n * (barWidth + gap) - gap)) / 2;
        double baseY = f.Top ? f.H : 0;
        double maxH = f.H * 0.86;

        var played = Vertical(baseY, baseY - (f.Top ? 1 : -1) * maxH,
            (f.B, 0.5 + f.F * 0.5, 0), (f.A, 0.6 + f.F * 0.5, 0.55), (ColorUtil.Lerp(f.A, White, 0.35), 0.75 + f.F * 0.3, 1));
        var rest = Solid(White, 0.1);
        var peakPlayed = Solid(White, 0.65);
        var peakRest = Solid(White, 0.14);

        for (int i = 0; i < n; i++)
        {
            double v = SampleBand(i / (double)(n - 1));
            if (fall) _peaks[i] = (float)Math.Max(v, _peaks[i] - (_playing || IdleMotion ? 0.012 : 0));
            double bh = Math.Max(1.5, maxH * v);
            double bx = offset + i * (barWidth + gap);
            bool isPlayed = bx + barWidth / 2 <= f.X;
            var rect = f.Top ? new Rect(bx, f.H - bh, barWidth, bh) : new Rect(bx, 0, barWidth, bh);
            dc.DrawRoundedRectangle(isPlayed ? played : rest, null, rect, 1.6, 1.6);

            double ph = Math.Max(2, maxH * _peaks[i]) + 2;
            var cap = f.Top ? new Rect(bx, f.H - ph - 1.5, barWidth, 1.5) : new Rect(bx, ph, barWidth, 1.5);
            dc.DrawRectangle(isPlayed ? peakPlayed : peakRest, null, cap);
        }
    }

    double SampleBand(double frac)
    {
        double pos = Math.Clamp(frac, 0, 1) * (_bands.Length - 1);
        int i = (int)pos;
        double k = pos - i;
        double v = i + 1 < _bands.Length ? _bands[i] * (1 - k) + _bands[i + 1] * k : _bands[i];
        return Math.Clamp(v, 0, 1);
    }

    // 6. Waveform: a SoundCloud-style waveform that records the real loudness as it plays.
    void DrawWaveform(Frame f)
    {
        var dc = f.Dc;
        const double barWidth = 2, gap = 1.3;
        int n = WaveBars(f.W);
        if (n < 2) return;
        EnsureWave(n);
        double offset = (f.W - (n * (barWidth + gap) - gap)) / 2;
        double mid = f.H / 2, maxH = f.H * 0.8;

        var played = Horizontal(0, Math.Max(1, f.X), (f.B, 0.55 + f.F * 0.45, 0), (f.A, 0.75 + f.F * 0.25, 1));
        var rest = Solid(White, 0.16);
        var recordedRest = Solid(White, 0.24);
        var head = Solid(White, 0.95);

        for (int i = 0; i < n; i++)
        {
            bool recorded = _recorded[i] > 0;
            double amp = recorded ? _recorded[i] : _pseudoWave[i];
            double bh = Math.Max(1.5, maxH * amp);
            double bx = offset + i * (barWidth + gap);
            Brush brush = Math.Abs(bx - f.X) < 2.5 && f.X > 0 ? head
                : bx < f.X ? played
                : recorded ? recordedRest : rest;
            dc.DrawRoundedRectangle(brush, null, new Rect(bx, mid - bh / 2, barWidth, bh), 1, 1);
        }
    }

    static int WaveBars(double width) => (int)((width + 1.3) / 3.3);

    void EnsureWave(int n)
    {
        if (_pseudoWave.Length == n) return;
        _pseudoWave = new double[n];
        _recorded = new float[n];
        var rng = new Random(_waveSeed);
        double seedOffset = rng.NextDouble() * 1000;
        for (int i = 0; i < n; i++)
        {
            double u = i / (double)(n - 1);
            double env = 0.35 + 0.25 * Smooth(Math.Clamp((u - 0.05) / 0.08, 0, 1));
            env += 0.25 * Bump(u, 0.3, 0.09) + 0.3 * Bump(u, 0.62, 0.1) + 0.25 * Bump(u, 0.85, 0.07);
            env *= 1 - 0.6 * Smooth(Math.Clamp((u - 0.94) / 0.06, 0, 1));
            double detail = 0.55 + 0.45 * Noise(i * 0.08 + seedOffset);
            double jitter = 0.82 + 0.3 * rng.NextDouble();
            _pseudoWave[i] = Math.Clamp(env * detail * jitter, 0.08, 1);
        }
    }

    void RecordWaveform()
    {
        if (!_playing || !_timeline || !_liveAudio || _recorded.Length == 0) return;
        int i = Math.Clamp((int)(_progress * _recorded.Length), 0, _recorded.Length - 1);
        float v = Math.Clamp(_level * 1.05f + 0.04f, 0.06f, 1f);
        _recorded[i] = Math.Max(_recorded[i], v);
        if (i > 0 && _recorded[i - 1] == 0) _recorded[i - 1] = v;
    }

    // 7. Ribbon: the cover's colours, column by column, revealed as the song plays.
    void DrawRibbon(Frame f)
    {
        var dc = f.Dc;
        Color[] strip = _strip ?? new[] { f.B, f.A, f.C };
        var stops = new GradientStopCollection();
        for (int i = 0; i < strip.Length; i++)
            stops.Add(new GradientStop(ColorUtil.WithAlpha(strip[i], 1), i / (double)(strip.Length - 1)));
        var full = new LinearGradientBrush(stops, new Point(0, 0), new Point(f.W, 0)) { MappingMode = BrushMappingMode.Absolute };
        full.Freeze();

        dc.PushOpacity(0.06);
        dc.DrawRectangle(full, null, new Rect(f.X, 0, Math.Max(0, f.W - f.X), f.H));
        dc.Pop();
        if (f.X < 1) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        dc.PushOpacity(Math.Min(1, 0.3 + f.F * 0.8));
        dc.DrawRectangle(full, null, new Rect(0, 0, f.W, f.H));
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.12, 0), (White, 0, 0.45), (Black, 0, 0.55), (Black, 0.16, 1)), null, new Rect(0, 0, f.X, f.H));
        double s = (f.T % 6) / 6;
        double sx = -160 + s * (f.X + 320);
        dc.DrawRectangle(Horizontal(sx - 80, sx + 80, (White, 0, 0), (White, 0.13, 0.5), (White, 0, 1)), null, new Rect(sx - 80, 0, 160, f.H));
        dc.Pop();

        var headColor = strip[Math.Clamp((int)(f.X / f.W * (strip.Length - 1)), 0, strip.Length - 1)];
        dc.DrawRectangle(Radial(f.X, f.H / 2, f.H * 1.4, f.H, (ColorUtil.Lerp(headColor, White, 0.5), 0.45, 0), (headColor, 0, 1)), null,
            new Rect(f.X - f.H * 1.4, 0, f.H * 2.8, f.H));
        dc.DrawRectangle(Solid(ColorUtil.Lerp(headColor, White, 0.6), 0.9), null, new Rect(f.X - 0.75, 0, 1.5, f.H));
    }

    // 8. Cinema: the cover, deeply blurred, drifting slowly behind the taskbar.
    void DrawCinema(Frame f)
    {
        if (_blurArt == null)
        {
            DrawRibbon(f);
            return;
        }
        var dc = f.Dc;
        double drift = Math.Sin(f.T * 0.06) * f.W * 0.03;
        var dest = new Rect(-f.W * 0.04 + drift, -f.H * 0.2, f.W * 1.08, f.H * 1.4);

        if (f.X < f.W)
        {
            dc.PushClip(new RectangleGeometry(new Rect(f.X, 0, f.W - f.X, f.H)));
            dc.PushOpacity(0.07);
            dc.DrawImage(_blurArt, dest);
            dc.Pop();
            dc.Pop();
        }
        if (f.X < 1) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double soft = Math.Max(0, (f.X - 30) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 1, 0), (Black, 1, soft), (Black, 0.35, 1)));
        dc.PushOpacity(Math.Min(1, 0.35 + f.F * 0.9));
        dc.DrawImage(_blurArt, dest);
        dc.Pop();
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (Black, 0.3, 0), (Black, 0, 0.35), (Black, 0, 0.65), (Black, 0.3, 1)), null, new Rect(0, 0, f.X, f.H));
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0, 0), (White, 0.8, 0.5), (White, 0, 1)), null, new Rect(f.X - 0.5, 0, 1, f.H));
    }

    // 9. Mosaic: an LED matrix that twinkles in the song's palette.
    static void DrawMosaic(Frame f)
    {
        var dc = f.Dc;
        int rows = f.H >= 40 ? 4 : 3;
        const double gap = 2;
        double size = (f.H - gap * (rows + 1)) / rows;
        if (size < 2) return;
        double step = size + gap;
        int cols = (int)((f.W - gap) / step);
        double offset = (f.W - (cols * step - gap)) / 2;
        double headCol = (f.X - offset) / step;
        var dim = Solid(White, 0.05);
        int lit = (int)Math.Ceiling(f.Level * rows);

        for (int col = 0; col < cols; col++)
        {
            double cx = offset + col * step;
            bool played = cx + size <= f.X;
            bool partial = !played && cx < f.X;
            double near = Math.Max(0, 1 - (headCol - col) / 7);
            var tint = ColorUtil.Lerp(ColorUtil.Lerp(f.B, f.A, col / (double)cols), White, near * 0.45);
            for (int row = 0; row < rows; row++)
            {
                double cy = gap + row * step;
                var rect = new Rect(cx, cy, size, size);
                if (played)
                {
                    double twinkle = 0.5 + 0.5 * Math.Sin(f.T * (0.7 + Hash(col * 31 + row) * 1.8) + Hash(col * 7 + row * 13) * 6.283);
                    double alpha = (0.28 + f.F * 0.9) * (0.55 + 0.45 * twinkle) + near * 0.25;
                    // Near the head the column doubles as a level meter.
                    if (near > 0 && rows - row > lit) alpha *= 0.55;
                    dc.DrawRoundedRectangle(Solid(tint, Math.Min(1, alpha)), null, rect, 1.6, 1.6);
                }
                else if (partial)
                {
                    dc.DrawRoundedRectangle(Solid(White, 0.15 + 0.6 * (f.X - cx) / size), null, rect, 1.6, 1.6);
                }
                else
                {
                    dc.DrawRoundedRectangle(dim, null, rect, 1.6, 1.6);
                }
            }
        }
    }

    // 10. Mesh: big soft pools of the cover's colours drifting into one another, breathing with the music.
    void DrawMesh(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        Color[] palette = _strip is { Length: > 0 } strip
            ? new[] { f.A, Vivid(strip[strip.Length / 6]), f.C, ColorUtil.ShiftHue(f.A, -35, 0.06), f.B, Vivid(strip[strip.Length * 2 / 3]) }
            : new[] { f.A, ColorUtil.ShiftHue(f.A, -35, 0.06), f.C, f.B, ColorUtil.ShiftHue(f.B, 30, 0.08), ColorUtil.Lerp(f.A, White, 0.35) };

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double soft = Math.Max(0, (f.X - 60) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.3, 0), (Black, 1, 0.25), (Black, 1, soft), (Black, 0.2, 1)));
        dc.PushOpacity(Math.Min(1, 0.3 + f.F * 0.85));
        dc.DrawRectangle(Horizontal(0, f.X, (f.B, 0.3, 0), (f.A, 0.4, 1)), null, new Rect(0, 0, f.X, f.H));

        int n = Math.Max(6, (int)(f.W / 140));
        double spacing = f.W / n;
        for (int j = 0; j < n; j++)
        {
            double cx = (j + 0.5) * spacing + (Noise(f.T * 0.09 + j * 3.7) - 0.5) * spacing * 1.6;
            double swell = 1 + 0.3 * f.Level * SampleBand(Hash(j * 5 + 2));
            double rx = spacing * (0.9 + 0.7 * Hash(j * 3 + 1)) * swell;
            if (cx - rx > f.X) continue;
            // Shorter than the bar, wandering up and down, so the colours also shift vertically.
            double ry = f.H * (0.5 + 0.35 * Hash(j * 7 + 4)) * swell;
            double cy = f.H * (0.5 + (Noise(f.T * 0.13 + j * 5.3) - 0.5) * 1.2);
            var c = palette[j % palette.Length];
            dc.DrawEllipse(Radial(cx, cy, rx, ry, (c, 0.95, 0), (c, 0.55, 0.45), (c, 0, 1)), null, new Point(cx, cy), rx, ry);
        }
        dc.Pop();
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.1, 0), (White, 0, 0.35), (Black, 0, 0.7), (Black, 0.14, 1)), null, new Rect(0, 0, f.X, f.H));
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0, 0), (White, 0.55, 0.5), (White, 0, 1)), null, new Rect(f.X - 0.5, 0, 1, f.H));
    }

    // 11. Silk: fine threads twisting around each other, spun into a single strand at the playhead.
    void DrawSilk(Frame f)
    {
        var dc = f.Dc;
        double mid = f.H / 2;
        if (f.X < f.W)
            dc.DrawRectangle(Horizontal(f.X, f.W, (White, 0.24, 0), (White, 0.09, 0.25), (White, 0.09, 1)), null, new Rect(f.X, mid - 0.5, f.W - f.X, 1));
        if (f.X < 2) return;

        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.1, 0), (Black, 1, 0.3), (Black, 1, 1)));
        dc.DrawRectangle(Vertical(0, f.H, (f.A, 0, 0.1), (f.A, f.F * 0.2, 0.5), (f.A, 0, 0.9)), null, new Rect(0, 0, f.X, f.H));
        var threads = SpinSilk(f, out double scale);
        dc.DrawImage(threads, new Rect(0, 0, threads.PixelWidth / scale, threads.PixelHeight / scale));
        dc.Pop();

        dc.DrawEllipse(Radial(f.X, mid, 16, 11, (White, 0.9, 0), (f.A, 0.45, 0.3), (f.A, 0, 1)), null, new Point(f.X, mid), 16, 11);
        dc.DrawRectangle(Horizontal(f.X - 26, f.X + 26, (White, 0, 0), (White, 0.75, 0.5), (White, 0, 1)), null, new Rect(f.X - 26, mid - 0.5, 52, 1));
    }

    /// <summary>
    /// Rasterises the silk threads by hand, one pixel column at a time. As WPF strokes the same
    /// curves cost several times the CPU (strokes are re-tessellated every frame); here light
    /// also adds up where threads cross, which is what makes them read as silk.
    /// </summary>
    WriteableBitmap SpinSilk(Frame f, out double scale)
    {
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int pw = Math.Max(1, (int)Math.Ceiling(f.W * scale)), ph = Math.Max(1, (int)Math.Ceiling(f.H * scale));
        if (_silk == null || _silk.PixelWidth != pw || _silk.PixelHeight != ph)
        {
            _silk = new WriteableBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
            _silkLight = new float[pw * ph * 4];
            _silkPixels = new byte[pw * ph * 4];
        }
        else if (_silkPass == _renderPass)
        {
            return _silk;
        }
        _silkPass = _renderPass;
        var light = _silkLight;
        Array.Clear(light);

        const int threads = 14;
        double s = scale, mid = f.H / 2;
        double gather = Math.Min(260, f.X * 0.7);
        double amp = f.H * 0.38 * (0.85 + f.Level * 0.45);
        double core = s, halo = 1.6 * s;
        float strength = (float)Math.Min(1, 0.35 + f.F * 0.55);
        int last = Math.Min(pw - 1, (int)(f.X * s));
        for (int i = 0; i < threads; i++)
        {
            double u = i / (double)(threads - 1);
            var color = ColorUtil.Lerp(u < 0.5 ? ColorUtil.Lerp(f.B, f.A, u * 2) : ColorUtil.Lerp(f.A, f.C, u * 2 - 1), White, 0.2);
            float cr = color.R / 255f, cg = color.G / 255f, cb = color.B / 255f;
            double k = 2 * Math.PI / (f.W * (0.1 + 0.18 * Hash(i * 3 + 1)) + 50);
            double speed = 0.45 + 0.7 * Hash(i * 7 + 2);
            double phase = Hash(i * 13 + 5) * 2 * Math.PI;
            double a = amp * (0.45 + 0.55 * Hash(i * 5 + 3));
            double Y(double x) => s * (mid + a * Smooth(Math.Clamp((f.X - x) / gather, 0, 1))
                * (0.8 * Math.Sin(k * x - f.T * speed + phase) + 0.25 * Math.Sin(2.7 * k * x + f.T * speed * 0.6 + phase * 1.7)));

            double y0 = Y(0);
            for (int px = 0; px <= last; px++)
            {
                double y1 = Y((px + 1) / s);
                // The segment through this column, as a vertical span: full coverage inside the core,
                // fading out over a short halo either side.
                double lo = Math.Min(y0, y1) - core / 2, hi = Math.Max(y0, y1) + core / 2;
                int r0 = Math.Max(0, (int)(lo - halo)), r1 = Math.Min(ph - 1, (int)(hi + halo));
                for (int row = r0; row <= r1; row++)
                {
                    double cover = Math.Clamp(Math.Min(hi, row + 1) - Math.Max(lo, row), 0, 1);
                    double centre = row + 0.5;
                    double gap = centre < lo ? lo - centre : centre > hi ? centre - hi : 0;
                    double glow = Math.Max(0, 1 - gap / halo) * 0.16;
                    float w = (float)Math.Min(1, cover + glow) * strength;
                    if (w <= 0) continue;
                    int p = (row * pw + px) * 4;
                    light[p] += cb * w;
                    light[p + 1] += cg * w;
                    light[p + 2] += cr * w;
                    light[p + 3] += w;
                }
                y0 = y1;
            }
        }

        // Soft tone mapping: crossings grow denser in the threads' own colours instead of clipping to
        // white. Scaling every channel by the same factor keeps the premultiplied pixel valid.
        var pixels = _silkPixels;
        for (int p = 0; p < light.Length; p += 4)
        {
            float sum = light[p + 3];
            float factor = sum > 0 ? (1 - MathF.Exp(-1.3f * sum)) / sum * 255 : 0;
            pixels[p] = (byte)(light[p] * factor);
            pixels[p + 1] = (byte)(light[p + 1] * factor);
            pixels[p + 2] = (byte)(light[p + 2] * factor);
            pixels[p + 3] = (byte)(sum * factor);
        }
        _silk.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, pw * 4, 0);
        return _silk;
    }

    // 12. Holo: holographic foil shifting around the song's hue, with light gliding over it.
    static void DrawHolo(Frame f)
    {
        var dc = f.Dc;
        ColorUtil.ToHsl(f.A, out double hue, out _, out _);
        double spread = 1 + f.Level * 0.6;
        double drift = f.T * 24;
        var foil = Gradient(new Point(drift, 0), new Point(drift + 150, f.H * 2.4), new[]
        {
            (ColorUtil.FromHsl(hue - 60 * spread, 0.7, 0.68), 1.0, 0.0),
            (ColorUtil.FromHsl(hue - 15, 0.85, 0.64), 1.0, 0.28),
            (ColorUtil.FromHsl(hue + 35 * spread, 0.8, 0.7), 1.0, 0.55),
            (ColorUtil.FromHsl(hue + 110, 0.65, 0.76), 1.0, 0.8),
            (ColorUtil.FromHsl(hue + 170, 0.55, 0.8), 1.0, 1.0),
        }, GradientSpreadMethod.Reflect);

        if (f.X < f.W)
        {
            dc.PushOpacity(0.05);
            dc.DrawRectangle(foil, null, new Rect(f.X, 0, f.W - f.X, f.H));
            dc.Pop();
        }
        if (f.X < 1) return;

        var played = new Rect(0, 0, f.X, f.H);
        dc.PushClip(new RectangleGeometry(played));
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.35, 0), (Black, 1, 0.3), (Black, 1, 1)));
        dc.PushOpacity(Math.Min(1, 0.25 + f.F * 0.8));
        dc.DrawRectangle(foil, null, played);
        dc.Pop();
        dc.DrawRectangle(Lenticular, null, played);

        // Thin glints tilted against the colour bands, sliding the other way.
        var glints = Gradient(new Point(-drift * 1.7, f.H * 2), new Point(-drift * 1.7 + 120, 0),
            new[] { (White, 0.0, 0.0), (White, 0.0, 0.62), (White, 0.2 + f.Level * 0.15, 0.8), (White, 0.0, 0.98) }, GradientSpreadMethod.Repeat);
        dc.DrawRectangle(glints, null, played);

        // Every few seconds a broad light sweeps across the foil.
        double sx = -150 + (f.T % 6.5) / 6.5 * (f.X + 300);
        dc.DrawRectangle(Gradient(new Point(sx - 70, f.H), new Point(sx + 70, 0),
            new[] { (White, 0.0, 0.0), (White, 0.32, 0.5), (White, 0.0, 1.0) }), null, played);
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.16, 0), (White, 0, 0.4), (Black, 0, 0.6), (Black, 0.2, 1)), null, played);
        dc.Pop();
        dc.Pop();

        double mid = f.H / 2;
        var sheen = ColorUtil.FromHsl(hue + 35, 0.8, 0.8);
        dc.DrawEllipse(Radial(f.X, mid, 9, f.H * 0.75, (White, 0.55, 0), (sheen, 0.25, 0.45), (sheen, 0, 1)), null, new Point(f.X, mid), 9, f.H * 0.75);
        dc.DrawRectangle(Solid(White, 0.85), null, new Rect(f.X - 0.5, 0, 1, f.H));
    }

    // 13. Bokeh: out-of-focus lights at different depths, each pulsing with its own slice of the spectrum.
    void DrawBokeh(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        var lights = new[] { Vivid(f.A), Vivid(f.B), Vivid(f.C), ColorUtil.Lerp(Vivid(f.A), White, 0.5), ColorUtil.Lerp(Vivid(f.B), White, 0.35) };
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, f.X, f.H)));
        double soft = Math.Max(0, (f.X - 40) / f.X);
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.45, 0), (Black, 1, 0.2), (Black, 1, soft), (Black, 0.25, 1)));
        dc.DrawRectangle(Horizontal(0, f.X, (f.B, f.F * 0.06, 0), (f.A, f.F * 0.14, 1)), null, new Rect(0, 0, f.X, f.H));

        int n = Math.Max(24, (int)(f.W / 40));
        double strength = 0.6 + f.F * 0.9;
        // Near lights (large, soft) first; the far, crisp ones sit on top.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = 0; j < n; j++)
            {
                double depth = Hash(j * 3 + 7);
                bool near = depth > 0.55;
                if (near != (pass == 0)) continue;
                double band = SampleBand(Hash(j * 17 + 9));
                double r = f.H * (0.14 + 0.7 * depth * depth) * (1 + 0.08 * band);
                double cx = Hash(j * 11 + 1) * f.W + Math.Sin(f.T * (0.12 + 0.1 * depth) + j) * (4 + 10 * depth);
                if (cx - r > f.X) continue;
                double cy = f.H * (0.18 + 0.64 * Hash(j * 5 + 3)) + Math.Sin(f.T * 0.21 + j * 1.3) * f.H * 0.05;
                double a = strength * (1 - depth * 0.35) * (0.5 + 0.7 * band);
                var c = lights[j % lights.Length];
                var brush = near
                    ? Radial(cx, cy, r, r, (c, a * 0.6, 0), (c, a * 0.6, 0.7), (c, a * 0.3, 0.9), (c, 0, 1))
                    : Radial(cx, cy, r, r, (c, a * 0.65, 0), (c, a * 0.8, 0.7), (ColorUtil.Lerp(c, White, 0.4), a, 0.86), (c, 0, 1));
                dc.DrawEllipse(brush, null, new Point(cx, cy), r, r);
            }
        }
        dc.Pop();
        dc.Pop();

        // The playhead is the one light in focus.
        double mid = f.H / 2;
        dc.DrawEllipse(Radial(f.X, mid, f.H * 0.7, f.H * 0.6, (White, 0.55, 0), (f.A, 0.25, 0.4), (f.A, 0, 1)), null, new Point(f.X, mid), f.H * 0.7, f.H * 0.6);
        dc.DrawEllipse(Solid(White, 0.95), null, new Point(f.X, mid), 2.2, 2.2);
    }

    // 14. Ink: watercolour bleeding out of the playhead; each drop keeps spreading after the head has passed.
    void DrawInk(Frame f)
    {
        if (f.X < 1) return;
        var dc = f.Dc;
        double splash = 1 + f.Level * 0.8;
        double Edge(double y) => f.X - 5
            + 14 * splash * (Noise(y * 0.08 + f.T * 0.22) - 0.5)
            + 6 * (Noise(y * 0.22 - f.T * 0.35 + 7) - 0.5)
            + 1.5 * (Noise(y * 0.5 + f.T * 0.6 + 13) - 0.5);

        var palette = new[] { f.A, f.B, f.C, ColorUtil.ShiftHue(f.A, -25, -0.04) };
        double strength = 0.3 + f.F * 0.7;
        dc.DrawGeometry(Solid(f.A, 0.05 + f.F * 0.1), null, EdgeShape(f.H, y => Edge(y) + 6, closed: true));

        dc.PushClip(EdgeShape(f.H, Edge, closed: true));
        dc.PushOpacityMask(Horizontal(0, f.X, (Black, 0.4, 0), (Black, 1, 0.35), (Black, 1, 1)));
        // Uneven pigment, as if the wash had dried in patches.
        var wash = new GradientStopCollection();
        for (int i = 0; i <= 12; i++)
        {
            double u = i / 12.0;
            double density = 0.2 + 0.3 * Noise(i * 1.7 + 3);
            wash.Add(new GradientStop(ColorUtil.WithAlpha(ColorUtil.Lerp(f.B, f.A, u), Math.Min(1, density * (0.5 + f.F))), u));
        }
        var washBrush = new LinearGradientBrush(wash, new Point(0, 0), new Point(f.X + 12, 0)) { MappingMode = BrushMappingMode.Absolute };
        washBrush.Freeze();
        dc.DrawRectangle(washBrush, null, new Rect(0, 0, f.X + 14, f.H));

        // Drops dropped at regular places along the bar; how far each has spread depends on
        // how long ago the playhead passed it, so the newest are still blooming open.
        const double spacing = 34;
        int n = (int)((f.X + 20) / spacing) + 1;
        for (int j = 0; j < n; j++)
        {
            double cx = (j + Hash(j * 7 + 3)) * spacing;
            double age = f.X - cx;
            if (age < -6) continue;
            double grow = 1 - Math.Exp(-(age + 6) / 32);
            double ry = f.H * (0.35 + 0.35 * Hash(j * 3 + 1)) * (0.3 + 0.7 * grow);
            double rx = ry * (1.6 + 1.4 * Hash(j * 5 + 2));
            double cy = f.H * (0.25 + 0.5 * Hash(j * 11 + 4));
            var c = palette[j % palette.Length];
            double a = strength * (0.5 + 0.5 * Math.Exp(-Math.Max(0, age) / 500));
            Bloom(cx, cy, rx, ry, c, a, j);
        }

        // A wobbly blot: pigment pools toward its rim and dries into a darker tide line.
        void Bloom(double cx, double cy, double rx, double ry, Color c, double a, int seed)
        {
            const int points = 36;
            var blot = new StreamGeometry();
            using (var g = blot.Open())
            {
                for (int k = 0; k < points; k++)
                {
                    double th = 2 * Math.PI * k / points;
                    double wob = 1 + 0.13 * Math.Sin(3 * th + seed * 1.7) + 0.08 * Math.Sin(5 * th + seed * 2.9 + f.T * 0.15)
                                 + 0.05 * Math.Sin(8 * th + seed * 4.3);
                    var p = new Point(cx + Math.Cos(th) * rx * wob, cy + Math.Sin(th) * ry * wob);
                    if (k == 0) g.BeginFigure(p, true, true);
                    else g.LineTo(p, true, true);
                }
            }
            blot.Freeze();
            var fill = Radial(cx, cy, rx * 1.25, ry * 1.25,
                (ColorUtil.Lerp(c, White, 0.12), a * 0.22, 0), (c, a * 0.4, 0.5), (c, a * 0.75, 0.85), (c, a * 0.9, 1));
            dc.DrawGeometry(fill, MakePen(ColorUtil.Lerp(c, White, 0.2), a * 0.5, 0.8), blot);
        }
        dc.Pop();
        dc.DrawRectangle(Vertical(0, f.H, (White, 0.06, 0), (White, 0, 0.4), (Black, 0, 0.6), (Black, 0.12, 1)), null, new Rect(0, 0, f.X + 14, f.H));
        dc.Pop();

        // Fine fingers of ink creeping ahead of the wet edge.
        for (int k = 0; k < 3; k++)
        {
            double ty = f.H * (0.15 + 0.7 * Noise(f.T * 0.05 + k * 9.1));
            double ex = Edge(ty);
            double length = (4 + 12 * Noise(f.T * 0.25 + k * 4.3)) * splash;
            double tilt = (Noise(f.T * 0.2 + k * 2.9) - 0.5) * 6;
            var pen = new Pen(Horizontal(ex - 2, ex + length, (f.A, 0.5 * strength, 0), (f.A, 0, 1)), 1) { EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawLine(pen, new Point(ex - 2, ty), new Point(ex + length, ty + tilt));
        }
        dc.DrawGeometry(null, MakePen(ColorUtil.Lerp(f.A, White, 0.3), 0.15 + f.F * 0.3, 1.1), EdgeShape(f.H, Edge, closed: false));
    }

    // ------------------------------------------------------------------ art processing

    static Color[] BuildStrip(BitmapSource art, int count)
    {
        var small = Pixels(art, count, 8, out int w, out int h);
        var colors = new Color[w];
        for (int x = 0; x < w; x++)
        {
            double r = 0, g = 0, b = 0;
            for (int y = 0; y < h; y++)
            {
                int i = (y * w + x) * 4;
                b += small[i];
                g += small[i + 1];
                r += small[i + 2];
            }
            var avg = Color.FromRgb((byte)(r / h), (byte)(g / h), (byte)(b / h));
            ColorUtil.ToHsl(avg, out var hue, out var sat, out var light);
            colors[x] = ColorUtil.FromHsl(hue, Math.Min(1, sat * 1.25 + 0.05), Math.Clamp(light, 0.32, 0.72));
        }
        return colors;
    }

    /// <summary>A cover colour pushed toward something that glows on a dark bar.</summary>
    static Color Vivid(Color c)
    {
        ColorUtil.ToHsl(c, out var h, out var s, out var l);
        return ColorUtil.FromHsl(h, Math.Max(s, 0.55), Math.Clamp(l, 0.5, 0.7));
    }

    static BitmapSource BuildBlur(BitmapSource art, int width, int height)
    {
        var px = Pixels(art, width, height, out int w, out int h);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    static byte[] Pixels(BitmapSource art, int width, int height, out int w, out int h)
    {
        var scaled = new TransformedBitmap(art, new ScaleTransform(width / (double)art.PixelWidth, height / (double)art.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth;
        h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        return px;
    }

    // ------------------------------------------------------------------ drawing helpers

    static StreamGeometry Ribbon(double from, double to, double step, Func<double, double> center, Func<double, double> thickness)
    {
        var top = new List<Point>();
        var bottom = new List<Point>();
        for (double x = from; x <= to + step; x += step)
        {
            double c = center(x), half = thickness(x) / 2;
            top.Add(new Point(x, c - half));
            bottom.Add(new Point(x, c + half));
        }
        bottom.Reverse();
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(top[0], true, true);
            g.PolyLineTo(top, false, true);
            g.PolyLineTo(bottom, false, true);
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>The area left of a ragged vertical edge (closed), or just the edge as a line.</summary>
    static StreamGeometry EdgeShape(double h, Func<double, double> edge, bool closed)
    {
        int steps = Math.Max(2, (int)Math.Ceiling(h / 1.5));
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(closed ? new Point(0, 0) : new Point(edge(0), 0), closed, closed);
            if (closed) g.LineTo(new Point(edge(0), 0), false, true);
            for (int i = 1; i <= steps; i++)
            {
                double y = h * i / steps;
                g.LineTo(new Point(edge(y), y), !closed, true);
            }
            if (closed) g.LineTo(new Point(0, h), false, true);
        }
        geo.Freeze();
        return geo;
    }

    static SolidColorBrush Solid(Color c, double alpha)
    {
        var brush = new SolidColorBrush(ColorUtil.WithAlpha(c, alpha * c.A / 255.0));
        brush.Freeze();
        return brush;
    }

    static Pen MakePen(Color c, double alpha, double thickness)
    {
        var pen = new Pen(Solid(c, alpha), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    static LinearGradientBrush Horizontal(double x0, double x1, params (Color C, double A, double O)[] stops)
        => Gradient(new Point(x0, 0), new Point(x1 <= x0 ? x0 + 1 : x1, 0), stops);

    static LinearGradientBrush Vertical(double y0, double y1, params (Color C, double A, double O)[] stops)
        => Gradient(new Point(0, y0), new Point(0, Math.Abs(y1 - y0) < 0.5 ? y0 + 1 : y1), stops);

    static LinearGradientBrush Gradient(Point start, Point end, (Color C, double A, double O)[] stops,
        GradientSpreadMethod spread = GradientSpreadMethod.Pad)
    {
        var collection = new GradientStopCollection(stops.Length);
        foreach (var (c, a, o) in stops) collection.Add(new GradientStop(ColorUtil.WithAlpha(c, a), o));
        var brush = new LinearGradientBrush(collection, start, end) { MappingMode = BrushMappingMode.Absolute, SpreadMethod = spread };
        brush.Freeze();
        return brush;
    }

    static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, params (Color C, double A, double O)[] stops)
    {
        var collection = new GradientStopCollection(stops.Length);
        foreach (var (c, a, o) in stops) collection.Add(new GradientStop(ColorUtil.WithAlpha(c, a), o));
        var brush = new RadialGradientBrush(collection)
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(cx, cy),
            GradientOrigin = new Point(cx, cy),
            RadiusX = Math.Max(0.5, rx),
            RadiusY = Math.Max(0.5, ry),
        };
        brush.Freeze();
        return brush;
    }

    static double Hash(double n)
    {
        double s = Math.Sin(n * 127.1 + 311.7) * 43758.5453;
        return s - Math.Floor(s);
    }

    static double Noise(double x)
    {
        double i = Math.Floor(x), k = x - i;
        return Hash(i) + (Hash(i + 1) - Hash(i)) * Smooth(k);
    }

    static double Smooth(double k) => k * k * (3 - 2 * k);

    static double Bump(double u, double center, double width)
    {
        double d = (u - center) / width;
        return Math.Exp(-d * d);
    }
}
