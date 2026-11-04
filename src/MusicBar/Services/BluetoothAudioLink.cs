using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>
/// Connects and disconnects paired Bluetooth headphones and speakers. Windows has no public
/// API for it; the Bluetooth audio drivers instead take the one-shot KSPROPSETID_BtAudio
/// properties on their KS filter (the technique ToothTray uses). The filter is reached from
/// the device's audio endpoints: endpoint, its topology, connector 0, the driver's filter.
/// Callers run this on a thread-pool thread, which the CLR has already put in the MTA; the
/// MMDevice and DeviceTopology objects are free-threaded, so no further COM setup is needed.
/// </summary>
public static class BluetoothAudioLink
{
    const int Render = 0;
    const int Capture = 1;
    // A paired headset that is switched off shows as "unplugged", on some drivers as "not present".
    const int ActiveOrUnplugged = 0x1 | 0x4 | 0x8;
    const int AllContexts = 23;               // CLSCTX_ALL
    const int InprocServer = 1;               // CLSCTX_INPROC_SERVER
    const uint PropertyGet = 1;               // KSPROPERTY_TYPE_GET
    const uint OneShotReconnect = 0;
    const uint OneShotDisconnect = 1;
    const uint LocatePhantom = 1;             // CM_LOCATE_DEVNODE_PHANTOM
    static readonly Guid BtAudioSet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    static readonly Guid MachineContainer = new("00000000-0000-0000-ffff-ffffffffffff"); // every built-in device
    static readonly Type EnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!;

