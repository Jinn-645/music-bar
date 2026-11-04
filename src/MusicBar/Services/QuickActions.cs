using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using MusicBar.Infrastructure;
using MusicBar.Interop;
using Windows.Devices.Radios;

namespace MusicBar.Services;

/// <summary>A list the control center slides out beside itself.</summary>
public enum QuickPanel { None, Project, Cast }

/// <summary>
/// A tile in the control center: a switch when <see cref="IsOn"/> is set and
/// <see cref="Run"/> flips it, a list when <see cref="Panel"/> is set, otherwise a shortcut.
/// </summary>
public sealed class QuickAction
{
    public required string Glyph { get; init; }
    public required string Label { get; init; }
    public Func<bool>? IsOn { get; init; }
    public Func<Task>? Run { get; init; }
    public QuickPanel Panel { get; init; }
}

/// <summary>
/// The Windows quick settings MusicBar can offer itself. Real switches where
/// Windows allows it (radios, Night light, Live captions); the rest open the
/// matching Windows page or flyout directly.
/// </summary>
public static class QuickActions
{
    public static async Task<IReadOnlyList<QuickAction>> ListAsync()
    {
        var actions = new List<QuickAction>();
        foreach (var radio in await RadiosAsync())
        {
            var r = radio;
            actions.Add(new QuickAction
            {
                Glyph = r.Kind == RadioKind.Bluetooth ? "" : "",
                Label = r.Kind == RadioKind.Bluetooth ? "Bluetooth" : "Wi-Fi",
                IsOn = () => r.State == RadioState.On,
                Run = async () => await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On),
            });
        }
        if (NightLight.IsOn != null)
        {
            actions.Add(new QuickAction
            {
                Glyph = "", Label = "Ánh sáng đêm",
                IsOn = () => NightLight.IsOn == true,
                Run = () => Task.FromResult(NightLight.Set(NightLight.IsOn != true)),
            });
        }
        actions.Add(new QuickAction
        {
            Glyph = "", Label = "Phụ đề trực tiếp",
            IsOn = () => Process.GetProcessesByName("LiveCaptions").Length > 0,
            Run = () =>
            {
                Keys(0x5B, 0x11, 0x4C); // Win+Ctrl+L
                return Task.Delay(700);
            },
        });
        actions.Add(Open("", "Tiết kiệm năng lượng", "ms-settings:powersleep"));
        actions.Add(Open("", "Chia sẻ lân cận", "ms-settings:crossdevice"));
        actions.Add(new QuickAction { Glyph = "", Label = "Màn hình chiếu", Panel = QuickPanel.Project });
        actions.Add(new QuickAction { Glyph = "", Label = "Truyền màn hình", Panel = QuickPanel.Cast, IsOn = () => WirelessDisplays.Connected });
        actions.Add(Open("", "Trợ năng", "ms-settings:easeofaccess"));
        return actions;
    }

    static QuickAction Open(string glyph, string label, string uri) => new()
    {
        Glyph = glyph,
        Label = label,
        Run = () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"Opening {uri} failed", ex);
            }
            return Task.CompletedTask;
        },
    };

    static async Task<IReadOnlyList<Radio>> RadiosAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return [];
            return (await Radio.GetRadiosAsync())
                .Where(r => r.Kind is RadioKind.WiFi or RadioKind.Bluetooth)
                .OrderBy(r => r.Kind == RadioKind.Bluetooth)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Reading radios failed", ex);
            return [];
        }
    }

    /// <summary>Presses the keys together, like a shortcut typed by hand.</summary>
    static void Keys(params byte[] keys)
    {
        foreach (var k in keys) Native.keybd_event(k, 0, 0, UIntPtr.Zero);
        foreach (var k in keys.Reverse()) Native.keybd_event(k, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
    }
}

/// <summary>
/// Night light on/off. Windows keeps it in an undocumented CloudStore value, a Bond
/// CompactBinary v1 record; the layout follows the reverse-engineered description in
/// kvnxiao/win-nightlight-cli. Switching writes only the "state" value, the way the
/// Windows switch does, and refuses to write if the value holds anything unexpected.
/// </summary>
public static class NightLight
{
    const string Store = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\";
    const string StateValue = @"default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    /// <summary>On, off, or null when the value cannot be read safely.</summary>
    public static bool? IsOn => ReadState() is { } s ? s.On : null;

