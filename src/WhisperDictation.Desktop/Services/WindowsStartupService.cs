using Microsoft.Win32;
using WhisperDictation.Core;

namespace WhisperDictation.Desktop.Services;

public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException(UiText.StartupFailed);

        if (!enabled)
        {
            runKey.DeleteValue(AppDefaults.AppName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException(UiText.StartupFailed);

        runKey.SetValue(AppDefaults.AppName, BuildRunCommand(executablePath), RegistryValueKind.String);
    }

    public static string BuildRunCommand(string executablePath) =>
        $"\"{executablePath}\" {AppDefaults.BackgroundLaunchArgument}";
}
