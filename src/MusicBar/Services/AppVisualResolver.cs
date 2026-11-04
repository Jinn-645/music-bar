using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Interop;

namespace MusicBar.Services;

public sealed record AppVisual(string Name, BitmapSource? Icon, Color? Color);

/// <summary>Turns a media session's AppUserModelId into a friendly name, icon and brand color.</summary>
public static class AppVisualResolver
{
    static readonly Dictionary<string, AppVisual> Cache = new(StringComparer.OrdinalIgnoreCase);

    static readonly (string Key, string Name, string? Hex)[] Brands =
    [
        ("spotify", "Spotify", "#1ED760"),
        ("applemusic", "Apple Music", "#FA2D48"),
        ("zunemusic", "Media Player", "#FF6A3D"),
        ("deezer", "Deezer", "#A238FF"),
        ("soundcloud", "SoundCloud", "#FF5500"),
        ("amazonmusic", "Amazon Music", "#25D1DA"),
        ("youtubemusic", "YouTube Music", "#FF0033"),
        ("tidal", "TIDAL", null),
        ("vlc", "VLC", "#FF8800"),
        ("aimp", "AIMP", "#FFB300"),
        ("foobar2000", "foobar2000", null),
        ("musicbee", "MusicBee", "#F5B400"),
    ];

    public static AppVisual Resolve(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return new AppVisual("", null, null);
        if (Cache.TryGetValue(appId, out var cached)) return cached;

        string? name = null;
        BitmapSource? icon = null;
        bool looksLikeExe = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeExe) (name, icon) = ShellImages.Load(@"shell:AppsFolder\" + appId, 64);

        if ((icon == null || name == null) && FindExecutable(appId) is { } exe)
        {
            var (shellName, exeIcon) = ShellImages.Load(exe, 64);
            icon ??= exeIcon;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                name ??= NonEmpty(info.FileDescription) ?? NonEmpty(info.ProductName);
            }
            catch (Exception)
            {
            }
            name ??= shellName;
        }

        var key = appId.Replace(" ", "").ToLowerInvariant();
        Color? color = null;
        foreach (var brand in Brands)
        {
            if (!key.Contains(brand.Key)) continue;
            name = brand.Name;
            if (brand.Hex != null && ColorUtil.TryParse(brand.Hex, out var bc)) color = bc;
            break;
        }

        if (color == null && icon != null) color = ColorExtractor.FromBitmap(icon)?.Primary;
        name ??= Path.GetFileNameWithoutExtension(appId);

        var visual = new AppVisual(name, icon, color);
        Cache[appId] = visual;
        return visual;
    }

    static string? FindExecutable(string appId)
    {
        var processName = Path.GetFileNameWithoutExtension(appId);
        if (string.IsNullOrWhiteSpace(processName)) return null;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch (Exception)
            {
                // Access denied for protected processes; try the next one.
            }
            finally
            {
                p.Dispose();
            }
        }
        return null;
    }

    static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Brings the player's window to the front (best effort).</summary>
    public static void Activate(string appId)
    {
        try
        {
            var processName = Path.GetFileNameWithoutExtension(appId);
            foreach (var p in Process.GetProcessesByName(processName))
            {
                using (p)
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                    Native.SetForegroundWindow(hwnd);
                    return;
                }
            }
            if (!appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + appId) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Activating player failed", ex);
        }
    }
}