    public static bool Set(bool on)
    {
        try
        {
            if (ReadState() is not { } state || !state.Writable) return false;
            if (state.On == on) return true;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Stamp above the previous write and Windows' own record, or Windows reverts it.
            ulong stamp = Math.Max((ulong)now, Math.Max(state.Timestamp, CloudTimestamp()) + 2);

            var inner = new List<byte> { 0x43, 0x42, 0x01, 0x00 };
            if (on) inner.AddRange([0x10, 0x00]);                 // field 0 (int32 0): on now
            inner.AddRange([0xD0, 0x0A, 0x02]);                     // field 10 (int32 1): changed by hand
            inner.AddRange([0xC6, 0x14]);                           // field 20 (uint64): when
            Varint(inner, (ulong)DateTime.UtcNow.ToFileTimeUtc());
            if (state.Usable == false) inner.AddRange([0xC2, 0x1E, 0x00]); // field 30 (bool false)
            inner.Add(0x00);

            var data = new List<byte> { 0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06 };
            Varint(data, stamp);
            data.AddRange([0x2A, 0x2B, 0x0E]);
            Varint(data, (ulong)inner.Count);
            data.AddRange(inner);
            data.AddRange([0x00, 0x00, 0x00]);

            using var key = Registry.CurrentUser.OpenSubKey(Store + @"Current\" + StateValue, writable: true);
            if (key == null) return false;
            key.SetValue("Data", data.ToArray(), RegistryValueKind.Binary);
            Log.Info($"Night light {(on ? "on" : "off")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Switching Night light failed", ex);
            return false;
        }
    }

    sealed record State(ulong Timestamp, bool On, bool? Usable, bool Writable);

    static State? ReadState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Store + @"Current\" + StateValue);
            if (key?.GetValue("Data") is not byte[] data) return null;
            var r = new Reader(data);
            if (!r.Header()) return null;
            ulong stamp = 0;
            byte[]? payload = null;
            // Outer envelope: field 0 metadata, field 1 { 0: timestamp, 1: { 1: list<int8> payload } }.
            while (r.Field(out int id, out int type))
            {
                if (id == 1 && type == Reader.Struct)
                {
                    while (r.Field(out int cid, out int ctype))
                    {
                        if (cid == 0 && ctype == Reader.UInt64) stamp = r.Varint();
                        else if (cid == 1 && ctype == Reader.Struct)
                        {
                            while (r.Field(out int wid, out int wtype))
                            {
                                if (wid == 1 && wtype == Reader.List) payload = r.ByteList();
                                else r.Skip(wtype);
                            }
                        }
                        else r.Skip(ctype);
                    }
                }
                else r.Skip(type);
            }
            // Never switched yet: an empty payload means off.
            if (payload == null) return new State(stamp, false, null, true);

            var p = new Reader(payload);
            if (!p.Header()) return null;
            bool on = false, writable = true;
            bool? usable = null;
            while (p.Field(out int id, out int type))
            {
                switch (id)
                {
                    case 0 when type == Reader.Int32: p.Varint(); on = true; break;
                    case 10 when type == Reader.Int32: p.Varint(); break;
                    case 20 when type == Reader.UInt64: p.Varint(); break;
                    case 30 when type == Reader.Bool: usable = p.Byte() != 0; break;
                    default: p.Skip(type); writable = false; break;
                }
            }
            return new State(stamp, on, usable, writable);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The timestamp Windows records for its own last state write (0 when there is none).</summary>
    static ulong CloudTimestamp()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Store + @"Cloud\" + StateValue);
            if (key?.GetValue("Data") is not byte[] data) return 0;
            var r = new Reader(data);
            if (!r.Header()) return 0;
            while (r.Field(out int id, out int type))
            {
                if (id == 1 && type == Reader.UInt64) return r.Varint();
                r.Skip(type);
            }
        }
        catch (Exception)
        {
        }
        return 0;
    }

    static void Varint(List<byte> to, ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            to.Add(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);
    }

    /// <summary>Just enough of Bond CompactBinary v1 to read these two records.</summary>
    sealed class Reader(byte[] data)
    {
        public const int Bool = 2, UInt64 = 6, Struct = 10, List = 11, Int8 = 14, Int16 = 15, Int32 = 16;
        int _pos;

        public bool Header()
        {
            if (data.Length < 4 || data[0] != 0x43 || data[1] != 0x42 || data[2] != 0x01 || data[3] != 0x00) return false;
            _pos = 4;
            return true;
        }

        public byte Byte() => _pos < data.Length ? data[_pos++] : throw new FormatException("Truncated record");

        /// <summary>Reads a field header; false at the end of the current struct.</summary>
        public bool Field(out int id, out int type)
        {
            byte b = Byte();
            type = b & 0x1F;
            id = b >> 5;
            if (id == 6) id = Byte();
            else if (id == 7) id = Byte() | (Byte() << 8);
            return type != 0;
        }

        public ulong Varint()
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                byte b = Byte();
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new FormatException("Bad varint");
        }

        public byte[] ByteList()
        {
            int element = Byte();
            if (element != Int8) throw new FormatException("Unexpected list");
            int count = (int)Varint();
            if (_pos + count > data.Length) throw new FormatException("Truncated list");
            var bytes = data.AsSpan(_pos, count).ToArray();
            _pos += count;
            return bytes;
        }

        public void Skip(int type)
        {
            switch (type)
            {
                case Bool or 3 or Int8: Byte(); break;
                case 4 or 5 or UInt64 or Int16 or Int32 or 17: Varint(); break;
                case Struct:
                    while (Field(out _, out int t)) Skip(t);
                    break;
                case List:
                    int element = Byte();
                    int count = (int)Varint();
                    for (int i = 0; i < count; i++) Skip(element);
                    break;
                default: throw new FormatException($"Unsupported field type {type}");
            }
        }
    }
}
