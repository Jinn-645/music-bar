using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicBar.Infrastructure;

namespace MusicBar.Views.Face;

/// <summary>
/// Draws the face and takes the user's hands: a click pokes it, dragging back and forth (or
/// wiggling the pointer over it) rubs it, a double click delights it, the scroll wheel makes it
/// dizzy. The host calls <see cref="Step"/> on its own clock with what the face can sense.
/// </summary>
public sealed class FaceView : FrameworkElement
{
    // The design box is 100 x 72; signs and ears may reach 8 units past it on every side.
    const double BoxWidth = 116, BoxHeight = 88;

    // Not quite transparent: a layered window lets clicks through fully transparent pixels.
    static readonly Brush HitArea = Frozen(new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)));

    readonly FaceRig _rig = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    FacePainter _painter = FacePainters.For(FaceStyle.Classic);
    FaceStyle _style = FaceStyle.Classic;
    double _last = -1, _level;

    Point? _pressedAt;
    double _travel;
    Point _lastPointer;
    int _wiggleDirection;
    double _wiggleDistance;

    /// <summary>For XAML: a face with a seed of its own.</summary>
    public FaceView() : this(Environment.TickCount)
    {
    }

    public FaceView(int seed)
    {
        Brain = new FaceBrain(_rig, seed);
        Focusable = false;
        Cursor = Cursors.Hand;
    }

    public FaceBrain Brain { get; }

    public Color Accent { get; set; } = ColorUtil.Brand;

    /// <summary>Lines drawn straight on the taskbar: near-white, or near-black on a light taskbar.</summary>
    public Color Ink { get; set; } = Color.FromRgb(0xFF, 0xFD, 0xF6);

    /// <summary>Hat, glasses, headphones and the focus timer; the mic follows the brain (<see cref="FaceBrain.Singing"/>).</summary>
    public FaceOutfit Outfit { get; set; }

    /// <summary>Clicks, rubs and the scroll wheel reach the brain (off for previews that only watch).</summary>
    public bool Touchable { get; set; } = true;

    public double Now => _clock.Elapsed.TotalSeconds;

    public FaceStyle Look
    {
        get => _style;
        set
        {
            if (value == _style) return;
            _style = value;
            _painter = FacePainters.For(value);
            InvalidateVisual();
        }
    }

    /// <summary>One tick: the brain decides, the rig moves, the face is drawn again.</summary>
    public void Step(FaceSenses senses)
    {
        double now = Now;
        double dt = _last < 0 ? 0 : now - _last;
        _last = now;
        _level = Math.Max(0, senses.Level);
        Brain.Tick(now, dt, senses);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;
        if (Touchable) dc.DrawRectangle(HitArea, null, new Rect(RenderSize));

        double now = Now;
        double unit = Math.Min(w / BoxWidth, h / BoxHeight);
        var (sx, sy, lift, angle, sway) = _rig.Pose(now);
        // Squash and tilt around the chin, so the face stays seated on the taskbar.
        var m = Matrix.Identity;
        m.Translate(-50, -64);
        m.Scale(sx, sy);
        m.Rotate(angle);
        m.Translate(50 + sway * 100, 64 + lift * 72);
        m.Scale(unit, unit);
        m.Translate((w - 100 * unit) / 2, (h - 72 * unit) / 2);
        dc.PushTransform(new MatrixTransform(m));
        var frame = _rig.Frame(now, Accent, Ink, _level);
        var outfit = Outfit with { Mic = _rig.MicAmount };
        FaceProps.DrawBehind(dc, _style, frame, outfit);
        _painter.Draw(dc, frame);
        FaceProps.DrawFront(dc, _style, frame, outfit);
        FacePainters.DrawExtras(dc, frame);
        dc.Pop();
    }

    // ------------------------------------------------------------------ hands on

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Touchable) return;
        if (e.ClickCount == 2)
        {
            Brain.DoubleTap(Now);
            _pressedAt = null;
        }
        else
        {
            _pressedAt = e.GetPosition(this);
            _travel = 0;
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!Touchable) return;
        // A press that hardly moved is a poke; one that dragged around was a rub.
        if (_pressedAt != null && _travel < 6) Brain.Poke(Now);
        _pressedAt = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!Touchable) return;
        var p = e.GetPosition(this);
        double dx = p.X - _lastPointer.X, dy = p.Y - _lastPointer.Y;
        _lastPointer = p;
        double width = Math.Max(1, ActualWidth);
        if (_pressedAt != null)
        {
            double moved = Math.Sqrt(dx * dx + dy * dy);
            _travel += moved;
            if (_travel >= 6) Brain.Rub(moved / width * 1.6);
            return;
        }
        // Wiggling the pointer over it, without a click, tickles too: count the turns.
        int direction = Math.Sign(dx);
        if (direction == 0) return;
        _wiggleDistance += Math.Abs(dx);
        if (direction != _wiggleDirection)
        {
            if (_wiggleDirection != 0 && _wiggleDistance > 6) Brain.Rub(0.22);
            _wiggleDirection = direction;
            _wiggleDistance = 0;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!Touchable) return;
        Brain.Scroll(Now, e.Delta);
        e.Handled = true;
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
