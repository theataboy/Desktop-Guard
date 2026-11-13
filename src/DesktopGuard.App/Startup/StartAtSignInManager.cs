using Microsoft.Win32;

namespace DesktopGuard.App.Startup;

internal sealed class StartAtSignInManager : IStartAtSignInManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopGuard";

    public void SetEnabled(bool enabled)
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? (enabled ? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true) : null);

        if (runKey is null)
        {
            return;
        }

        if (!enabled)
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            throw new InvalidOperationException("The application executable path is unavailable.");
        }

        runKey.SetValue(ValueName, $"\"{executablePath}\" --background", RegistryValueKind.String);
    }
}
