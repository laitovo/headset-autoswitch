using System.Text.Json;

namespace HeadphoneAutoSwitch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--diagnose"))
        {
            var index = Array.IndexOf(args, "--diagnose");
            var path = index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
            return DiagnoseAsync(path, args.Contains("--watch")).GetAwaiter().GetResult();
        }

        using var mutex = new Mutex(true, "Local\\HeadphoneAutoSwitch", out var firstInstance);
        if (!firstInstance)
        {
            if (!args.Contains("--tray"))
                MessageBox.Show(UiText.AlreadyRunning, AppInfo.Name);
            return 0;
        }
        try
        {
            try { WindowsStartup.MigrateLegacyRegistration(); }
            catch (Exception ex) { Storage.Log(UiText.StartupWriteError + ": " + ex.Message); }
            Application.Run(new MainForm(args.Contains("--tray")));
            return 0;
        }
        catch (Exception ex)
        {
            Storage.Log(ex.ToString());
            MessageBox.Show(ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static async Task<int> DiagnoseAsync(string path, bool watch)
    {
        var report = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.Now,
            ["version"] = AppInfo.Version,
            ["uiLanguage"] = UiText.LanguageFor(System.Globalization.CultureInfo.CurrentUICulture),
            ["os"] = Environment.OSVersion.ToString(),
            ["note"] = UiText.DiagnoseNote
        };
        try
        {
            report["outputs"] = WindowsAudio.ListOutputs();
            report["defaultOutputId"] = WindowsAudio.DefaultId();
        }
        catch (Exception ex) { report["audioError"] = ex.ToString(); }
        try
        {
            var paths = HidNative.ListRazerPaths();
            var interfaces = new List<object>();
            foreach (var devicePath in paths)
            {
                try { interfaces.Add(HidNative.Describe(devicePath)); }
                catch (Exception ex) { interfaces.Add(new { Path = devicePath, Error = ex.Message }); }
            }
            report["hidInterfaces"] = interfaces;
            var detector = new RazerHeadset();
            var readings = new List<HeadsetReading>();
            report["readings"] = readings;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(watch ? 100 : 20));
            var count = watch ? 45 : 3;
            for (var i = 0; i < count; i++)
            {
                readings.Add(await detector.PollAsync(timeout.Token));
                File.WriteAllText(Path.GetFullPath(path), JsonSerializer.Serialize(report, Storage.JsonOptions));
                if (i < count - 1) await Task.Delay(1000, timeout.Token);
            }
        }
        catch (Exception ex) { report["hidError"] = ex.ToString(); }
        try
        {
            File.WriteAllText(Path.GetFullPath(path), JsonSerializer.Serialize(report, Storage.JsonOptions));
            return report.ContainsKey("audioError") || report.ContainsKey("hidError") ? 1 : 0;
        }
        catch (Exception ex)
        {
            Storage.Log(UiText.DiagnoseSaveError + ex.Message);
            return 1;
        }
    }
}
