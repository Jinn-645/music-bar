using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>A playback device as the control center lists it.</summary>
public sealed record AudioOutput(string Id, string Name, string Adapter, AudioFormFactor Kind);

/// <summary>PKEY_AudioEndpoint_FormFactor values, for the device icon.</summary>
public enum AudioFormFactor
{
    Remote = 0, Speakers = 1, LineLevel = 2, Headphones = 3, Microphone = 4, Headset = 5, Handset = 6,
    UnknownDigital = 7, Spdif = 8, DigitalDisplay = 9, Unknown = 10,
}

/// <summary>
/// The playback devices, which one is the default, and the default device's
/// volume. Switching the default goes through IPolicyConfig, the interface the
/// Windows sound settings use (not public, but stable since Windows 7).
/// </summary>
public sealed class AudioOutputs : IDisposable
{
    const int Render = 0;
    const int ActiveState = 1;
    static Guid NoContext = Guid.Empty;
    static readonly Type EnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!;
    static readonly Type PolicyConfigType = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"))!;

    // Created by CLSID and used through the interface only: the enumerator is shared, and
    // .NET hands back whatever wrapper AudioMeter already made for it.
    static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)Activator.CreateInstance(EnumeratorType)!;

    IAudioEndpointVolume? _volume;
    string? _volumeDevice;

    public IReadOnlyList<AudioOutput> List()
    {
        var result = new List<AudioOutput>();
        try
        {
            var enumerator = Enumerator();
            Check(enumerator.EnumAudioEndpoints(Render, ActiveState, out var devices));
            Check(devices.GetCount(out uint count));
            for (uint i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                if (Describe(device) is { } output) result.Add(output);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Listing audio outputs failed", ex);
        }
        return result;
    }

    public AudioOutput? Default()
    {
        try
        {
            var enumerator = Enumerator();
            return enumerator.GetDefaultAudioEndpoint(Render, 1 /* eMultimedia */, out var device) == 0 ? Describe(device) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Reading the default audio output failed", ex);
            return null;
        }
    }

    /// <summary>Makes the device the default for every role, like the Windows sound settings do.</summary>
    public bool SetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)Activator.CreateInstance(PolicyConfigType)!;
            for (int role = 0; role < 3; role++) Check(policy.SetDefaultEndpoint(id, role));
            Log.Info($"Default audio output set to {id}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Switching the audio output failed", ex);
            return false;
        }
    }

    /// <summary>Volume 0..1 of the default device, or null when it cannot be read.</summary>
    public float? Volume
    {
        get => Try(v => { Check(v.GetMasterVolumeLevelScalar(out float level)); return (float?)level; });
        set
        {
            if (value is float level) Try(v => { Check(v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 1), ref NoContext)); return (float?)level; });
        }
    }

    public bool Muted
    {
        get => Try(v => { Check(v.GetMute(out bool muted)); return (float?)(muted ? 1 : 0); }) == 1;
        set => Try(v => { Check(v.SetMute(value, ref NoContext)); return (float?)0; });
    }

    public void Dispose()
    {
        if (_volume != null) Marshal.ReleaseComObject(_volume);
        _volume = null;
    }

    float? Try(Func<IAudioEndpointVolume, float?> action)
    {
        try
        {
            var volume = EndpointVolume();
            return volume == null ? null : action(volume);
        }
        catch (Exception)
        {
            // The device went away: start over next time.
            Dispose();
            return null;
        }
    }

    /// <summary>The default device's volume control, re-opened when the default changes.</summary>
    IAudioEndpointVolume? EndpointVolume()
    {
        var enumerator = Enumerator();
        if (enumerator.GetDefaultAudioEndpoint(Render, 1, out var device) != 0) return null;
        device.GetId(out string id);
        if (_volume != null && id == _volumeDevice) return _volume;
        Dispose();
        var iid = typeof(IAudioEndpointVolume).GUID;
        Check(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj));
        _volume = (IAudioEndpointVolume)obj;
        _volumeDevice = id;
        return _volume;
    }

    static AudioOutput? Describe(IMMDevice device)
    {
        if (device.GetId(out string id) != 0) return null;
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return new AudioOutput(id, "Thiết bị âm thanh", "", AudioFormFactor.Unknown);
        string name = ReadString(store, DeviceDescription) ?? ReadString(store, FriendlyName) ?? "Thiết bị âm thanh";
        string adapter = ReadString(store, InterfaceName) ?? "";
        var kind = ReadUInt(store, FormFactor) is uint f && f <= 10 ? (AudioFormFactor)f : AudioFormFactor.Unknown;
        return new AudioOutput(id, name, adapter, kind);
    }

    static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        var value = new PropVariant();
        try
        {
            return store.GetValue(ref key, ref value) == 0 && value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    static uint? ReadUInt(IPropertyStore store, PropertyKey key)
    {
        var value = new PropVariant();
        try
        {
            return store.GetValue(ref key, ref value) == 0 && value.Type == 19 /* VT_UI4 */ ? value.UInt : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    static void Check(int hr)
    {
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    // ------------------------------------------------------------------ COM

    static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    static readonly PropertyKey DeviceDescription = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    static readonly PropertyKey InterfaceName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
    static readonly PropertyKey FormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    [StructLayout(LayoutKind.Sequential)]
    readonly struct PropertyKey(Guid format, int id)
    {
        public readonly Guid Format = format;
        public readonly int Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public uint UInt;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
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

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultPeriod, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
