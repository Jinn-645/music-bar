using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace MusicBar.Services;

public sealed record NotificationItem(uint Id, string AppName, string AppId, ImageSource? Logo, string Title, string Body, DateTimeOffset Time);

/// <summary>
/// The notifications waiting in the Windows notification center, read through
/// UserNotificationListener. Polled (the change event is not available to a
/// desktop app): often while the control center is open, slowly for the badge.
/// </summary>
public sealed class NotificationFeed
{
    static readonly TimeSpan FastPoll = TimeSpan.FromSeconds(2);
    static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(8);

    readonly DispatcherTimer _poll = new() { Interval = SlowPoll };
    readonly Dictionary<string, ImageSource?> _logos = new();
    readonly HashSet<uint> _seen = new();
    UserNotificationListener? _listener;
    bool _refreshing;
    string _signature = "";

    public NotificationFeed()
    {
        _poll.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Windows lets MusicBar read notifications.</summary>
    public bool Available { get; private set; }

    public IReadOnlyList<NotificationItem> Items { get; private set; } = [];

    /// <summary>Notifications that arrived since the control center was last opened.</summary>
    public int Unseen => Items.Count(i => !_seen.Contains(i.Id));

    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = _listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified) access = await _listener.RequestAccessAsync();
            Available = access == UserNotificationListenerAccessStatus.Allowed;
            Log.Info($"Notification access: {access}");
        }
        catch (Exception ex)
        {
            Available = false;
            Log.Error("Notification listener unavailable", ex);
        }
        if (!Available) return;

        // What is already there when MusicBar starts is not news.
        await RefreshAsync();
        foreach (var item in Items) _seen.Add(item.Id);
        Changed?.Invoke();
        _poll.Start();
    }

    public void Stop() => _poll.Stop();

    public void SetFastPolling(bool fast) => _poll.Interval = fast ? FastPoll : SlowPoll;

    public void MarkAllSeen()
    {
        foreach (var item in Items) _seen.Add(item.Id);
        Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        if (_listener == null || !Available || _refreshing) return;
        _refreshing = true;
        try
        {
            var raw = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<NotificationItem>(raw.Count);
            foreach (var n in raw.OrderByDescending(n => n.CreationTime))
            {
                var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                string title = texts?.FirstOrDefault()?.Text?.Trim() ?? "";
                string body = texts == null ? "" : string.Join("\n", texts.Skip(1).Select(t => t.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t)));
                string appName = "", appId = "";
                try
                {
                    appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                    appId = n.AppInfo?.AppUserModelId ?? "";
                }
                catch (Exception)
                {
                }
                items.Add(new NotificationItem(n.Id, appName, appId, await LogoAsync(n), title, body, n.CreationTime));
            }

            string signature = string.Join(",", items.Select(i => i.Id));
            Items = items;
            if (signature != _signature)
            {
                _signature = signature;
                _seen.IntersectWith(items.Select(i => i.Id));
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Reading notifications failed", ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dismiss(uint id)
    {
        try
        {
            _listener?.RemoveNotification(id);
        }
        catch (Exception ex)
        {
            Log.Error("Dismissing a notification failed", ex);
        }
        Items = Items.Where(i => i.Id != id).ToList();
        _signature = string.Join(",", Items.Select(i => i.Id));
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        try
        {
            _listener?.ClearNotifications();
        }
        catch (Exception ex)
        {
            Log.Error("Clearing notifications failed", ex);
        }
        Items = [];
        _signature = "";
        Changed?.Invoke();
    }

    /// <summary>Opens the app that sent the notification and takes the notification away, as Windows does.</summary>
    public void Open(NotificationItem item)
    {
        if (item.AppId.Length > 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.AppId}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"Opening {item.AppId} failed", ex);
            }
        }
        Dismiss(item.Id);
    }

    async Task<ImageSource?> LogoAsync(UserNotification n)
    {
        string key;
        try
        {
            key = n.AppInfo?.AppUserModelId ?? "";
        }
        catch (Exception)
        {
            return null;
        }
        if (_logos.TryGetValue(key, out var cached)) return cached;

        ImageSource? logo = null;
        try
        {
            var reference = n.AppInfo!.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
            using var stream = await reference.OpenReadAsync();
            using var net = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = net;
            bitmap.DecodePixelWidth = 32;
            bitmap.EndInit();
            bitmap.Freeze();
            logo = bitmap;
        }
        catch (Exception)
        {
        }
        _logos[key] = logo;
        return logo;
    }
}