    /// <summary>
    /// True when this Bluetooth device (address in any common form: "00:11:22:AA:BB:CC", "001122aabbcc",
    /// or the AEP id "Bluetooth#Bluetooth00:00:00:00:00:00-00:11:22:aa:bb:cc") has audio endpoints
    /// MusicBar can connect or disconnect.
    /// </summary>
    public static bool IsAudio(string address)
    {
        try
        {
            return Normalize(address) is { } wanted && Visit(wanted, _ => 0, firstOnly: true).Accepted > 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Checking Bluetooth audio for {address} failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Asks the Bluetooth audio driver to connect (true) or disconnect (false) every audio endpoint
    /// of the device (headphones usually expose two: stereo A2DP and hands-free). Returns true if
    /// at least one endpoint accepted.
    /// </summary>
    public static bool SetConnected(string address, bool connect)
    {
        string verb = connect ? "connect" : "disconnect";
        try
        {
            if (Normalize(address) is not { } wanted)
            {
                Log.Info($"Bluetooth audio {verb} skipped: '{address}' is not a Bluetooth address");
                return false;
            }
            var outcome = Visit(wanted, control =>
            {
                var property = new KsIdentifier { Set = BtAudioSet, Id = connect ? OneShotReconnect : OneShotDisconnect, Flags = PropertyGet };
                return control.KsProperty(ref property, (uint)Marshal.SizeOf<KsIdentifier>(), IntPtr.Zero, 0, out _);
            }, firstOnly: false);
            Log.Info($"Bluetooth audio {verb} {wanted}: {outcome.Endpoints} endpoint(s) on {outcome.Filters} driver filter(s), {outcome.Accepted} accepted"
                     + (outcome.LastError != 0 ? $", last error 0x{outcome.LastError:X8}" : ""));
            return outcome.Accepted > 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Bluetooth audio {verb} for {address} failed", ex);
            return false;
        }
    }

    /// <summary>"00:11:22:AA:BB:CC", "001122aabbcc" or an AEP id ending in the address → "001122AABBCC".</summary>
    static string? Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        string digits = address.Trim().Replace(":", "").Replace("-", "");
        if (digits.Length < 12) return null;
        string tail = digits[^12..];
        return tail.All(char.IsAsciiHexDigit) ? tail.ToUpperInvariant() : null;
    }

    // ------------------------------------------------------------------ endpoints

    /// <summary>A Bluetooth audio endpoint, with the COM objects needed to reach its filter again.</summary>
    sealed record Endpoint(string FilterId, Guid Container, bool ByAddress, object Topology, IConnector Connector);

    readonly record struct Outcome(int Endpoints, int Filters, int Accepted, int LastError);

    /// <summary>
    /// Hands the KS control of each driver behind the device's endpoints to <paramref name="send"/>,
    /// once per driver; its HRESULT decides "accepted".
    /// </summary>
    static Outcome Visit(string address, Func<IKsControl, int> send, bool firstOnly)
    {
        // The enumerator is shared with AudioOutputs and AudioMeter (same wrapper): never released here.
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(EnumeratorType)!;
        var endpoints = new List<Endpoint>();
        try
        {
            // Render first, so a driver that also has a mic (hands-free) is reached through its
            // speaker filter, as ToothTray does; capture still covers a device with only a mic.
            Collect(enumerator, Render, address, endpoints);
            Collect(enumerator, Capture, address, endpoints);

            // An endpoint the address does not reach still belongs to the device when Windows put
            // it in the same container as one it does (that is how ToothTray groups a headset).
            var containers = endpoints.Where(e => e.ByAddress && e.Container != Guid.Empty && e.Container != MachineContainer)
                                      .Select(e => e.Container).ToHashSet();
            var mine = endpoints.Where(e => e.ByAddress || containers.Contains(e.Container)).ToList();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int filters = 0, accepted = 0, lastError = 0;
            foreach (var endpoint in mine)
            {
                if (!seen.Add(DriverOf(endpoint.FilterId))) continue;
                filters++;
                var control = Control(enumerator, endpoint, out int hr);
                if (control == null)
                {
                    lastError = hr != 0 ? hr : unchecked((int)0x80004002) /* E_NOINTERFACE */;
                    continue;
                }
                try
                {
                    hr = send(control);
                    if (hr >= 0) accepted++;
                    else lastError = hr;
                }
                finally
                {
                    Release(control);
                }
                if (firstOnly && accepted > 0) break;
            }
            return new Outcome(mine.Count, filters, accepted, lastError);
        }
        finally
        {
            foreach (var endpoint in endpoints)
            {
                Release(endpoint.Connector);
                Release(endpoint.Topology);
            }
        }
    }

    /// <summary>Every Bluetooth audio endpoint (of any device), marked by whether the address reaches it.</summary>
    static void Collect(IMMDeviceEnumerator enumerator, int flow, string address, List<Endpoint> into)
    {
        if (enumerator.EnumAudioEndpoints(flow, ActiveOrUnplugged, out var devices) != 0) return;
        try
        {
            if (devices.GetCount(out uint count) != 0) return;
            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                try
                {
                    if (Inspect(device, address) is { } endpoint) into.Add(endpoint);
                }
                catch (Exception)
                {
                    // An endpoint that goes away mid-walk is simply not a candidate.
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(devices);
        }
    }

    /// <summary>The endpoint's driver filter, when that filter is a Bluetooth one (bthenum, bthhfenum, ...).</summary>
    static Endpoint? Inspect(IMMDevice device, string address)
    {
        var iid = typeof(IDeviceTopology).GUID;
        if (device.Activate(ref iid, AllContexts, IntPtr.Zero, out var topologyObj) != 0) return null;
        IConnector? connector = null;
        bool keep = false;
        try
        {
            if (topologyObj is not IDeviceTopology topology || topology.GetConnector(0, out connector) != 0) return null;
            // "{2}.\\?\bthenum#{0000110b-…}_vid&…&0&001122aabbcc_c00000000#{6994ad04-…}\…"
            if (connector.GetDeviceIdConnectedTo(out string filterId) != 0
                || filterId == null
                || filterId.IndexOf(@"\\?\bth", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var endpoint = new Endpoint(filterId, ContainerOf(device), BelongsTo(filterId, address), topologyObj, connector);
            keep = true;
            return endpoint;
        }
        finally
        {
            if (!keep)
            {
                Release(connector);
                Release(topologyObj);
            }
        }
    }

    /// <summary>
    /// The filter's KS control: through the filter's own device, as the Windows SDK sample and
    /// ToothTray do. The connector part is only a fallback: IPart.Activate answered E_NOINTERFACE
    /// for IKsControl on the (HD Audio) filters it was tried on.
    /// </summary>
    static IKsControl? Control(IMMDeviceEnumerator enumerator, Endpoint endpoint, out int hr)
    {
        var iid = typeof(IKsControl).GUID;
        IMMDevice? filter = null;
        try
        {
            hr = enumerator.GetDevice(endpoint.FilterId, out filter);
            if (hr == 0)
            {
                hr = filter.Activate(ref iid, AllContexts, IntPtr.Zero, out var control);
                if (hr == 0 && control is IKsControl ks) return ks;
                Release(control);
            }
        }
        finally
        {
            Release(filter);
        }

        IConnector? other = null;
        try
        {
            hr = endpoint.Connector.GetConnectedTo(out other);
            if (hr == 0 && other is IPart part)
            {
                hr = part.Activate(InprocServer, ref iid, out var control);
                if (hr == 0 && control is IKsControl ks) return ks;
                Release(control);
            }
        }
        finally
        {
            Release(other);
        }
        return null;
    }

    static Guid ContainerOf(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return Guid.Empty;
        var key = ContainerId;
        var value = new PropVariant();
        try
        {
            return store.GetValue(ref key, ref value) == 0 && value.Type == 72 /* VT_CLSID */ && value.Pointer != IntPtr.Zero
                ? Marshal.PtrToStructure<Guid>(value.Pointer)
                : Guid.Empty;
        }
        finally
        {
            PropVariantClear(ref value);
            Release(store);
        }
    }

    static void Release(object? com)
    {
        // One release per wrapper we received: the same wrapper may be shared with other code.
        if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    // ------------------------------------------------------------------ address matching

    /// <summary>
    /// True when the filter's id, or the instance id of one of its device's two nearest parents,
    /// carries the address. The stereo (A2DP) filter has it in its own id; the hands-free one
    /// (BTHHFENUM\BthHFPAudio\…) only through its parent, the headset's BTHENUM hands-free service.
    /// </summary>
    static bool BelongsTo(string filterId, string address)
    {
        if (HasAddress(filterId, address)) return true;
        if (InstanceIdOf(filterId) is not { } instance || CM_Locate_DevNodeW(out uint node, instance, LocatePhantom) != 0) return false;
        for (int level = 0; level < 2; level++)
        {
            if (CM_Get_Parent(out uint parent, node, 0) != 0) return false;
            if (DeviceIdOf(parent) is { } id && HasAddress(id, address)) return true;
            node = parent;
        }
        return false;
    }

    /// <summary>
    /// The 12 hex digits as a token of their own, any case: not inside a longer hex run, and not
    /// the last group of a GUID (the ids also carry the service and interface class GUIDs).
    /// </summary>
    static bool HasAddress(string text, string address)
    {
        for (int at = text.IndexOf(address, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = text.IndexOf(address, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            int end = at + address.Length;
            bool joinedBefore = at > 0 && (char.IsAsciiHexDigit(text[at - 1]) || text[at - 1] == '-');
            bool joinedAfter = end < text.Length && char.IsAsciiHexDigit(text[end]);
            if (!joinedBefore && !joinedAfter) return true;
        }
        return false;
    }

    /// <summary>
    /// The filter id without its reference string (the part after "}\"): the driver itself. The
    /// hands-free speaker and mic reach one driver through two reference strings.
    /// </summary>
    static string DriverOf(string filterId)
    {
        int start = filterId.IndexOf(@"\\?\", StringComparison.Ordinal);
        int reference = start < 0 ? -1 : filterId.IndexOf('\\', start + 4);
        return reference < 0 ? filterId : filterId[..reference];
    }

    /// <summary>"{2}.\\?\bthhfenum#bthhfpaudio#8&amp;2bd1ed7b&amp;0&amp;97#{6994ad04-…}\…" → "bthhfenum\bthhfpaudio\8&amp;2bd1ed7b&amp;0&amp;97".</summary>
    static string? InstanceIdOf(string filterId)
    {
        int start = filterId.IndexOf(@"\\?\", StringComparison.Ordinal);
        if (start < 0) return null;
        var parts = filterId[(start + 4)..].Split('#');
        return parts.Length >= 4 ? string.Join('\\', parts[0], parts[1], parts[2]) : null;
    }

    static string? DeviceIdOf(uint node)
    {
        var buffer = new char[256];
        return CM_Get_Device_IDW(node, buffer, buffer.Length, 0) == 0 ? new string(buffer).TrimEnd('\0') : null;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    static extern int CM_Get_Parent(out uint parent, uint node, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_IDW(uint node, [Out] char[] buffer, int length, uint flags);

    // ------------------------------------------------------------------ COM

    static readonly PropertyKey ContainerId = new() { Format = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 2 };

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid Format;
        public int Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    /// <summary>KSIDENTIFIER / KSPROPERTY: the property set, the property id and the request type.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct KsIdentifier
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, ref PropVariant value);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
        [PreserveSig] int GetSubunitCount(out uint count);
        [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
        [PreserveSig] int GetPartById(uint id, out IntPtr part);
        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
    }

    [ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type); // IConnector::GetType
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IntPtr other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPart
    {
        [PreserveSig] int GetName(out IntPtr name);
        [PreserveSig] int GetLocalId(out uint id);
        [PreserveSig] int GetGlobalId(out IntPtr id);
        [PreserveSig] int GetPartType(out int type);
        [PreserveSig] int GetSubType(out Guid subType);
        [PreserveSig] int GetControlInterfaceCount(out uint count);
        [PreserveSig] int GetControlInterface(uint index, out IntPtr control);
        [PreserveSig] int EnumPartsIncoming(out IntPtr parts);
        [PreserveSig] int EnumPartsOutgoing(out IntPtr parts);
        [PreserveSig] int GetTopologyObject(out IntPtr topology);
        [PreserveSig] int Activate(int clsCtx, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int RegisterControlChangeCallback(ref Guid iid, IntPtr notify);
        [PreserveSig] int UnregisterControlChangeCallback(IntPtr notify);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsIdentifier property, uint propertyLength, IntPtr propertyData, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsMethod(ref KsIdentifier method, uint methodLength, IntPtr methodData, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsEvent(IntPtr ksEvent, uint eventLength, IntPtr eventData, uint dataLength, out uint bytesReturned);
    }
}
