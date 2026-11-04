using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>
/// Folds the volume icon and the notification button of the Windows 10 style
/// tray (Windows 10, ExplorerPatcher) into one entry button that opens the
/// MusicBar control center, and puts everything back when MusicBar stops.
/// <list type="bullet">
/// <item>The volume icon is Explorer's own notify icon; hiding it with NIS_HIDDEN
/// makes the tray reflow by itself.</item>
/// <item>The notification button is a child window; once hidden, Explorer still
/// keeps its slot, so the remaining tray items are slid right to close the gap.
/// A WM_SIZE on the tray makes Explorer lay everything out again.</item>
/// </list>
/// </summary>
internal sealed class TrayMerge
{
    /// <summary>Explorer's system volume icon (the network icon is ...AE74).</summary>
    static readonly Guid VolumeIcon = new("7820AE73-23E3-4229-82C1-E41CB67D5B9C");
    static readonly TimeSpan VolumeRecheck = TimeSpan.FromSeconds(30);

    // What is currently changed, kept static so a crash handler can undo it from any thread.
    static readonly object Gate = new();
    static IntPtr _hiddenBell;
    static IntPtr _packedTray;
    static bool _volumeHidden;

    IntPtr _notify;
    DateTime _volumeAt = DateTime.MinValue;

    /// <summary>The button that opens the control center: Control Center when present, else the notification button.</summary>
    public IntPtr Entry { get; private set; }

    public Native.RECT EntryRect { get; private set; }

    /// <summary>The notification button was folded away (only when a separate Control Center button exists).</summary>
    public bool BellFolded => _hiddenBell != IntPtr.Zero;

    /// <summary>
    /// Brings the tray into the merged state; cheap enough to call on every tick.
    /// Returns true when it moved tray items, so the free-space scan can be refreshed.
    /// </summary>
    public bool Apply(IntPtr tray)
    {
        var notify = tray == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != _notify)
        {
            // New Explorer (or first run): everything has to be done again.
            _notify = notify;
            _volumeAt = DateTime.MinValue;
            lock (Gate)
            {
                _hiddenBell = IntPtr.Zero;
                _packedTray = IntPtr.Zero;
            }
        }
        Entry = IntPtr.Zero;
        if (notify == IntPtr.Zero) return false;

        var controlCenter = Native.FindWindowEx(notify, IntPtr.Zero, "ControlCenterButton", null);
        var bell = Native.FindWindowEx(notify, IntPtr.Zero, "TrayButton", null);
        Entry = controlCenter != IntPtr.Zero && Native.IsWindowVisible(controlCenter) ? controlCenter : bell;
        if (Entry == IntPtr.Zero) return false;
        if (Native.GetWindowRect(Entry, out var entryRect)) EntryRect = entryRect;

        if (DateTime.Now - _volumeAt > VolumeRecheck)
        {
            _volumeAt = DateTime.Now;
            if (SetVolumeIconHidden(true)) lock (Gate) _volumeHidden = true;
        }

