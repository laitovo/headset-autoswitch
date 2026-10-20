using System.Globalization;
using HeadphoneAutoSwitch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var language = args.Length > 0 ? args[0] : "en-US";
        var tray = args.Contains("--tray");
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            // This executable has its own data directory, with automatic switching disabled.
            using var form = new MainForm(tray);
            using var timer = new System.Windows.Forms.Timer { Interval = tray ? 3000 : 200 };
            Exception? failure = null;
            var observed = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    observed = true;
                    if (form.Text != "Headset AutoSwitch") throw new Exception("Application name was not updated");
                    if (form.Icon == null || form.Icon.Handle == SystemIcons.Application.Handle)
                        throw new Exception("Application icon is missing");
                    if (form.Visible == tray) throw new Exception("Unexpected initial window visibility");
                    if (tray && !Descendants(form).OfType<Label>().Any(x =>
                        x.Text.StartsWith(UiText.Online) || x.Text.StartsWith(UiText.Offline)
                        || x.Text.StartsWith(UiText.Unknown) || x.Text.StartsWith(UiText.PollError)))
                        throw new Exception("The hidden app did not start background polling");
                    if (!tray)
                    {
                        var controls = Descendants(form).ToList();
                        if (!controls.OfType<CheckBox>().Any(x => x.Text == UiText.AutoSwitch))
                            throw new Exception("Translated checkbox not found");
                        if (!controls.OfType<CheckBox>().Any(x => x.Text == UiText.StartWithWindows))
                            throw new Exception("Startup checkbox not found in the main window");
                        if (!controls.OfType<Button>().Any(x => x.Text == UiText.Save))
                            throw new Exception("Translated save button not found");
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"preview-{language}.png"));
                    }
                    Console.WriteLine($"PASS {language}: {(tray ? "tray launch stays hidden and polls" : "normal launch shows translated settings")}");
                }
                catch (Exception ex) { failure = ex; }
                finally { Application.Exit(); }
            };
            timer.Start();
            Application.Run(form);
            if (failure != null) throw failure;
            if (!observed) throw new Exception("The message loop ended before the UI check ran");
        }
        return 0;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }
}
