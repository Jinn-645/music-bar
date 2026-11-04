using System;
using System.Windows;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace MusicBar.Views;

/// <summary>Notification-area icon: left click opens settings, right click shows the bar menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action onOpen, Action onMenu)
    {
        _icon = new WinForms.NotifyIcon
        {
            Text = "MusicBar",
            Icon = LoadIcon(),
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) onOpen();
            else if (e.Button == WinForms.MouseButtons.Right) onMenu();
        };
    }

    static Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MusicBar.ico"))!;
        using var stream = resource.Stream;
        return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..62] + "…" : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
