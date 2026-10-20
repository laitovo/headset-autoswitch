using System.Drawing;

namespace HeadphoneAutoSwitch;

public static class AppInfo
{
    public const string Name = "Headset AutoSwitch";
    public const string Version = "0.3.0";
    public const string ExecutableName = "HeadsetAutoSwitch.exe";
    public static Icon AppIcon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("HeadsetAutoSwitch.App.ico")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
