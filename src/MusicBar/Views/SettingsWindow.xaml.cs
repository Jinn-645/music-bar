using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

public partial class SettingsWindow : Window
{
    readonly AppSettings _s;
    readonly MediaService _media;
    readonly LyricsService _lyrics;
    readonly ObservableCollection<SourceOption> _sources = [];
    readonly StackPanel[] _pages;
    readonly System.Collections.Generic.List<(SeekStyle Value, Border Tile, SeekRenderer Preview)> _seekTiles = [];
    readonly Stopwatch _previewClock = Stopwatch.StartNew();
    bool _loadingTweaks;

    /// <summary>A row in the tracked-app list; app names are shown as they are, not looked up like <see cref="Option"/> labels.</summary>
    sealed record SourceOption(string Value, string Label);

    public SettingsWindow(AppSettings settings, MediaService media, LyricsService lyrics)
    {
        _s = settings;
        _media = media;
        _lyrics = lyrics;
        InitializeComponent();
        DataContext = settings;
        _pages = [PageBar, PageLook, PageLyrics, PageAbout];

        var version = Loc.T("Set_Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
        VersionText.Text = version;
        AboutVersion.Text = $"{version} · .NET {Environment.Version.ToString(2)}";

        FontCombo.ItemsSource = BuildFonts(settings.FontFamily);
        SourceCombo.ItemsSource = _sources;
        RefreshSources();
        RefreshLyricsInfo();
        RefreshLayerStatus();
        RefreshTaskbarTweaks(pendingRestart: false);
        BuildSeekGallery();
        CompositionTarget.Rendering += OnPreviewFrame;
        UpdateNowPlaying();
        Nav.SelectedIndex = 0;

        _media.PropertyChanged += OnMediaChanged;
        _media.ThumbnailChanged += OnThumbnailChanged;
        _s.PropertyChanged += OnSettingChanged;
        Closed += (_, _) =>
        {
            _media.PropertyChanged -= OnMediaChanged;
            _media.ThumbnailChanged -= OnThumbnailChanged;
            _s.PropertyChanged -= OnSettingChanged;
            CompositionTarget.Rendering -= OnPreviewFrame;
        };
    }

    /// <summary>The page on screen, by its place in the navigation list.</summary>
    public int Page { get => Nav.SelectedIndex; set => Nav.SelectedIndex = value; }

    /// <summary>MusicBar's own fonts first (they all handle Vietnamese), then the installed ones.</summary>
    static System.Collections.Generic.List<string> BuildFonts(string current)
    {
        var names = BundledFonts.Names.ToList();
        names.AddRange(Fonts.SystemFontFamilies.Select(f => f.Source).Except(names).Distinct().OrderBy(n => n));
        if (!names.Contains(current)) names.Insert(0, current);
        return names;
    }

    void RefreshSources()
    {
        if (_sources.Count == 0) _sources.Add(new SourceOption("", Loc.T("Set_Source_Auto")));
        foreach (var s in _media.GetSessions())
            if (_sources.All(o => o.Value != s.AppId)) _sources.Add(new SourceOption(s.AppId, s.AppName));
        var pinned = _s.PinnedAppId;
        if (pinned.Length > 0 && _sources.All(o => o.Value != pinned))
            _sources.Add(new SourceOption(pinned, Loc.T("Set_Source_NotRunning", AppVisualResolver.Resolve(pinned).Name)));
    }

    void RefreshLyricsInfo()
    {
        LyricsFolderText.Text = _lyrics.Folder;
        var (saved, misses) = _lyrics.GetCacheStats();
        CacheCard.Description = Loc.T("Set_Cache_Desc", saved, misses);
    }

    /// <summary>Shows the taskbar's current size/alignment without writing anything back.</summary>
    void RefreshTaskbarTweaks(bool pendingRestart)
    {
        _loadingTweaks = true;
        try
        {
            TaskbarSizeCombo.SelectedValue = TaskbarTweaks.IsSmall;
            TaskbarCenterSwitch.IsChecked = TaskbarTweaks.IsCentered;
            TaskbarCenterSwitch.IsEnabled = TaskbarTweaks.CanCenter;
        }
        finally
        {
            _loadingTweaks = false;
        }

        TransparentCard.Description = Loc.T(Overlay?.TranslucentTaskbarDetected == true
            ? "Set_Transparent_DescTranslucentTB"
            : "Set_Transparent_Desc");
        TaskbarSizeCard.Description = Loc.T(TaskbarTweaks.Kind == TaskbarKind.Windows11 && TaskbarTweaks.HasTaskbarSizeSetting
            ? "Set_TaskbarSize_DescWin11"
            : TaskbarTweaks.CanShrinkHeight
                ? "Set_TaskbarSize_Desc"
                : "Set_TaskbarSize_DescIconsOnly");
        SwitchTaskbarButton.Visibility = TaskbarTweaks.CanSwitchToExplorerPatcherTaskbar ? Visibility.Visible : Visibility.Collapsed;

        var status = Loc.T("Set_Tb_Current", TaskbarTweaks.KindLabel);
        if (!TaskbarTweaks.CanCenter)
            status += " " + Loc.T("Set_Tb_NoCenter");
        if (TaskbarTweaks.CanSwitchToExplorerPatcherTaskbar)
            status += " " + Loc.T("Set_Tb_TryExplorerPatcher");
        status += " " + Loc.T(pendingRestart ? "Set_Tb_PendingRestart" : "Set_Tb_RestartNote");
        TaskbarStatusText.Text = status;
    }

    void TaskbarSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTweaks || TaskbarSizeCombo.SelectedValue is not bool small) return;
        TaskbarTweaks.SetSmall(small);
        RefreshTaskbarTweaks(pendingRestart: !TaskbarTweaks.AppliesLive(sizeChange: true));
    }

    void TaskbarCenter_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingTweaks) return;
        TaskbarTweaks.SetCentered(TaskbarCenterSwitch.IsChecked == true);
        RefreshTaskbarTweaks(pendingRestart: !TaskbarTweaks.AppliesLive(sizeChange: false));
    }

    void RestartExplorer_Click(object sender, RoutedEventArgs e)
        => ConfirmAndRestartExplorer(Loc.T("Set_Restart_Ask"));

    void SwitchTaskbar_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Set_SwitchEp_Ask"), Loc.T("Set_SwitchEp_Caption"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        TaskbarTweaks.UseExplorerPatcherTaskbar();
        ConfirmAndRestartExplorer(Loc.T("Set_SwitchEp_Restart"));
    }

    async void ConfirmAndRestartExplorer(string question)
    {
        var answer = MessageBox.Show(this, question + "\n" + Loc.T("Set_Restart_Warning"), Loc.T("Set_RestartExplorer"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
        {
            RefreshTaskbarTweaks(pendingRestart: true);
            return;
        }
        RestartExplorerButton.IsEnabled = SwitchTaskbarButton.IsEnabled = false;
        await System.Threading.Tasks.Task.Run(TaskbarTweaks.RestartExplorer);
        RestartExplorerButton.IsEnabled = SwitchTaskbarButton.IsEnabled = true;
        RefreshTaskbarTweaks(pendingRestart: false);
    }

    // ------------------------------------------------------------------ seekbar gallery

    void BuildSeekGallery()
    {
        var (primary, secondary, art) = Overlay?.PreviewPalette() ?? DemoPalette();
        foreach (var option in SettingsOptions.SeekStyles)
        {
            var preview = new SeekRenderer { Look = option.Value, ClipToBounds = true, Intensity = _s.FillOpacity };
            preview.SetEdge(true);
            preview.SetColors(primary, secondary, animate: false);
            preview.SetArt(art);
            preview.SetTrack(option.Label);

            var screen = new Border
            {
                Height = 34,
                Margin = new Thickness(8, 8, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)),
                Child = preview,
            };
            screen.SizeChanged += (_, _) => screen.Clip = new RectangleGeometry(new Rect(screen.RenderSize), 6, 6);

            var text = new StackPanel { Margin = new Thickness(12, 8, 12, 10) };
            text.Children.Add(new TextBlock { Text = option.Label, FontSize = 13, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = option.Description, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

            var body = new StackPanel();
            body.Children.Add(screen);
            body.Children.Add(text);

            var tile = new Border
            {
                Width = 284,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Child = body,
            };
            var value = option.Value;
            tile.MouseLeftButtonUp += (_, _) => _s.SeekStyle = value;
            tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255));
            tile.MouseLeave += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255));
            _seekTiles.Add((value, tile, preview));
            SeekGallery.Children.Add(tile);
        }
        UpdateGallerySelection();
    }

    static (Color, Color, BitmapSource?) DemoPalette()
    {
        var art = DemoContent.CreateArt(DemoContent.Tracks[0]);
        var palette = ColorExtractor.FromBitmap(art);
        return (palette?.Primary ?? ColorUtil.Brand, palette?.Secondary ?? ColorUtil.Brand, art);
    }

    void UpdateGallerySelection()
    {
        var accent = (Brush)FindResource("Accent");
        var idle = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        foreach (var (value, tile, _) in _seekTiles) tile.BorderBrush = value == _s.SeekStyle ? accent : idle;
    }

    /// <summary>Keeps the previews moving while the appearance page is on screen.</summary>
    void OnPreviewFrame(object? sender, EventArgs e)
    {
        if (!IsVisible || PageLook.Visibility != Visibility.Visible) return;
        double t = _previewClock.Elapsed.TotalSeconds;
        double progress = 0.22 + (t * 0.015) % 0.6;
        foreach (var (_, _, preview) in _seekTiles)
        {
            preview.SetState(progress, timeline: true, playing: true);
            preview.Tick();
        }
    }

    void RefreshLayerStatus()
    {
        if (Overlay is not { } overlay) return;
        var where = Loc.T(overlay.IsOverMode ? "Set_Layer_Over" : "Set_Layer_Under");
        var tool = Loc.T(overlay.TranslucentTaskbarDetected ? "Set_Layer_ToolFound" : "Set_Layer_ToolMissing");
        LayerStatusText.Text = Loc.T("Set_Layer_Status", where, tool);
    }

    void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.LyricsFolder)) RefreshLyricsInfo();
        if (e.PropertyName == nameof(AppSettings.LayerMode)) RefreshLayerStatus();
        if (e.PropertyName == nameof(AppSettings.SeekStyle)) UpdateGallerySelection();
        if (e.PropertyName == nameof(AppSettings.FillOpacity))
            foreach (var (_, _, preview) in _seekTiles) preview.Intensity = _s.FillOpacity;
    }

    void OnMediaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaService.Title) or nameof(MediaService.Artist) or nameof(MediaService.HasSession))
            UpdateNowPlaying();
    }

    void OnThumbnailChanged(object? sender, EventArgs e) => NowArt.ImageSource = _media.Thumbnail;

    void UpdateNowPlaying()
    {
        if (_media.HasSession)
        {
            NowTitle.Text = _media.Title.Length > 0 ? _media.Title : _media.AppName;
            NowArtist.Text = _media.Artist.Length > 0 ? $"{_media.Artist} · {_media.AppName}" : _media.AppName;
        }
        else
        {
            NowTitle.Text = Loc.T("Set_NowPlaying_None");
            NowArtist.Text = Loc.T("Set_NowPlaying_Hint");
        }
        NowArt.ImageSource = _media.Thumbnail;
    }

    // ------------------------------------------------------------------ handlers

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null) return;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == Nav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.ScrollToTop();
        if (Nav.SelectedIndex == 2) RefreshLyricsInfo();
    }

    void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        _s.CustomColor = hex;
        _s.AccentSource = AccentSource.Custom;
    }

    void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        HexBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        _s.AccentSource = AccentSource.Custom;
    }

    void LyricSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _s.LyricColor = hex;
    }

    void StrokeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _s.LyricStrokeColor = hex;
    }

    /// <summary>Enter applies a typed hex color right away.</summary>
    void ColorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    void PreviewEffect_Click(object sender, RoutedEventArgs e) => Overlay?.PreviewLyrics();

    void SourceCombo_DropDownOpened(object? sender, EventArgs e) => RefreshSources();

    void OpenLyricsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_lyrics.Folder);
        Open(_lyrics.Folder);
    }

    void ChangeLyricsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Set_PickLyricsFolder"),
            InitialDirectory = Directory.Exists(_lyrics.Folder) ? _lyrics.Folder : AppPaths.DataDir,
        };
        if (dialog.ShowDialog(this) == true) _s.LyricsFolder = dialog.FolderName;
    }

    void ResetLyricsFolder_Click(object sender, RoutedEventArgs e) => _s.LyricsFolder = "";

    void ClearMisses_Click(object sender, RoutedEventArgs e)
    {
        _lyrics.ClearMisses();
        RefreshLyricsInfo();
    }

    void ClearDownloaded_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.T("Set_ClearDownloaded_Ask"), Loc.T("Set_ClearDownloaded"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        int removed = _lyrics.ClearDownloaded();
        RefreshLyricsInfo();
        Log.Info($"Removed {removed} downloaded lyric files");
    }

    void OpenDataFolder_Click(object sender, RoutedEventArgs e) => Open(AppPaths.DataDir);

    void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(AppPaths.LogFile)) Open(AppPaths.LogFile);
        else Open(AppPaths.DataDir);
    }

    /// <summary>The bar, when this window runs inside MusicBar (a preview tool may host it alone).</summary>
    static TaskbarOverlay? Overlay => (Application.Current as App)?.Overlay;

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Opening {target} failed", ex);
        }
    }
}
