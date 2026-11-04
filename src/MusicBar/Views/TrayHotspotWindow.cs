using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MusicBar.Interop;

namespace MusicBar.Views;

/// <summary>
/// An invisible button laid over the tray's Control Center button. It takes
/// the click (so the MusicBar control center opens instead of Windows'),
/// draws the hover highlight the covered button can no longer show, and
/// carries a badge for notifications that arrived since the panel was opened.
/// </summary>
internal sealed class TrayHotspotWindow : Window
{
    static readonly Brush Idle = Frozen(new SolidColorBrush(Color.FromArgb(0x00, 255, 255, 255)));
    static readonly Brush Hover = Frozen(new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)));
    static readonly Brush Pressed = Frozen(new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)));

    readonly Border _highlight = new() { CornerRadius = new CornerRadius(4), Margin = new Thickness(1, 3, 1, 3), Background = Idle };
    readonly Border _badge;
    readonly TextBlock _badgeText = new()
    {
        FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, -1, 0, 0),
    };

    public TrayHotspotWindow(Brush accent)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Left = Top = -20000;
        Width = 32;
        Height = 30;
        Title = "MusicBar Control Center Button";
        SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[CC_Title]") { Source = Infrastructure.Loc.Instance });
        Cursor = Cursors.Arrow;

        _badge = new Border
        {
            MinWidth = 14, Height = 14, CornerRadius = new CornerRadius(7), Padding = new Thickness(3, 0, 3, 0),
            Background = accent, BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x10, 0x14)), BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            Child = _badgeText,
        };
        var root = new Grid();
        root.Children.Add(_highlight);
        root.Children.Add(_badge);
        Content = root;

        MouseEnter += (_, _) => _highlight.Background = Hover;
        MouseLeave += (_, _) => _highlight.Background = Idle;
        MouseLeftButtonDown += (_, _) => _highlight.Background = Pressed;
        MouseLeftButtonUp += (_, _) =>
        {
            _highlight.Background = IsMouseOver ? Hover : Idle;
            Clicked?.Invoke();
        };

        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, Native.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public event Action? Clicked;

    public void SetBadge(int count)
    {
        _badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _badgeText.Text = count > 9 ? "9+" : count.ToString();
    }

    public void SetBadgeForeground(Color accent) =>
        _badgeText.Foreground = new SolidColorBrush(Infrastructure.ColorUtil.Luminance(accent) > 0.42 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White);

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(Native.MA_NOACTIVATE);
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
