namespace WhisperDictation.Core;

public static class AppDefaults
{
    public const string AppName = "Whisper Dictation";
    public const string AppAssetsDirectoryName = "Assets";
    public const string AppIconFileName = "WhisperDikte.ico";
    public const string WorkerFileName = "WhisperDictation.Worker.exe";
    public const string ModelDirectoryName = "models";
    public const string DefaultHotkey = "Shift+Space";
    public const string BackgroundLaunchArgument = "--background";
    public const string Language = "en";
    public const int SampleRate = 16_000;
    public const int MaximumRecordingSeconds = 300;
    public const int MaximumAudioBytes = SampleRate * sizeof(float) * MaximumRecordingSeconds;
    public const int MaximumMessageBytes = 64 * 1024;
    public const double MinimumRecordingSeconds = 0.25;
    public const float SilenceRmsThreshold = 0.002f;
    public static readonly TimeSpan CaptureStopTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan WorkerConnectTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ModelLoadTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan TranscriptionTimeout = TimeSpan.FromMinutes(5);
}

public enum ModelKind { Turbo, Large }
public enum ComputeMode { Automatic, Cpu }
public enum DictationState { Idle, Loading, Recording, Transcribing, Error }

public sealed record ModelChoice(ModelKind Kind, string Name, string FileName)
{
    public static IReadOnlyList<ModelChoice> All { get; } =
    [
        new(ModelKind.Turbo, UiText.TurboModelName, "ggml-large-v3-turbo.bin"),
        new(ModelKind.Large, UiText.LargeModelName, "ggml-large-v3.bin")
    ];
}

public sealed record AppSettings
{
    public ModelKind SelectedModel { get; init; } = ModelKind.Turbo;
    public ComputeMode ComputeMode { get; init; } = ComputeMode.Automatic;
    public string MicrophoneId { get; init; } = "";
    public string Hotkey { get; init; } = AppDefaults.DefaultHotkey;
    [System.Text.Json.Serialization.JsonPropertyName("ToggleHotkey")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyToggleHotkey { get; init; }
    public bool StartWithWindows { get; init; }
}
