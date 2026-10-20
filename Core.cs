using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace HeadphoneAutoSwitch;

public enum LinkState { Unknown, Online, Offline }
public sealed record HeadsetReading(LinkState State, string Detail, int? Battery = null);
public sealed record AudioEndpoint(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class Settings
{
    public string HeadphonesId { get; set; } = "";
    public string SpeakersId { get; set; } = "";
    public bool SwitchCommunications { get; set; }
    public bool AutoSwitch { get; set; }
    public int PollMilliseconds { get; set; } = 1500;
    public int OfflineSamples { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(HeadphonesId)
        && !string.IsNullOrWhiteSpace(SpeakersId) && HeadphonesId != SpeakersId;
}

public static class Storage
{
    public static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "data");
    public static readonly string ConfigPath = Path.Combine(DirectoryPath, "settings.json");
    public static readonly string LogPath = Path.Combine(DirectoryPath, "app.log");
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly object Gate = new();

    public static Settings Load()
    {
        if (!File.Exists(ConfigPath)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(ConfigPath)) ?? new();
        settings.PollMilliseconds = Math.Clamp(settings.PollMilliseconds, 1000, 10000);
        settings.OfflineSamples = Math.Clamp(settings.OfflineSamples, 2, 10);
        return settings;
    }

    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, ConfigPath, true);
    }

    public static void Log(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                    File.Move(LogPath, LogPath + ".old", true);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

// The switch is acknowledged only after the audio operation succeeds. Once acknowledged,
// the user can change outputs manually until the next confirmed physical transition.
public sealed class SwitchDecision(int offlineSamples = 3)
{
    private LinkState _candidate = LinkState.Unknown;
    private int _samples;
    public LinkState Stable { get; private set; } = LinkState.Unknown;
    public LinkState Applied { get; private set; } = LinkState.Unknown;

    public LinkState? Observe(LinkState state)
    {
        if (state == LinkState.Unknown)
        {
            _candidate = LinkState.Unknown;
            _samples = 0;
            return null;
        }
        if (_candidate != state) { _candidate = state; _samples = 0; }
        _samples = Math.Min(_samples + 1, 100);
        if (_samples >= (state == LinkState.Online ? 2 : offlineSamples))
            Stable = state;
        // Never retry an older decision while a different observation is pending.
        return state == Stable && Stable != Applied ? Stable : null;
    }

    public void Acknowledge(LinkState state) => Applied = state;
    public void Reset()
    {
        _candidate = Stable = Applied = LinkState.Unknown;
        _samples = 0;
    }
}

// PA frame layout researched from HaloBattery's MIT-licensed blackshark provider.
// See THIRD-PARTY-NOTICES.md. Strict parsing avoids treating unrelated HID traffic as a reply.
public static class PaProtocol
{
    public static byte[] Remote(bool on)
    {
        var b = Header();
        b[2] = 7; b[7] = 0x0e; b[9] = 2; b[10] = 0xe1; b[11] = on ? (byte)1 : (byte)0;
        return b;
    }

    public static byte[] BatteryQuery()
    {
        var b = Header();
        b[2] = 8; b[7] = 8; b[9] = 3; b[10] = 0x21;
        return b;
    }

    private static byte[] Header()
    {
        var b = new byte[64];
        b[0] = 2; b[1] = 0x80; b[5] = 0x50; b[6] = 0x41;
        return b;
    }

    public static int? ParseBattery(ReadOnlySpan<byte> data)
    {
        // Windows input reports include the report ID. Accept only a complete ACK,
        // at least one payload byte and an actual percentage (including a valid zero).
        if (data.Length < 16 || data[0] != 2 || data[12] != 0x21 || data[13] != 1)
            return null;
        var length = data[14];
        if (length < 1 || length > data.Length - 15 || data[15] > 100) return null;
        return data[15];
    }
}