        bool moved = false;
        if (Entry == controlCenter && bell != IntPtr.Zero)
        {
            if (Native.IsWindowVisible(bell))
            {
                Native.ShowWindow(bell, Native.SW_HIDE);
                lock (Gate) _hiddenBell = bell;
                Log.Info("Tray: notification button folded into the control center");
            }
            lock (Gate) _packedTray = notify;
            moved = Pack(notify);
            if (moved)
            {
                Log.Info("Tray: items slid over the notification button's slot");
                if (Native.GetWindowRect(Entry, out entryRect)) EntryRect = entryRect;
            }
        }
        return moved;
    }

    /// <summary>Restores the tray; the next <see cref="Apply"/> starts from scratch.</summary>
    public void Release()
    {
        Restore();
        _volumeAt = DateTime.MinValue;
    }

    /// <summary>Puts the tray back exactly as Explorer laid it out.</summary>
    public static void Restore()
    {
        IntPtr bell, notify;
        bool volume;
        lock (Gate)
        {
            bell = _hiddenBell;
            notify = _packedTray;
            volume = _volumeHidden;
            _hiddenBell = _packedTray = IntPtr.Zero;
            _volumeHidden = false;
        }
        try
        {
            if (bell != IntPtr.Zero && Native.IsWindow(bell)) Native.ShowWindow(bell, Native.SW_SHOWNA);
            if (notify != IntPtr.Zero && Native.IsWindow(notify)) Relayout(notify);
            if (volume) SetVolumeIconHidden(false);
        }
        catch (Exception ex)
        {
            Log.Error("Restoring the tray failed", ex);
        }
    }

    /// <summary>
    /// For a start after a crash: shows the notification button and the volume icon
    /// if an earlier run left them hidden. Harmless when nothing is hidden.
    /// </summary>
    public static void RecoverLeftovers(IntPtr tray)
    {
        var notify = tray == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify == IntPtr.Zero) return;
        var controlCenter = Native.FindWindowEx(notify, IntPtr.Zero, "ControlCenterButton", null);
        var bell = Native.FindWindowEx(notify, IntPtr.Zero, "TrayButton", null);
        if (controlCenter != IntPtr.Zero && bell != IntPtr.Zero && !Native.IsWindowVisible(bell))
        {
            Native.ShowWindow(bell, Native.SW_SHOWNA);
            Relayout(notify);
            Log.Info("Tray: showed the notification button an earlier run left hidden");
        }
        SetVolumeIconHidden(false);
    }

    /// <summary>
    /// Slides the visible tray items right so they sit flush against the
    /// show-desktop strip, closing the slot of the hidden notification button.
    /// </summary>
    static bool Pack(IntPtr notify)
    {
        if (!Native.GetWindowRect(notify, out var area)) return false;
        var items = new List<(IntPtr Hwnd, string Class, Native.RECT Rect)>();
        for (var h = Native.GetWindow(notify, Native.GW_CHILD); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDNEXT))
        {
            if (Native.IsWindowVisible(h) && Native.GetWindowRect(h, out var r) && r.Width > 0) items.Add((h, Native.ClassName(h), r));
        }

        var desktop = items.FirstOrDefault(i => i.Class == "TrayShowDesktopButtonWClass");
        int right = desktop.Hwnd != IntPtr.Zero ? desktop.Rect.Left : area.Right;
        bool moved = false;
        foreach (var item in items.Where(i => i.Hwnd != desktop.Hwnd).OrderByDescending(i => i.Rect.Left))
        {
            int left = right - item.Rect.Width;
            if (item.Rect.Left != left)
            {
                var p = new Native.POINT { X = left, Y = item.Rect.Top };
                Native.ScreenToClient(notify, ref p);
                Native.SetWindowPos(item.Hwnd, IntPtr.Zero, p.X, p.Y, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                moved = true;
            }
            right = left;
        }
        return moved;
    }

    static void Relayout(IntPtr notify)
    {
        if (!Native.GetClientRect(notify, out var c)) return;
        Native.SendMessage(notify, Native.WM_SIZE, IntPtr.Zero, (IntPtr)((c.Height << 16) | (c.Width & 0xFFFF)));
    }

    static bool SetVolumeIconHidden(bool hidden)
    {
        var owner = Native.FindWindow("SystemTray_Main", null);
        if (owner == IntPtr.Zero) return false;
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = owner,
            uFlags = NIF_STATE | NIF_GUID,
            guidItem = VolumeIcon,
            dwState = hidden ? NIS_HIDDEN : 0,
            dwStateMask = NIS_HIDDEN,
        };
        return Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    const uint NIM_MODIFY = 1;
    const uint NIF_STATE = 0x08;
    const uint NIF_GUID = 0x20;
    const uint NIS_HIDDEN = 0x01;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
}
