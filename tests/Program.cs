using HeadphoneAutoSwitch;
using System.Globalization;
using Microsoft.Win32;

var tests = new (string, Action)[]
{
    ("Language follows display language, not regional formatting", () =>
    {
        var beforeUi = CultureInfo.CurrentUICulture;
        var beforeFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            if (UiText.Settings != "Settings" || UiText.Online != "Headset connected")
                throw new Exception("English display language was not used");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-KZ");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            if (UiText.Settings != "Настройки" || UiText.StartWithWindows != "Запускать с Windows")
                throw new Exception("Russian display language was not used");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            if (UiText.Settings != "Settings") throw new Exception("Unsupported language must fall back to English");
        }
        finally { CultureInfo.CurrentUICulture = beforeUi; CultureInfo.CurrentCulture = beforeFormat; }
    }),
    ("Every UI message has Russian and English text", () =>
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var property in typeof(UiText).GetProperties())
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
                var ru = property.GetValue(null) as string;
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                var en = property.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(en) || string.IsNullOrWhiteSpace(ru) || en == ru
                    || en.Any(c => c is >= '\u0400' and <= '\u04ff'))
                    throw new Exception("Missing translation: " + property.Name);
            }
            if (!UiText.Battery(56).Contains("56%")) throw new Exception("Missing battery percentage");
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }),
    ("Startup can be enabled and disabled without touching unrelated entries", () =>
    {
        // Isolated disposable registry key, never the real Windows Run key.
        var keyPath = @"Software\HeadphoneAutoSwitch.Test-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "Headphone Auto Switch Test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var exe = Path.Combine(directory, "HeadphoneAutoSwitch.exe");
        File.WriteAllBytes(exe, []);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("OtherApp", "unchanged");
            if (WindowsStartup.IsEnabled(key, exe)) throw new Exception("Startup initially enabled");
            WindowsStartup.SetEnabled(key, true, exe);
            if (!WindowsStartup.IsEnabled(key, exe)) throw new Exception("Startup not enabled");
            if (!Equals(key.GetValue("HeadphoneAutoSwitch"), $"\"{exe}\" --tray"))
                throw new Exception("Executable with spaces must be quoted and launch in tray");
            if (WindowsStartup.IsEnabled(key, Path.Combine(directory, "Moved.exe")))
                throw new Exception("A different application location should not look enabled");
            WindowsStartup.SetEnabled(key, false, exe);
            WindowsStartup.SetEnabled(key, false, exe); // disabling twice is safe
            if (WindowsStartup.IsEnabled(key, exe)) throw new Exception("Startup still enabled");
            if (!Equals(key.GetValue("OtherApp"), "unchanged")) throw new Exception("Unrelated entry changed");
            try
            {
                WindowsStartup.SetEnabled(key, true, Path.Combine(directory, "Missing.exe"));
                throw new Exception("Missing EXE accepted");
            }
            catch (FileNotFoundException) { }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
            File.Delete(exe);
            Directory.Delete(directory);
        }
    }),
    ("Rename migrates only this folder's existing startup entry", () =>
    {
        var keyPath = @"Software\HeadphoneAutoSwitch.Test-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "Headset AutoSwitch Test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var current = Path.Combine(directory, "HeadsetAutoSwitch.exe");
        var legacy = Path.Combine(directory, "HeadphoneAutoSwitch.exe");
        File.WriteAllBytes(current, []);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            WindowsStartup.MigrateLegacyRegistration(key, directory);
            if (WindowsStartup.IsEnabled(key, current)) throw new Exception("Migration enabled startup without an existing preference");
            key.SetValue("HeadphoneAutoSwitch", WindowsStartup.BuildCommand(legacy));
            WindowsStartup.MigrateLegacyRegistration(key, directory);
            if (!WindowsStartup.IsEnabled(key, current)) throw new Exception("Existing startup preference was not migrated");
            var other = WindowsStartup.BuildCommand(Path.Combine(directory, "other", "HeadphoneAutoSwitch.exe"));
            key.SetValue("HeadphoneAutoSwitch", other);
            WindowsStartup.MigrateLegacyRegistration(key, directory);
            if (!Equals(key.GetValue("HeadphoneAutoSwitch"), other)) throw new Exception("Another installation's startup entry changed");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
            File.Delete(current);
            Directory.Delete(directory);
        }
    }),
    ("Two fresh observations establish online", () =>
    {
        var state = new SwitchDecision();
        Equal(null, state.Observe(LinkState.Online));
        Equal(LinkState.Online, state.Observe(LinkState.Online));
    }),
    ("A transient dropout cannot switch to speakers", () =>
    {
        var state = Online();
        Equal(null, state.Observe(LinkState.Offline));
        Equal(null, state.Observe(LinkState.Online));
        Equal<LinkState>(LinkState.Online, state.Stable);
    }),
    ("Three consecutive missing samples establish offline", () =>
    {
        var state = Online();
        Equal(null, state.Observe(LinkState.Offline));
        Equal(null, state.Observe(LinkState.Offline));
        Equal(LinkState.Offline, state.Observe(LinkState.Offline));
    }),
    ("Unknown breaks the confirmation sequence", () =>
    {
        var state = Online();
        state.Observe(LinkState.Offline);
        state.Observe(LinkState.Offline);
        Equal(null, state.Observe(LinkState.Unknown));
        Equal(null, state.Observe(LinkState.Offline));
        Equal<LinkState>(LinkState.Online, state.Stable);
    }),
    ("Manual output choice is respected in a stable state", () =>
    {
        var state = Online();
        for (var i = 0; i < 10; i++) Equal(null, state.Observe(LinkState.Online));
    }),
    ("Failed switch is retried until acknowledged", () =>
    {
        var state = new SwitchDecision();
        state.Observe(LinkState.Online);
        Equal(LinkState.Online, state.Observe(LinkState.Online));
        Equal(LinkState.Online, state.Observe(LinkState.Online));
        state.Acknowledge(LinkState.Online);
        Equal(null, state.Observe(LinkState.Online));
    }),
    ("Opposite pending observation suppresses an old retry", () =>
    {
        var state = new SwitchDecision();
        state.Observe(LinkState.Online);
        state.Observe(LinkState.Online);
        Equal(null, state.Observe(LinkState.Offline));
    }),
    ("Resume/reset discards pre-sleep samples", () =>
    {
        var state = Online();
        state.Observe(LinkState.Offline);
        state.Observe(LinkState.Offline);
        state.Reset();
        Equal(null, state.Observe(LinkState.Offline));
        Equal<LinkState>(LinkState.Unknown, state.Stable);
    }),
    ("Reconnect triggers a new switch after offline", () =>
    {
        var state = Online();
        for (var i = 0; i < 3; i++) state.Observe(LinkState.Offline);
        state.Acknowledge(LinkState.Offline);
        Equal(null, state.Observe(LinkState.Online));
        Equal(LinkState.Online, state.Observe(LinkState.Online));
    }),
    ("Battery zero is valid", () => Equal(0, PaProtocol.ParseBattery(Reply(0)))),
    ("Battery percentage parsed from ACK", () => Equal(77, PaProtocol.ParseBattery(Reply(77)))),
    ("Malformed, unrelated, truncated or invalid replies rejected", () =>
    {
        Equal(null, PaProtocol.ParseBattery([]));
        Equal(null, PaProtocol.ParseBattery(new byte[64]));
        Equal(null, PaProtocol.ParseBattery(Reply(101)));
        Equal(null, PaProtocol.ParseBattery(Reply(77)[..15]));
        var report = Reply(77); report[12] = 0x2a;
        Equal(null, PaProtocol.ParseBattery(report));
        report = Reply(77); report[13] = 0;
        Equal(null, PaProtocol.ParseBattery(report));
        report = Reply(77); report[14] = 0;
        Equal(null, PaProtocol.ParseBattery(report));
        report = Reply(77); report[14] = 60;
        Equal(null, PaProtocol.ParseBattery(report));
    }),
    ("Query and remote frames have the expected protocol layout", () =>
    {
        var query = PaProtocol.BatteryQuery();
        if (query.Length != 64 || query[0] != 2 || query[5] != 'P' || query[6] != 'A'
            || query[9] != 3 || query[10] != 0x21) throw new Exception("Bad battery query");
        if (PaProtocol.Remote(true)[11] != 1 || PaProtocol.Remote(false)[11] != 0)
            throw new Exception("Bad remote toggle");
    })
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static SwitchDecision Online()
{
    var state = new SwitchDecision();
    state.Observe(LinkState.Online);
    state.Observe(LinkState.Online);
    state.Acknowledge(LinkState.Online);
    return state;
}
static void Equal<T>(T? expected, T? actual) where T : struct
{
    if (!EqualityComparer<T?>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected?.ToString() ?? "null"}, got {actual?.ToString() ?? "null"}");
}
static byte[] Reply(byte battery)
{
    var data = new byte[64];
    data[0] = 2; data[12] = 0x21; data[13] = 1; data[14] = 1; data[15] = battery;
    return data;
}
