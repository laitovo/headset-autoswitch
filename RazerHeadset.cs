using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HeadphoneAutoSwitch;

public interface IHeadsetDetector
{
    Task<HeadsetReading> PollAsync(CancellationToken cancellationToken);
}

public sealed record HidInterface(string Path, ushort UsagePage, ushort InputLength, ushort OutputLength);

public sealed class RazerHeadset : IHeadsetDetector
{
    private string? _verifiedPath;
    public void Reset() => _verifiedPath = null;

    public async Task<HeadsetReading> PollAsync(CancellationToken cancellationToken)
    {
        string[] paths;
        try { paths = HidNative.ListRazerPaths(); }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        { return new(LinkState.Unknown, UiText.HidListError + ex.Message); }

        if (paths.Length == 0)
        {
            _verifiedPath = null;
            return new(LinkState.Offline, UiText.ReceiverMissing);
        }
        var details = new List<string>();
        // Prefer the collection that has actually returned a valid headset reply.
        foreach (var path in paths.OrderByDescending(p => p == _verifiedPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = HidNative.Describe(path);
                if (info.UsagePage is not (0xff00 or 0xff14) || info.InputLength < 64 || info.OutputLength < 64)
                    continue;
                var reading = await ProbeAsync(info, path == _verifiedPath, cancellationToken);
                if (reading.State == LinkState.Online) { _verifiedPath = path; return reading; }
                if (reading.State == LinkState.Offline) return reading;
                details.Add(reading.Detail);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            { details.Add(ex.Message); }
        }
        return new(LinkState.Unknown, UiText.ReceiverUnknown +
            (details.Count == 0 ? UiText.HidCollectionMissing : string.Join("; ", details.Distinct())));
    }

    private static async Task<HeadsetReading> ProbeAsync(HidInterface info, bool previouslyVerified, CancellationToken token)
    {
        using var handle = HidNative.Open(info.Path, true);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var stream = new FileStream(handle, FileAccess.ReadWrite, info.InputLength, true);
        var accepted = false;
        try
        {
            // Discard queued replies. A stale battery response must not be treated as power-on.
            var drainWatch = Stopwatch.StartNew();
            for (var i = 0; i < 32 && drainWatch.ElapsedMilliseconds < 100; i++)
                if (await ReadAsync(stream, info.InputLength, 5, token) is null) break;

            await WriteAsync(stream, PaProtocol.Remote(true), info.OutputLength, token);
            accepted = true;
            await Task.Delay(35, token); // wake the radio before the first actual query
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await WriteAsync(stream, PaProtocol.Remote(true), info.OutputLength, token);
                await Task.Delay(35, token);
                await WriteAsync(stream, PaProtocol.BatteryQuery(), info.OutputLength, token);
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 150)
                {
                    var data = await ReadAsync(stream, info.InputLength, 50, token);
                    if (data != null && PaProtocol.ParseBattery(data) is { } battery)
                        return new(LinkState.Online, UiText.Battery(battery), battery);
                }
            }
            // Silence is only evidence of link loss on a collection that returned a valid
            // reply before. At first launch, a wrong/inaccessible collection is indistinguishable.
            return previouslyVerified
                ? new(LinkState.Offline, UiText.NoReply)
                : new(LinkState.Unknown, UiText.FirstConnection);
        }
        finally
        {
            if (accepted)
            {
                try { await WriteAsync(stream, PaProtocol.Remote(false), info.OutputLength, CancellationToken.None); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
                { Storage.Log(UiText.HidCleanup + ex.Message); }
            }
        }
    }

    private static async Task WriteAsync(FileStream stream, byte[] frame, int length, CancellationToken token)
    {
        var report = new byte[length];
        frame.CopyTo(report, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(500);
        try { await stream.WriteAsync(report, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException(UiText.HidWriteTimeout); }
    }

    private static async Task<byte[]?> ReadAsync(FileStream stream, int length, int timeoutMs, CancellationToken token)
    {
        var report = new byte[length];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMs);
        try
        {
            var read = await stream.ReadAsync(report, timeout.Token);
            if (read == 0) throw new IOException(UiText.HidClosed);
            return report[..read];
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
    }
}

internal static class HidNative
{
    private static readonly Guid HidGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    public static string[] ListRazerPaths()
    {
        var guid = HidGuid;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var rc = CM_Get_Device_Interface_List_SizeW(out var size, ref guid, null, 0);
            if (rc != 0) throw new IOException($"CM_Get_Device_Interface_List_Size: 0x{rc:X}");
            var buffer = new char[size + 1];
            rc = CM_Get_Device_Interface_ListW(ref guid, null, buffer, (uint)buffer.Length, 0);
            if (rc == 0x1a) continue; // device list grew between the two calls
            if (rc != 0) throw new IOException($"CM_Get_Device_Interface_List: 0x{rc:X}");
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Contains("vid_1532", StringComparison.OrdinalIgnoreCase)
                    && p.Contains("pid_0555", StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        throw new IOException(UiText.HidListChanging);
    }

    public static SafeFileHandle Open(string path, bool readWrite) => CreateFileW(path,
        readWrite ? 0xc0000000u : 0, 3, IntPtr.Zero, 3, readWrite ? 0x40000000u : 0, IntPtr.Zero);

    public static HidInterface Describe(string path)
    {
        // Only open collections of the exact receiver, never keyboards/mice of other models.
        using var handle = Open(path, false);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!HidD_GetPreparsedData(handle, out var data)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var status = HidP_GetCaps(data, out var caps);
            if (status != 0x00110000) throw new IOException($"HidP_GetCaps: 0x{status:X}");
            return new(path, caps.UsagePage, caps.InputReportByteLength, caps.OutputReportByteLength);
        }
        finally { HidD_FreePreparsedData(data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid guid, string? id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(ref Guid guid, string? id, [Out] char[] buffer, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
}
