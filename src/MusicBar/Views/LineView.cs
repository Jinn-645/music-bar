using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicBar.Views;

/// <summary>
/// Hosts one line of text at its natural width. When the text is wider than
/// the space available it is left-anchored, clipped, and can be scrolled
/// (marquee) via <see cref="Scroll"/>, with soft fades on the hidden edges.
/// </summary>
internal sealed class LineView : Panel
{
    readonly FrameworkElement _child;
    readonly TranslateTransform _shift = new();
    readonly LinearGradientBrush _mask;
    readonly GradientStop _leftEdge = new(Colors.Black, 0);
    readonly GradientStop _leftInner = new(Colors.Black, 0.05);
    readonly GradientStop _rightInner = new(Colors.Black, 0.95);
    readonly GradientStop _rightEdge = new(Colors.Black, 1);
    double _scroll;

    public LineView(FrameworkElement child, HorizontalAlignment align)
    {
        _child = child;
        Align = align;
        _child.RenderTransform = _shift;
        Children.Add(_child);
        _mask = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        _mask.GradientStops.Add(_leftEdge);
        _mask.GradientStops.Add(_leftInner);
        _mask.GradientStops.Add(_rightInner);
        _mask.GradientStops.Add(_rightEdge);
    }

    public HorizontalAlignment Align { get; }

    public double Overflow { get; private set; }

    public double Scroll
    {
        get => _scroll;
        set
        {
            value = Math.Clamp(value, 0, Overflow);
            if (Math.Abs(value - _scroll) < 0.25) return;
            _scroll = value;
            _shift.X = -value;
            UpdateMask();
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        _child.Measure(new Size(double.PositiveInfinity, available.Height));
        var w = _child.DesiredSize.Width;
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), _child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = _child.DesiredSize.Width;
        Overflow = Math.Max(0, w - final.Width);
        double x = 0;
        if (Overflow <= 0)
        {
            if (Align == HorizontalAlignment.Center) x = (final.Width - w) / 2;
            else if (Align == HorizontalAlignment.Right) x = final.Width - w;
        }
        _child.Arrange(new Rect(x, (final.Height - _child.DesiredSize.Height) / 2, w, _child.DesiredSize.Height));
        if (_scroll > Overflow)
        {
            _scroll = Overflow;
            _shift.X = -_scroll;
        }

        if (Overflow > 0 && final.Width > 48)
        {
            double f = 20 / final.Width;
            _leftInner.Offset = f;
            _rightInner.Offset = 1 - f;
            OpacityMask = _mask;
            UpdateMask();
            // Clip horizontally only, so effects that move text vertically are not cut.
            Clip = new RectangleGeometry(new Rect(0, -final.Height, final.Width, final.Height * 3));
        }
        else
        {
            OpacityMask = null;
            Clip = null;
        }
        return final;
    }

    void UpdateMask()
    {
        if (OpacityMask == null) return;
        _leftEdge.Color = _scroll > 1 ? Colors.Transparent : Colors.Black;
        _rightEdge.Color = _scroll < Overflow - 1 ? Colors.Transparent : Colors.Black;
    }
}
