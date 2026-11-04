using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>How a Wi-Fi network asks to be joined.</summary>
public enum WifiSecurity { Open, Password, Enterprise }

public enum WifiStatus { Ready, NoAdapter, Denied }

public enum WifiResult { Connected, WrongKey, Failed }

/// <summary>A network in range, by name: what Windows reports for it, with its saved profile if it has one.</summary>
public sealed record WifiNetwork(string Ssid, int Bars, WifiSecurity Security, bool Connected, string? Profile, bool AutoConnect)
{
    /// <summary>Windows remembers this network (a saved profile), so it joins without asking for the key.</summary>
    public bool Saved => Profile != null;

    internal byte[] SsidBytes { get; init; } = [];
    internal int Auth { get; init; }
    internal int Cipher { get; init; }
    internal bool Encrypted { get; init; }
}

/// <summary>
/// The Wi-Fi networks around, through the WLAN service (wlanapi): scan, join with a key or a saved
/// profile, leave, forget. WinRT's WiFiAdapter refuses desktop apps that are not packaged.
/// Since Windows 11 24H2 the service lists networks only to apps allowed to use the location.
/// </summary>
public sealed class WifiNetworks
{
    const int AccessDenied = 5;
    const uint AcmSource = 0x8;
    const int ScanComplete = 7, ScanFailed = 8, ConnectionComplete = 10;
    const int Infrastructure = 1;
    const uint Connected = 0x1, HasProfile = 0x2;
    // WLAN_AVAILABLE_NETWORK: name[256] (WCHAR), DOT11_SSID (36), then 4-byte fields up to 628 bytes.
    const int NetworkSize = 628;

    IntPtr _handle;
    Guid _iface;
    readonly WlanNotify _notify;
    TaskCompletionSource<bool>? _scan;
    TaskCompletionSource<int>? _join;
    string? _joining;

    public WifiNetworks() => _notify = OnNotification;

    /// <summary>
    /// The WLAN service refused to list networks: location is off for desktop apps (by the user,
    /// or by a policy). Windows' own Wi-Fi list is exempt, so that is where to go instead.
    /// </summary>
    public bool Blocked { get; private set; }

    public Task<WifiStatus> OpenAsync() => Task.Run(Open);

