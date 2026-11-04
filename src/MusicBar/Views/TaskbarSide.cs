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
}
