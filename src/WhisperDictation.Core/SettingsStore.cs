using System.Text.Json;
using System.Globalization;

namespace WhisperDictation.Core;

public sealed class SettingsStore(string rootDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DataDirectory { get; } = Path.Combine(rootDirectory, "data");
    public bool RecoveredDefaults { get; private set; }
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public AppSettings Load()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(SettingsPath))
        {
            var initialSettings = new AppSettings { Language = GetSystemLanguage() };
            Save(initialSettings);
            return initialSettings;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            if (settings is null || !Enum.IsDefined(settings.SelectedModel) || !Enum.IsDefined(settings.ComputeMode)
                || !HotkeyChord.TryParse(settings.LegacyToggleHotkey ?? settings.Hotkey, out var chord))
                throw new JsonException();
            var language = LanguageChoice.All.Any(choice => choice.Code == settings.Language)
                ? settings.Language : AppDefaults.DefaultLanguage;
            var outputMode = Enum.IsDefined(settings.OutputMode) ? settings.OutputMode : OutputMode.Transcribe;
            return (settings with
            {
                Hotkey = chord!.ToDisplayString(),
                Language = language,
                OutputMode = outputMode,
                LegacyToggleHotkey = null
            }).WithCompatibleModel();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            RecoveredDefaults = true;
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings.WithCompatibleModel(), JsonOptions));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    public string GetModelPath(ModelKind model) => Path.Combine(rootDirectory, AppDefaults.ModelDirectoryName,
        ModelChoice.All.Single(choice => choice.Kind == model).FileName);

    private static string GetSystemLanguage()
    {
        var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return LanguageChoice.All.Any(choice => choice.Code == systemLanguage)
            ? systemLanguage : AppDefaults.DefaultLanguage;
    }

}
