using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// "A new MusicBar is out", in the corner above the tray, without taking focus. Update installs it
/// (or opens the release page for a portable copy); Later asks again next time; ticking "skip this
/// version" with Later stays quiet until an even newer one.
/// </summary>
public partial class UpdateWindow : Window
{
    readonly UpdateService _updates;
    readonly ReleaseInfo _release;
    readonly AppSettings _s;
    CancellationTokenSource? _download;

    public UpdateWindow(UpdateService updates, ReleaseInfo release, AppSettings settings, Color accent)
    {
        _updates = updates;
        _release = release;
        _s = settings;
        InitializeComponent();
        Resources["Accent"] = new SolidColorBrush(accent);
        Heading.Text = Loc.T("Upd_Title", release.Version.ToString(3));
        Running.Text = Loc.T("Upd_Running", UpdateService.Current.ToString(3));
        Notes.Text = UpdateService.Summary(release.Notes, settings.Language);
        bool installs = UpdateService.IsInstalled && release.Setup != null;
        UpdateButton.Content = Loc.T(installs ? "Upd_Install" : "Upd_Download");
        // In the corner above the tray, where Windows puts its own notifications.
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        };
        Closed += (_, _) => _download?.Cancel();
    }

    void Later_Click(object sender, RoutedEventArgs e)
    {
        if (SkipBox.IsChecked == true)
        {
            _s.SkippedVersion = _release.Tag;
            Log.Info($"Update {_release.Tag} skipped");
        }
        Close();
    }

    void Page_Click(object sender, RoutedEventArgs e) => Open(_release.Page);

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateService.IsInstalled || _release.Setup == null)
        {
            // A portable copy is replaced by hand: the release page has the zip.
            Open(_release.Page);
            Close();
            return;
        }
        UpdateButton.IsEnabled = LaterButton.IsEnabled = SkipBox.IsEnabled = false;
        Problem.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = Loc.T("Upd_Downloading", 0);
        _download = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            ProgressFill.Width = ((FrameworkElement)ProgressFill.Parent).ActualWidth * p;
            ProgressText.Text = Loc.T("Upd_Downloading", (int)(p * 100));
        });
        string? installer = await _updates.DownloadAsync(_release, progress, _download.Token);
        if (installer == null || !UpdateService.Install(installer))
        {
            if (!IsLoaded) return;
            ProgressPanel.Visibility = Visibility.Collapsed;
            Problem.Text = Loc.T("Upd_Failed");
            Problem.Visibility = Visibility.Visible;
            UpdateButton.IsEnabled = LaterButton.IsEnabled = SkipBox.IsEnabled = true;
            return;
        }
        // The installer closes MusicBar itself; leaving now spares it the wait.
        ProgressText.Text = Loc.T("Upd_Installing");
        (Application.Current as App)?.ExitApp();
    }

    static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Opening {url} failed", ex);
        }
    }
}
