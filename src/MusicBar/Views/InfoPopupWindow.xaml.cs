using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Interop;

namespace MusicBar.Views;

public sealed record SongCard(
    string Title,
    string Artist,
    string Album,
    int? Year,
    IReadOnlyList<string> Genres,
    string? TrackLabel,
    ImageSource? SourceIcon,
    BitmapSource? Cover);

/// <summary>
/// The song card that rises above the taskbar when the pointer rests on the
/// album cover. Purely informational, so it never takes clicks or focus.
/// </summary>
public partial class InfoPopupWindow : Window
{
    const double CardGap = 10;
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

    readonly SolidColorBrush _chipAccent = new(Color.FromArgb(0x47, 0xFF, 0x6B, 0x6B));
    int _showToken;
    long _lastSecond = -1;

    public InfoPopupWindow(SolidColorBrush accentBrush)
    {
        InitializeComponent();
        ProgressFill.Background = accentBrush;
        Card.SizeChanged += (_, _) => Card.Clip = new RectangleGeometry(new Rect(Card.RenderSize), 14, 14);
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                Native.WS_EX_APPWINDOW);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public bool IsOpen { get; private set; }

    public void SetAccent(Color accent)
    {
        WashStop.Color = ColorUtil.WithAlpha(accent, 0.34);
        _chipAccent.Color = ColorUtil.WithAlpha(accent, 0.3);
    }

    public void SetContent(SongCard card)
    {
        TitleText.Text = card.Title;
        ArtistText.Text = card.Artist;
        ArtistText.Visibility = card.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var album = card.Album;
        if (card.Year is int year) album = album.Length > 0 ? $"{album}  ·  {year}" : year.ToString();
        AlbumText.Text = album;
        AlbumText.Visibility = album.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        CoverBrush.ImageSource = card.Cover;
        BackdropBrush.ImageSource = card.Cover;
        SourceIcon.Source = card.SourceIcon;
        SourceBadge.Visibility = card.SourceIcon != null ? Visibility.Visible : Visibility.Collapsed;

        Chips.Children.Clear();
        bool first = true;
        foreach (var genre in card.Genres)
        {
            Chips.Children.Add(Chip(genre, first));
            first = false;
        }
        if (card.TrackLabel != null) Chips.Children.Add(Chip(card.TrackLabel, accent: false));
        Chips.Visibility = Chips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _lastSecond = -1;
    }

    Border Chip(string text, bool accent) => new()
    {
        CornerRadius = new CornerRadius(8),
        Background = accent ? _chipAccent : new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(7, 1, 7, 2),
        Margin = new Thickness(0, 0, 5, 4),
        Child = new TextBlock { Text = text, FontSize = 10.5, Foreground = Brushes.White },
    };

    public void SetProgress(TimeSpan position, TimeSpan duration)
    {
        bool known = duration > TimeSpan.Zero;
        double frac = known ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
        ProgressScale.ScaleX = frac;
        long second = (long)position.TotalSeconds;
        if (second == _lastSecond) return;
        _lastSecond = second;
        ElapsedText.Text = Format(position);
        RemainingText.Text = known ? "-" + Format(duration - position) : "";
    }

    static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>
    /// Opens the card next to the taskbar, horizontally centred on
    /// <paramref name="anchor"/> (screen pixels) and kept on the monitor.
    /// </summary>
    internal void ShowAt(Point anchor, int taskbarEdgeY, bool above, Native.RECT monitorBounds)
    {
        _showToken++;
        Root.RenderTransformOrigin = new Point(0.5, above ? 1 : 0);
        if (!IsVisible) Show();
        UpdateLayout();

        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        int w = (int)Math.Round(ActualWidth * m.M11);
        int h = (int)Math.Round(ActualHeight * m.M22);
        int x = (int)Math.Round(anchor.X - w / 2.0);
        x = Math.Clamp(x, monitorBounds.Left - (int)(Root.Margin.Left * m.M11) + 6,
            Math.Max(monitorBounds.Left, monitorBounds.Right - w + (int)(Root.Margin.Right * m.M11) - 6));
        int y = above
            ? taskbarEdgeY - (int)Math.Round(CardGap * m.M22) + (int)Math.Round(Root.Margin.Bottom * m.M22) - h
            : taskbarEdgeY + (int)Math.Round(CardGap * m.M22) - (int)Math.Round(Root.Margin.Top * m.M22);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        IsOpen = true;
        var d = TimeSpan.FromMilliseconds(280);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.93, 1, d) { EasingFunction = Spring });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.93, 1, d) { EasingFunction = Spring });
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(above ? 12 : -12, 0, d) { EasingFunction = EaseOut });
    }

    public void Dismiss()
    {
        if (!IsOpen) return;
        IsOpen = false;
        int token = ++_showToken;
        var d = TimeSpan.FromMilliseconds(150);
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) =>
        {
            if (token == _showToken) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, d) { EasingFunction = EaseOut });
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