    WifiStatus Open()
    {
        if (_handle != IntPtr.Zero) return WifiStatus.Ready;
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0) return WifiStatus.Denied;
        if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != 0)
        {
            WlanCloseHandle(handle, IntPtr.Zero);
            return WifiStatus.NoAdapter;
        }
        try
        {
            if (Marshal.ReadInt32(list) == 0)
            {
                WlanCloseHandle(handle, IntPtr.Zero);
                return WifiStatus.NoAdapter;
            }
            // WLAN_INTERFACE_INFO_LIST: two DWORDs, then the first interface, its GUID first.
            _iface = Marshal.PtrToStructure<Guid>(list + 8);
        }
        finally
        {
            WlanFreeMemory(list);
        }
        _handle = handle;
        WlanRegisterNotification(_handle, AcmSource, true, _notify, IntPtr.Zero, IntPtr.Zero, out _);
        return WifiStatus.Ready;
    }

    /// <summary>The service's notifications, on its own thread: scans finishing and joins completing.</summary>
    void OnNotification(IntPtr data, IntPtr context)
    {
        try
        {
            int code = Marshal.ReadInt32(data, 4);
            if (code is ScanComplete or ScanFailed)
            {
                _scan?.TrySetResult(code == ScanComplete);
                return;
            }
            if (code != ConnectionComplete || _join is not { } join) return;
            // WLAN_NOTIFICATION_DATA: source, code, interface GUID, data size, then (aligned) the data pointer.
            var payload = Marshal.ReadIntPtr(data, IntPtr.Size == 8 ? 32 : 28);
            // WLAN_CONNECTION_NOTIFICATION_DATA: mode, profile name[256], DOT11_SSID, BSS type, security, reason.
            string profile = Marshal.PtrToStringUni(payload + 4) ?? "";
            if (profile == _joining) join.TrySetResult(Marshal.ReadInt32(payload, 560));
        }
        catch (Exception ex)
        {
            Log.Error("Reading a Wi-Fi notification failed", ex);
        }
    }

    /// <summary>The networks Windows saw last, or after a fresh scan; the connected one first, then by signal.</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ListAsync(bool scan)
    {
        if (_handle == IntPtr.Zero) return [];
        if (scan)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _scan = done;
            var iface = _iface;
            int error = WlanScan(_handle, ref iface, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (error == AccessDenied)
            {
                Blocked = true;
                return [];
            }
            // Drivers have four seconds to finish a scan.
            if (error == 0) await Task.WhenAny(done.Task, Task.Delay(5000));
            _scan = null;
        }
        return await Task.Run(Read);
    }

    List<WifiNetwork> Read()
    {
        var iface = _iface;
        int error = WlanGetAvailableNetworkList(_handle, ref iface, 0, IntPtr.Zero, out var list);
        Blocked = error == AccessDenied;
        if (error != 0)
        {
            if (!Blocked) Log.Info($"Listing Wi-Fi networks failed: {error}");
            return [];
        }
        var seen = new List<(byte[] Ssid, string Profile, uint Flags, int Quality, bool Encrypted, int Auth, int Cipher)>();
        try
        {
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < count; i++)
            {
                var p = list + 8 + i * NetworkSize;
                int length = Math.Clamp(Marshal.ReadInt32(p, 512), 0, 32);
                // Hidden networks (no name) and ad hoc ones are not offered, as in Windows' list.
                if (length == 0 || Marshal.ReadInt32(p, 548) != Infrastructure) continue;
                var ssid = new byte[length];
                Marshal.Copy(p + 516, ssid, 0, length);
                seen.Add((ssid, Marshal.PtrToStringUni(p) ?? "", (uint)Marshal.ReadInt32(p, 620), Marshal.ReadInt32(p, 604),
                    Marshal.ReadInt32(p, 608) != 0, Marshal.ReadInt32(p, 612), Marshal.ReadInt32(p, 616)));
            }
        }
        finally
        {
            WlanFreeMemory(list);
        }

        // Windows lists a network once per saved profile and once without one; fold them by name.
        return seen
            .GroupBy(n => Convert.ToHexString(n.Ssid))
            .Select(g =>
            {
                var first = g.First();
                var saved = g.FirstOrDefault(n => (n.Flags & HasProfile) != 0 && n.Profile.Length > 0);
                string? profile = saved.Profile is { Length: > 0 } name ? name : null;
                var source = profile != null ? saved : first;
                return new WifiNetwork(Name(first.Ssid), Bars(g.Max(n => n.Quality)), Security(source.Auth, source.Encrypted),
                    g.Any(n => (n.Flags & Connected) != 0), profile, profile == null || AutoConnects(profile))
                {
                    SsidBytes = first.Ssid, Auth = source.Auth, Cipher = source.Cipher, Encrypted = source.Encrypted,
                };
            })
            .OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.Bars)
            .ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Joins the network: with its saved profile if <paramref name="key"/> is null, otherwise with a new
    /// profile holding the key. A profile that never managed to connect is not kept, as in Windows.
    /// </summary>
    public async Task<WifiResult> ConnectAsync(WifiNetwork network, bool automatic, string? key = null)
    {
        if (_handle == IntPtr.Zero) return WifiResult.Failed;
        var iface = _iface;
        string profile = network.Profile ?? network.Ssid;
        bool created = false;
        if (key != null || network.Profile == null)
        {
            if (network.Security == WifiSecurity.Enterprise) return WifiResult.Failed;
            int error = WlanSetProfile(_handle, ref iface, 0, ProfileXml(profile, network, key, automatic), null, true, IntPtr.Zero, out uint reason);
            if (error != 0)
            {
                Log.Info($"Wi-Fi '{network.Ssid}': profile refused ({error}, reason 0x{reason:X})");
                return key != null ? WifiResult.WrongKey : WifiResult.Failed;
            }
            created = network.Profile == null;
        }
        else if (network.AutoConnect != automatic)
        {
            SetAutoConnect(profile, automatic);
        }

        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _join = done;
        _joining = profile;
        var parameters = new ConnectionParameters { Mode = 0, Profile = profile, BssType = Infrastructure };
        int result = WlanConnect(_handle, ref iface, ref parameters, IntPtr.Zero);
        int code = result != 0 ? result : await Task.WhenAny(done.Task, Task.Delay(30000)) == done.Task ? done.Task.Result : -1;
        _join = null;
        _joining = null;
        Log.Info($"Wi-Fi '{network.Ssid}': {(code == 0 ? "connected" : $"not connected, reason 0x{code:X}")}");
        if (code == 0) return WifiResult.Connected;

        if (created) WlanDeleteProfile(_handle, ref iface, profile, IntPtr.Zero);
        // Security-module reasons (MSMSEC) on a network with a key mean the key was not accepted.
        bool keyRejected = network.Security == WifiSecurity.Password && code is >= 0x48000 and <= 0x4FFFF;
        return keyRejected ? WifiResult.WrongKey : WifiResult.Failed;
    }

    public void Disconnect()
    {
        if (_handle == IntPtr.Zero) return;
        var iface = _iface;
        int error = WlanDisconnect(_handle, ref iface, IntPtr.Zero);
        Log.Info($"Wi-Fi disconnect: {(error == 0 ? "done" : error.ToString())}");
    }

    /// <summary>Deletes the saved profile: Windows stops joining the network and forgets its key.</summary>
    public bool Forget(WifiNetwork network)
    {
        if (_handle == IntPtr.Zero || network.Profile is not { } profile) return false;
        var iface = _iface;
        bool ok = WlanDeleteProfile(_handle, ref iface, profile, IntPtr.Zero) == 0;
        Log.Info($"Forget Wi-Fi '{network.Ssid}': {(ok ? "done" : "failed")}");
        return ok;
    }

    static string Name(byte[] ssid)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(ssid);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(ssid);
        }
    }

    /// <summary>Signal quality (0-100) as the four bars Windows draws.</summary>
    static int Bars(int quality) => quality >= 80 ? 4 : quality >= 60 ? 3 : quality >= 35 ? 2 : 1;

    // DOT11_AUTH_ALGORITHM: 1 open, 2 shared key (WEP), 3 WPA, 4 WPA-PSK, 6 WPA2, 7 WPA2-PSK, 8 WPA3, 9 WPA3-SAE, 10 OWE, 11 WPA3 enterprise.
    static WifiSecurity Security(int auth, bool encrypted) => auth switch
    {
        1 when !encrypted => WifiSecurity.Open,
        10 => WifiSecurity.Open,
        3 or 6 or 8 or 11 => WifiSecurity.Enterprise,
        _ => WifiSecurity.Password,
    };

    bool AutoConnects(string profile) => ProfileOf(profile) is { } xml && Mode(xml)?.Value != "manual";

    void SetAutoConnect(string profile, bool automatic)
    {
        // The saved profile comes back with its key encrypted for this PC, which it accepts as it is.
        if (ProfileOf(profile) is not { } xml || Mode(xml) is not { } mode) return;
        mode.Value = automatic ? "auto" : "manual";
        var iface = _iface;
        WlanSetProfile(_handle, ref iface, 0, xml.ToString(SaveOptions.DisableFormatting), null, true, IntPtr.Zero, out _);
    }

    XElement? ProfileOf(string profile)
    {
        var iface = _iface;
        uint flags = 0;
        if (WlanGetProfile(_handle, ref iface, profile, IntPtr.Zero, out var text, ref flags, out _) != 0) return null;
        try
        {
            return XElement.Parse(Marshal.PtrToStringUni(text) ?? "");
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            WlanFreeMemory(text);
        }
    }

    static XElement? Mode(XElement profile) => profile.Element(profile.Name.Namespace + "connectionMode");

    /// <summary>A WLAN profile for a network with a key (or none), the way Windows writes one when you join.</summary>
    static string ProfileXml(string name, WifiNetwork n, string? key, bool automatic)
    {
        // DOT11_CIPHER_ALGORITHM: 2 TKIP; everything else current is AES (CCMP/GCMP); 1, 5 and 0x101 are WEP.
        string cipher = n.Cipher == 2 ? "TKIP" : "AES";
        var (authentication, encryption) = n.Auth switch
        {
            1 => ("open", n.Encrypted ? "WEP" : "none"),
            2 => ("shared", "WEP"),
            4 => ("WPAPSK", cipher),
            9 => ("WPA3SAE", "AES"),
            10 => ("OWE", "AES"),
            _ => ("WPA2PSK", cipher),
        };
        XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
        var security = new XElement(ns + "security",
            new XElement(ns + "authEncryption",
                new XElement(ns + "authentication", authentication),
                new XElement(ns + "encryption", encryption),
                new XElement(ns + "useOneX", "false")));
        if (key != null && encryption != "none" && authentication != "OWE")
        {
            security.Add(new XElement(ns + "sharedKey",
                new XElement(ns + "keyType", encryption == "WEP" ? "networkKey" : "passPhrase"),
                new XElement(ns + "protected", "false"),
                new XElement(ns + "keyMaterial", key)));
        }
        return new XElement(ns + "WLANProfile",
            new XElement(ns + "name", name),
            new XElement(ns + "SSIDConfig", new XElement(ns + "SSID",
                new XElement(ns + "hex", Convert.ToHexString(n.SsidBytes)),
                new XElement(ns + "name", n.Ssid))),
            new XElement(ns + "connectionType", "ESS"),
            new XElement(ns + "connectionMode", automatic ? "auto" : "manual"),
            new XElement(ns + "MSM", security)).ToString(SaveOptions.DisableFormatting);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void WlanNotify(IntPtr data, IntPtr context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ConnectionParameters
    {
        public int Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid;
        public IntPtr Bssids;
        public int BssType;
        public int Flags;
    }

    [DllImport("wlanapi.dll")]
    static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    static extern int WlanRegisterNotification(IntPtr handle, uint source, bool ignoreDuplicate, WlanNotify callback,
        IntPtr context, IntPtr reserved, out uint previous);

    [DllImport("wlanapi.dll")]
    static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    static extern int WlanGetProfile(IntPtr handle, ref Guid iface, string name, IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    static extern int WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, string xml, string? security, bool overwrite,
        IntPtr reserved, out uint reasonCode);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    static extern int WlanDeleteProfile(IntPtr handle, ref Guid iface, string name, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    static extern int WlanConnect(IntPtr handle, ref Guid iface, ref ConnectionParameters parameters, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    static extern int WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    static extern void WlanFreeMemory(IntPtr memory);
}
