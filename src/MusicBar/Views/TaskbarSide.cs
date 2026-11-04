using System.Windows;
using System.Windows.Media;
using MusicBar.Interop;

namespace MusicBar.Views;

/// <summary>Which way popups beside the taskbar move: they come out of it and sink back into it.</summary>
internal static class TaskbarSide
{
    /// <summary>The translate axis that points across the taskbar, and the sign that points into it.</summary>
    public static (DependencyProperty Axis, double Toward) Slide(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Top => (TranslateTransform.YProperty, -1),
        TaskbarEdge.Left => (TranslateTransform.XProperty, -1),
        TaskbarEdge.Right => (TranslateTransform.XProperty, 1),
        _ => (TranslateTransform.YProperty, 1),
    };

    public static bool IsVertical(TaskbarEdge edge) => edge is TaskbarEdge.Left or TaskbarEdge.Right;

    /// <summary>
    /// Where a card's window goes beside the taskbar, in screen pixels: <paramref name="gap"/> off
    /// the taskbar, centred on <paramref name="anchor"/> along it and kept on the monitor. The
    /// window's <paramref name="margin"/> (room for a shadow) may hang over the taskbar.
    /// </summary>
    public static (int X, int Y) CardSpot(int width, int height, Thickness margin, int gap,
        Point anchor, TaskbarEdge edge, Native.RECT taskbar, Native.RECT monitor)
    {
        int left = (int)margin.Left, right = (int)margin.Right, top = (int)margin.Top, bottom = (int)margin.Bottom;
        if (IsVertical(edge))
        {
            int x = edge == TaskbarEdge.Left ? taskbar.Right + gap - left : taskbar.Left - gap + right - width;
            int y = System.Math.Clamp((int)System.Math.Round(anchor.Y - height / 2.0), monitor.Top - top + 6,
                System.Math.Max(monitor.Top, monitor.Bottom - height + bottom - 6));
            return (x, y);
        }
        int cx = System.Math.Clamp((int)System.Math.Round(anchor.X - width / 2.0), monitor.Left - left + 6,
            System.Math.Max(monitor.Left, monitor.Right - width + right - 6));
        int cy = edge == TaskbarEdge.Top ? taskbar.Bottom + gap - top : taskbar.Top - gap + bottom - height;
        return (cx, cy);
    }
}
