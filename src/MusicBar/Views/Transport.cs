using System;
using System.Windows;
using System.Windows.Media;

namespace MusicBar.Views;

/// <summary>
/// State the transport button templates draw from (Themes/Bar.xaml): the song's progress for the
/// ring around play, whether music is playing for the record's shade and the cassette key's lamp,
/// and which key carries that lamp.
/// </summary>
public static class Transport
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached(
        "Progress", typeof(double), typeof(Transport), new FrameworkPropertyMetadata(0.0));

    public static double GetProgress(DependencyObject o) => (double)o.GetValue(ProgressProperty);
    public static void SetProgress(DependencyObject o, double value) => o.SetValue(ProgressProperty, value);

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.RegisterAttached(
        "IsPlaying", typeof(bool), typeof(Transport), new FrameworkPropertyMetadata(false));

    public static bool GetIsPlaying(DependencyObject o) => (bool)o.GetValue(IsPlayingProperty);
    public static void SetIsPlaying(DependencyObject o, bool value) => o.SetValue(IsPlayingProperty, value);

    public static readonly DependencyProperty LampProperty = DependencyProperty.RegisterAttached(
        "Lamp", typeof(bool), typeof(Transport), new FrameworkPropertyMetadata(false));

    public static bool GetLamp(DependencyObject o) => (bool)o.GetValue(LampProperty);
    public static void SetLamp(DependencyObject o, bool value) => o.SetValue(LampProperty, value);
}

/// <summary>An arc from twelve o'clock, clockwise, <see cref="Progress"/> of the way round.</summary>
public sealed class ProgressArc : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ProgressArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ProgressArc), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ProgressArc), new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double p = Math.Clamp(Progress, 0, 1);
        double r = (Math.Min(ActualWidth, ActualHeight) - Thickness) / 2;
        if (p < 0.002 || r <= 0) return;
        var pen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        if (p > 0.998)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }
        double a = p * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), isFilled: false, isClosed: false);
            g.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0,
                isLargeArc: p > 0.5, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
