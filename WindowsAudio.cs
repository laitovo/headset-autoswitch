using System.Runtime.InteropServices;

namespace HeadphoneAutoSwitch;

public static class WindowsAudio
{
    public static List<AudioEndpoint> ListOutputs()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        var result = new List<AudioEndpoint>();
        try
        {
            Check(enumerator.EnumAudioEndpoints(0, 1, out collection)); // render, active
            Check(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                Check(collection.Item(i, out var device));
                IPropertyStore? properties = null;
                try
                {
                    Check(device.GetId(out var id));
                    Check(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey(new("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
                    Check(properties.GetValue(ref key, out var value));
                    try
                    {
                        var name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;
                        result.Add(new(id, name ?? id));
                    }
                    finally { PropVariantClear(ref value); }
                }
                finally { Release(properties); Release(device); }
            }
            return result.OrderBy(x => x.Name).ToList();
        }
        finally { Release(collection); Release(enumerator); }
    }

    public static string? DefaultId(int role = 1)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
            if (hr == unchecked((int)0x80070490)) return null; // no default
            Check(hr);
            Check(device.GetId(out var id));
            return id;
        }
        finally { Release(device); Release(enumerator); }
    }

    public static void SetDefault(string id, bool communications)
    {
        if (!ListOutputs().Any(x => x.Id == id))
            throw new InvalidOperationException(UiText.OutputUnavailable);
        int[] roles = communications ? [0, 1, 2] : [0, 1];
        var before = roles.ToDictionary(x => x, x => DefaultId(x));
        // Windows exposes no documented public setter for the system default endpoint.
        // PolicyConfig is the established COM interface used for this on Windows 10/11.
        var policy = (IPolicyConfig)new PolicyConfigClient();
        var changed = new List<int>();
        try
        {
            foreach (var role in roles)
            {
                if (before[role] == id) continue;
                Check(policy.SetDefaultEndpoint(id, role));
                changed.Add(role);
            }
        }
        catch (Exception error)
        {
            foreach (var role in changed)
                if (before[role] is { } old)
                {
                    var hr = policy.SetDefaultEndpoint(old, role);
                    if (hr < 0) Storage.Log(UiText.RestoreRoleError(role, hr));
                }
            throw new InvalidOperationException(UiText.WindowsSwitchError + error.Message, error);
        }
        finally { Release(policy); }
    }

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr id, int @default, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr id);
        [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod(IntPtr id, int @default, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
    }
}
