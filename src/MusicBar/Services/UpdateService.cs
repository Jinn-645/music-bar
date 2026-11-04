using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

/// <summary>A release on GitHub newer than this copy.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string Page, Uri? Setup, long SetupSize, string? SetupSha256);

/// <summary>
/// Looks for a newer MusicBar on the GitHub releases page and installs it: the installed copy
/// downloads the new installer and runs it quietly (it closes MusicBar and opens the new one);
/// a portable copy is pointed at the release page instead.
/// </summary>
public sealed class UpdateService
{
    const string LatestRelease = "https://api.github.com/repos/lvt1693/music-bar/releases/latest";

    readonly AppSettings _settings;

    public UpdateService(AppSettings settings) => _settings = settings;

    /// <summary>This copy's version (major.minor.build).</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Installed by the installer (its uninstaller sits beside the app), rather than unzipped.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    HttpClient Client(TimeSpan timeout)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (!string.IsNullOrWhiteSpace(_settings.ProxyUrl))
        {
            handler.Proxy = new WebProxy(_settings.ProxyUrl);
            handler.UseProxy = true;
        }
        var http = new HttpClient(handler) { Timeout = timeout };
        // GitHub's API turns away requests without a user agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicBar/{Current} (+https://github.com/lvt1693/music-bar)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The latest release if it is newer than this copy, null when there is none (or GitHub can't be reached).</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var http = Client(TimeSpan.FromSeconds(20));
            using var response = await http.GetAsync(LatestRelease, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"Update check: GitHub answered {(int)response.StatusCode}");
                return null;
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
            if (version <= Current) return null;

            Uri? setup = null;
            long size = 0;
            string? sha = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                setup = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
                size = asset.GetProperty("size").GetInt64();
                // GitHub publishes each asset's digest ("sha256:…"); check the download against it when there is one.
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = d[7..];
            }
            string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            string page = root.GetProperty("html_url").GetString() ?? "https://github.com/lvt1693/music-bar/releases/latest";
            Log.Info($"Update check: {tag} is out (this is {Current})");
            return new ReleaseInfo(version, tag, notes, page, setup, size, sha);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            return null;
        }
    }

    /// <summary>Downloads the release's installer to a temporary folder; null when it failed or didn't match.</summary>
    public async Task<string?> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        if (release.Setup == null) return null;
        string folder = Path.Combine(Path.GetTempPath(), "MusicBar-update");
        string path = Path.Combine(folder, Path.GetFileName(release.Setup.LocalPath));
        try
        {
            Directory.CreateDirectory(folder);
            using var http = Client(TimeSpan.FromMinutes(10));
            using var response = await http.GetAsync(release.Setup, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.SetupSize;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(path))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress.Report((double)done / total);
                }
            }
            if (release.SetupSha256 is { } expected)
            {
                string actual;
                await using (var check = File.OpenRead(path))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Info($"Update download of {release.Tag} did not match its published checksum");
                    File.Delete(path);
                    return null;
                }
            }
            Log.Info($"Update {release.Tag} downloaded to {path}");
            return path;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Downloading update {release.Tag} failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Runs the installer quietly: it shows its progress, closes MusicBar, installs over this copy
    /// and opens the new one. False if Windows wouldn't start it.
    /// </summary>
    public static bool Install(string installer)
    {
        try
        {
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Starting the update installer failed", ex);
            return false;
        }
    }

    /// <summary>
    /// The part of the release notes worth showing: the "Mới / New" and "Sửa lỗi / Fixed" sections in
    /// Vietnamese, the "In English" paragraph in English, as plain text. Release notes keep that shape.
    /// </summary>
    public static string Summary(string notes, AppLanguage language)
    {
        string text = notes.Replace("\r\n", "\n");
        if (language == AppLanguage.English)
        {
            var english = Regex.Match(text, @"\*\*In English:\*\*\s*(.+?)(\n\n|$)", RegexOptions.Singleline);
            if (english.Success) return Plain(english.Groups[1].Value);
        }
        int start = text.IndexOf("**Mới**", StringComparison.Ordinal);
        if (start < 0) start = text.IndexOf("**Sửa lỗi**", StringComparison.Ordinal);
        int end = text.IndexOf("**In English", StringComparison.Ordinal);
        if (start >= 0) text = end > start ? text[start..end] : text[start..];
        return Plain(text);
    }

    static string Plain(string markdown)
    {
        var lines = markdown.Split('\n')
            .Select(l => l.TrimEnd())
            .Select(l => Regex.Replace(l, @"\[([^\]]+)\]\([^)]+\)", "$1"))
            .Select(l => l.Replace("**", "").Replace("`", ""))
            .Select(l => Regex.Replace(l, @"^(\s*)[-*] ", "$1• "))
            .Select(l => l.TrimStart('#', ' ') is var t && l.StartsWith('#') ? t : l);
        string text = Regex.Replace(string.Join("\n", lines).Trim(), @"\n{3,}", "\n\n");
        return text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : text;
    }
}
