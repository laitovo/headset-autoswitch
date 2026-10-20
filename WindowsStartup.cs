using Microsoft.Win32;

namespace HeadphoneAutoSwitch;

public static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HeadphoneAutoSwitch";
    private static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "HeadsetAutoSwitch.exe");

    // Keep the registry value identity across the rename, including Windows' disabled state.
    public static void MigrateLegacyRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key != null) MigrateLegacyRegistration(key, AppContext.BaseDirectory);
    }

    internal static void MigrateLegacyRegistration(RegistryKey key, string directory)
    {
        var legacy = Path.Combine(directory, "HeadphoneAutoSwitch.exe");
        var current = Path.Combine(directory, "HeadsetAutoSwitch.exe");
        if (File.Exists(current) && IsEnabled(key, legacy))
            SetEnabled(key, true, current);
    }

    public static string BuildCommand(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" --tray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key != null && IsEnabled(key, ExecutablePath);
    }

    internal static bool IsEnabled(RegistryKey key, string executablePath) =>
        string.Equals(key.GetValue(ValueName) as string, BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);

    // Registry is the source of truth. Only this app's value is changed, on a checkbox change.
    // HKCU makes this a per-user preference and does not require administrator privileges.
    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new IOException(UiText.StartupKeyUnavailable);
            SetEnabled(key, true, ExecutablePath);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key != null) SetEnabled(key, false, ExecutablePath);
        }
    }

    internal static void SetEnabled(RegistryKey key, bool enabled, string executablePath)
    {
        if (enabled)
        {
            if (!File.Exists(executablePath)) throw new FileNotFoundException(UiText.StartupExeMissing, executablePath);
            key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
