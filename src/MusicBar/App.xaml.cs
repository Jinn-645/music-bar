using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views;

namespace MusicBar;

public partial class App : Application
{
    const string MutexName = "MusicBar.SingleInstance.v1";
    const string ShowSettingsEventName = "MusicBar.ShowSettings.v1";
    const string ExitEventName = "MusicBar.Exit.v1";

    Mutex? _mutex;
    bool _ownsMutex;
    EventWaitHandle? _showSignal;
    EventWaitHandle? _exitSignal;
    volatile bool _exiting;

    SettingsService? _settings;
    MediaService? _media;
    LyricsService? _lyrics;
    TaskbarOverlay? _overlay;
    SettingsWindow? _settingsWindow;
    TrayIcon? _tray;

    public static App Instance => (App)Current;

    public TaskbarOverlay? Overlay => _overlay;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool exitRequest = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex || exitRequest)
        {
            // Already running: forward the request to that instance, then leave.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(exitRequest ? ExitEventName : ShowSettingsEventName);
                signal.Set();
            }
            catch (Exception)
            {
            }
            if (_ownsMutex) _mutex.ReleaseMutex();
            _ownsMutex = false;
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        new Thread(() =>
        {
            var handles = new WaitHandle[] { _showSignal, _exitSignal };
            while (!_exiting)
            {
                int which = WaitHandle.WaitAny(handles);
                if (_exiting) break;
                if (which == 0) Dispatcher.BeginInvoke(ShowSettings);
                else Dispatcher.BeginInvoke(ExitApp);
            }
        })
        { IsBackground = true, Name = "MusicBar.Signals" }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled UI exception", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Log.Error("Fatal exception", ex.ExceptionObject as Exception);
            // Do not leave the tray's volume icon and notification button hidden behind us.
            TrayMerge.Restore();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TrayMerge.Restore();
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Error("Unobserved task exception", ex.Exception);
            ex.SetObserved();
        };

        Log.Info($"MusicBar starting ({string.Join(" ", e.Args)})");
        _settings = new SettingsService();
        var settings = _settings.Current;
        if (settings.StartWithWindows) StartupService.Apply(true);
        settings.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName == nameof(AppSettings.StartWithWindows)) StartupService.Apply(settings.StartWithWindows);
        };

        _media = new MediaService(settings);
        _lyrics = new LyricsService(settings);
        _overlay = new TaskbarOverlay(settings, _media, _lyrics);
        _overlay.Start();

        _tray = new TrayIcon(ShowSettings, _overlay.ShowMenu);
        _media.TrackChanged += (_, _) =>
            _tray.SetTooltip(_media.HasSession && _media.Title.Length > 0 ? $"MusicBar · {_media.Title}" : "MusicBar");

        if (e.Args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase))) _media.StartDemo();
        else await _media.InitializeAsync();

        if (_settings.IsFirstRun || e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
            ShowSettings();
    }

    public void ShowSettings()
    {
        if (_settings == null || _media == null || _lyrics == null || _exiting) return;
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_settings.Current, _media, _lyrics);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }
        _settingsWindow.Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        _settings?.SaveNow();
        _settingsWindow?.Close();
        _overlay?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _showSignal?.Set();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _settings?.SaveNow();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        base.OnExit(e);
    }
}
