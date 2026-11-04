using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// MusicBar's control center: notifications on top, volume and the playback
/// device below. The device list slides out to the left of the panel instead
/// of replacing it, so there is no going in and back out.
/// </summary>
public partial class ControlCenterWindow : Window
{
    const double Gap = 12;
    static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    readonly NotificationFeed _feed;
    readonly AudioOutputs _audio;
    readonly DispatcherTimer _volumePoll = new() { Interval = TimeSpan.FromMilliseconds(300) };
    int _token;
    bool _syncing;
    enum SideKind { None, Outputs, Project, Cast }

    SideKind _side;
    int _sideToken;
    string? _defaultId;
    int _right, _edgeY;
    bool _above = true;
    Color _accent = ColorUtil.Brand;
    readonly System.Collections.Generic.List<Action> _tilePainters = [];

    public ControlCenterWindow(NotificationFeed feed, AudioOutputs audio)
    {
        InitializeComponent();
        _feed = feed;
        _audio = audio;
        _feed.Changed += () =>
        {
            if (IsOpen) RenderNotifications();
        };
        _volumePoll.Tick += (_, _) => SyncVolume();
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW, Native.WS_EX_APPWINDOW);
        };
        Deactivated += (_, _) => Dismiss();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_side != SideKind.None) SetSide(SideKind.None);
            else Dismiss();
            e.Handled = true;
        };
        // Growing (a new notification) keeps the panel's edge where it is.
        SizeChanged += (_, _) =>
        {
            if (IsOpen) Place();
        };
    }

    public IntPtr Hwnd { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>When the panel last closed; a click on the tray button right after is the same click.</summary>
    public DateTime ClosedAt { get; private set; } = DateTime.MinValue;

    /// <summary>The playback device was switched.</summary>
    public event Action? OutputChanged;

    public void SetAccent(Color accent)
    {
        _accent = accent;
        Resources["Accent"] = new SolidColorBrush(accent);
        foreach (var paint in _tilePainters) paint();
        NotificationWash.Color = ColorUtil.WithAlpha(accent, 0.16);
        CountText.Foreground = new SolidColorBrush(ColorUtil.Luminance(accent) > 0.42 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White);
    }

    /// <summary>
    /// Opens the panel at the right end of the taskbar: <paramref name="right"/> is
    /// the screen x the panel ends at, <paramref name="edgeY"/> the taskbar edge.
    /// </summary>
    internal void Open(int right, int edgeY, bool above, Native.RECT monitorBounds)
    {
        _token++;
        _right = right;
        _edgeY = edgeY;
        _above = above;
        IsOpen = true;
        NotificationScroll.MaxHeight = Math.Max(160, monitorBounds.Height / Scale() * 0.5);
        SetSide(SideKind.None, animate: false);
        SyncVolume();
        SyncOutput();
        RenderTiles();
        RenderNotifications();
        _feed.MarkAllSeen();
        _ = _feed.RefreshAsync();

        if (!IsVisible) Show();
        UpdateLayout();
        Place();
        Activate();
        _volumePoll.Start();

        var d = TimeSpan.FromMilliseconds(260);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        MainShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(above ? 18 : -18, 0, d) { EasingFunction = EaseOut });
    }

    public void Dismiss()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ClosedAt = DateTime.Now;
        _volumePoll.Stop();
        int token = ++_token;
        var d = TimeSpan.FromMilliseconds(140);
        var fade = new DoubleAnimation(0, d);
        fade.Completed += (_, _) =>
        {
            if (token == _token) Hide();
        };
        Root.BeginAnimation(OpacityProperty, fade);
        MainShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_above ? 8 : -8, d) { EasingFunction = EaseOut });
    }

    double Scale() => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11
                      ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Keeps the main card's outer corner pinned next to the taskbar's end.</summary>
    void Place()
    {
        if (Hwnd == IntPtr.Zero) return;
        double s = Scale();
        int w = (int)Math.Round(ActualWidth * s);
        int h = (int)Math.Round(ActualHeight * s);
        int x = _right - (int)Math.Round(Gap * s) + (int)Math.Round(Root.Margin.Right * s) - w;
        int y = _above
            ? _edgeY - (int)Math.Round(Gap * s) + (int)Math.Round(Root.Margin.Bottom * s) - h
            : _edgeY + (int)Math.Round(Gap * s) - (int)Math.Round(Root.Margin.Top * s);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE);
    }

    // ------------------------------------------------------------------ volume and device

    void SyncVolume()
    {
        if (VolumeSlider.IsMouseCaptureWithin) return;
        float? level = _audio.Volume;
        bool muted = _audio.Muted;
        _syncing = true;
        VolumeSlider.IsEnabled = MuteButton.IsEnabled = level != null;
        VolumeSlider.Value = Math.Round((level ?? 0) * 100);
        _syncing = false;
        ShowVolume(VolumeSlider.Value, muted);

        if (IsOpen && _audio.Default()?.Id is { } id && id != _defaultId) SyncOutput();
    }

    void ShowVolume(double value, bool muted)
    {
        VolumeText.Text = ((int)Math.Round(value)).ToString();
        VolumeGlyph.Text = muted ? "" : value < 1 ? "" : value < 34 ? "" : value < 67 ? "" : "";
        VolumeSlider.Opacity = muted ? 0.45 : 1;
    }

    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        // Moving the slider means "I want to hear it", as in the Windows flyout.
        if (_audio.Muted) _audio.Muted = false;
        _audio.Volume = (float)(e.NewValue / 100);
        ShowVolume(e.NewValue, false);
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        _audio.Muted = !_audio.Muted;
        ShowVolume(VolumeSlider.Value, _audio.Muted);
    }

    void SyncOutput()
    {
        var current = _audio.Default();
        _defaultId = current?.Id;
        OutputName.Text = current?.Name ?? "Không có thiết bị phát";
        OutputAdapter.Text = current?.Adapter ?? "";
        OutputAdapter.Visibility = OutputAdapter.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputGlyph.Text = Glyph(current?.Kind ?? AudioFormFactor.Speakers);
        if (_side == SideKind.Outputs) RenderOutputs();
    }

    static string Glyph(AudioFormFactor kind) => kind switch
    {
        AudioFormFactor.Headphones or AudioFormFactor.Headset or AudioFormFactor.Handset => "",
        AudioFormFactor.DigitalDisplay => "",
        _ => "",
    };

    void Output_Click(object sender, RoutedEventArgs e) => SetSide(_side == SideKind.Outputs ? SideKind.None : SideKind.Outputs);

    /// <summary>Slides a list out beside the panel (or back in): playback devices, display modes or wireless displays.</summary>
    void SetSide(SideKind kind, bool animate = true)
    {
        _side = kind;
        _sideToken++;
        bool open = kind != SideKind.None;
        switch (kind)
        {
            case SideKind.Outputs: RenderOutputs(); break;
            case SideKind.Project: RenderProject(); break;
            case SideKind.Cast: RenderCast(); break;
        }
        Side.IsHitTestVisible = open;
        OutputChevron.Text = kind == SideKind.Outputs ? "" : "";
        var d = TimeSpan.FromMilliseconds(animate ? 220 : 0);
        Side.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, animate ? TimeSpan.FromMilliseconds(open ? 180 : 120) : d));
        SideShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(open ? 0 : 28, d) { EasingFunction = EaseOut });
    }

    void SideHeader(string title)
    {
        SideTitle.Text = title;
        SideList.Children.Clear();
    }

    TextBlock SideMessage(string text)
    {
        var message = new TextBlock { Text = text, FontSize = 12, Opacity = 0.55, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 2, 10, 8) };
        SideList.Children.Add(message);
        return message;
    }

    /// <summary>One entry of a side list: icon, name, optional detail, and a check on the current one.</summary>
    void SideRow(string glyph, string title, string? detail, bool current, Action click)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Opacity = current ? 1 : 0.75,
        };
        if (current) icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(detail))
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.55, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        if (current)
        {
            var check = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            };
            check.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            Grid.SetColumn(check, 2);
            row.Children.Add(check);
        }

        var button = new Button { Style = (Style)FindResource("RowButton"), Content = row, Margin = new Thickness(0, 0, 0, 2) };
        button.Click += (_, _) => click();
        SideList.Children.Add(button);
    }

    void RenderOutputs()
    {
        SideHeader("Đầu ra âm thanh");
        var outputs = _audio.List();
        if (outputs.Count == 0)
        {
            SideMessage("Không tìm thấy thiết bị phát nào.");
            return;
        }
        foreach (var output in outputs.OrderBy(o => o.Name))
        {
            var id = output.Id;
            SideRow(Glyph(output.Kind), output.Name, output.Adapter, id == _defaultId, () =>
            {
                if (id != _defaultId && _audio.SetDefault(id))
                {
                    SyncOutput();
                    SyncVolume();
                    OutputChanged?.Invoke();
                }
                SetSide(SideKind.None);
            });
        }
    }

    static readonly (ProjectMode Mode, string Glyph, string Title, string Detail)[] ProjectModes =
    [
        (ProjectMode.PcScreenOnly, "", "Chỉ màn hình máy tính", "Tắt màn hình thứ hai"),
        (ProjectMode.Duplicate, "", "Nhân bản", "Hai màn hình hiện giống nhau"),
        (ProjectMode.Extend, "", "Mở rộng", "Kéo cửa sổ sang màn hình thứ hai"),
        (ProjectMode.SecondScreenOnly, "", "Chỉ màn hình thứ hai", "Tắt màn hình chính"),
    ];

    void RenderProject()
    {
        SideHeader("Màn hình chiếu");
        var current = DisplayProjection.Current();
        foreach (var (mode, glyph, title, detail) in ProjectModes)
        {
            var m = mode;
            SideRow(glyph, title, detail, m == current, () =>
            {
                if (m != DisplayProjection.Current() && !DisplayProjection.Set(m))
                {
                    RenderProject();
                    SideMessage("Windows không đổi được sang chế độ này (cần có màn hình thứ hai đang cắm).");
                    return;
                }
                SetSide(SideKind.None);
            });
        }
    }

    async void RenderCast(string? note = null)
    {
        int token = _sideToken;
        SideHeader("Truyền màn hình");
        if (note != null) SideMessage(note);
        if (WirelessDisplays.Connected)
        {
            SideRow("", "Ngắt kết nối", "Đang truyền tới màn hình không dây", true, async () =>
            {
                await WirelessDisplays.DisconnectAsync();
                foreach (var paint in _tilePainters) paint();
                if (_side == SideKind.Cast) RenderCast();
            });
        }
        var searching = SideMessage("Đang tìm màn hình không dây…");
        var displays = await WirelessDisplays.FindAsync();
        if (token != _sideToken) return;
        SideList.Children.Remove(searching);
        if (displays.Count == 0)
        {
            SideMessage(WirelessDisplays.HasWifi
                ? "Không tìm thấy màn hình không dây nào. Bật chế độ nhận Miracast trên TV hoặc màn hình rồi mở lại."
                : "Truyền màn hình qua Miracast cần Wi-Fi; máy chưa cắm bộ thu Wi-Fi nào.");
            return;
        }
        foreach (var display in displays)
        {
            var d = display;
            SideRow("", d.Name, null, false, async () =>
            {
                SideHeader("Truyền màn hình");
                SideMessage($"Đang kết nối tới {d.Name}…");
                bool ok = await WirelessDisplays.ConnectAsync(d);
                foreach (var paint in _tilePainters) paint();
                if (_side != SideKind.Cast) return;
                if (ok) SetSide(SideKind.None);
                else RenderCast($"Không kết nối được tới {d.Name}.");
            });
        }
    }

    // ------------------------------------------------------------------ quick settings

    async void RenderTiles()
    {
        var actions = await QuickActions.ListAsync();
        TileGrid.Children.Clear();
        _tilePainters.Clear();
        foreach (var action in actions) TileGrid.Children.Add(Tile(action));
    }

    FrameworkElement Tile(QuickAction action)
    {
        var glyph = new TextBlock
        {
            Text = action.Glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(glyph);
        // A tile with its own list carries a chevron, as in the Windows panel.
        if (action.Panel != QuickPanel.None)
        {
            var chevron = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 9, Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 0, 0),
            };
            chevron.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(TextBlock.Foreground)) { Source = glyph });
            face.Children.Add(chevron);
        }
        var button = new Button { Style = (Style)FindResource("TileButton"), Content = face, ToolTip = action.Label };
        var label = new TextBlock
        {
            Text = action.Label, FontSize = 11, Opacity = 0.85, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 30, Margin = new Thickness(0, 5, 0, 0),
        };

        void Paint()
        {
            bool on = action.IsOn?.Invoke() == true;
            if (on) button.SetResourceReference(BackgroundProperty, "Accent");
            else button.SetResourceReference(BackgroundProperty, "TileIdle");
            glyph.Foreground = on && ColorUtil.Luminance(_accent) > 0.42 ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) : Brushes.White;
        }
        Paint();
        _tilePainters.Add(Paint);

        button.Click += async (_, _) =>
        {
            if (action.Panel != QuickPanel.None)
            {
                var kind = action.Panel == QuickPanel.Project ? SideKind.Project : SideKind.Cast;
                SetSide(_side == kind ? SideKind.None : kind);
                return;
            }
            if (action.Run == null) return;
            // A shortcut hands over to a Windows page; a switch stays here and repaints.
            if (action.IsOn == null) Dismiss();
            try
            {
                await action.Run();
            }
            catch (Exception ex)
            {
                Log.Error($"Quick setting '{action.Label}' failed", ex);
            }
            Paint();
        };

        var cell = new StackPanel { Margin = new Thickness(4, 0, 4, 10) };
        cell.Children.Add(button);
        cell.Children.Add(label);
        return cell;
    }

    // ------------------------------------------------------------------ notifications

    void ClearAll_Click(object sender, RoutedEventArgs e) => _feed.ClearAll();

    void RenderNotifications()
    {
        var items = _feed.Items;
        NotificationList.Children.Clear();
        foreach (var item in items) NotificationList.Children.Add(NotificationCard(item));

        bool any = items.Count > 0;
        NotificationScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = _feed.Available ? "Không có thông báo mới" : "Windows chưa cho MusicBar đọc thông báo";
        CountChip.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = items.Count.ToString();
        ClearAllButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement NotificationCard(NotificationItem item)
    {
        var dismiss = new Button
        {
            Style = (Style)FindResource("TextButton"), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(4, -4, -6, -4),
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10 },
            Opacity = 0, ToolTip = "Bỏ thông báo này",
        };
        dismiss.Click += (_, e) =>
        {
            e.Handled = true;
            _feed.Dismiss(item.Id);
        };

        var head = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement logo = item.Logo != null
            ? new Image { Source = item.Logo, Width = 16, Height = 16 }
            : new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, Opacity = 0.7 };
        logo.Margin = new Thickness(0, 0, 8, 0);
        logo.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        head.Children.Add(logo);
        var app = new TextBlock { Text = item.AppName, FontSize = 11.5, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(app, 1);
        head.Children.Add(app);
        var time = new TextBlock { Text = When(item.Time), FontSize = 11, Opacity = 0.45, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(time, 2);
        head.Children.Add(time);
        Grid.SetColumn(dismiss, 3);
        head.Children.Add(dismiss);

        var body = new StackPanel();
        body.Children.Add(head);
        if (item.Title.Length > 0)
            body.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 38, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Body.Length > 0)
            body.Children.Add(new TextBlock { Text = item.Body, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxHeight = 66, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Child = body,
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
            dismiss.Opacity = 1;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255));
            dismiss.Opacity = 0;
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            Dismiss();
            _feed.Open(item);
        };
        return card;
    }

    static string When(DateTimeOffset time)
    {
        var ago = DateTimeOffset.Now - time;
        if (ago < TimeSpan.FromMinutes(1)) return "Vừa xong";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} phút";
        var local = time.ToLocalTime();
        if (local.Date == DateTime.Today) return local.ToString("HH:mm");
        if (local.Date == DateTime.Today.AddDays(-1)) return "Hôm qua";
        return local.ToString("dd/MM");
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
