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
    public const string DefaultLanguage = "auto";
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
public enum OutputMode { Transcribe, TranslateToEnglish }
public enum DictationState { Idle, Loading, Recording, Transcribing, Error }

public sealed record OutputModeChoice(OutputMode Mode, string Name)
{
    public static IReadOnlyList<OutputModeChoice> All { get; } =
    [new(OutputMode.Transcribe, UiText.TranscribeMode), new(OutputMode.TranslateToEnglish, UiText.TranslateToEnglishMode)];
}

public sealed record LanguageChoice(string Code, string Name)
{
    public static IReadOnlyList<LanguageChoice> All { get; } =
    [
        new(AppDefaults.DefaultLanguage, UiText.AutoDetectLanguage),
        new("en", "English"), new("zh", "中文"), new("de", "Deutsch"), new("es", "Español"),
        new("ru", "Русский"), new("ko", "한국어"), new("fr", "Français"), new("ja", "日本語"),
        new("pt", "Português"), new("tr", "Türkçe"), new("pl", "Polski"), new("ca", "Català"),
        new("nl", "Nederlands"), new("ar", "العربية"), new("sv", "Svenska"), new("it", "Italiano"),
        new("id", "Bahasa Indonesia"), new("hi", "हिन्दी"), new("fi", "Suomi"), new("vi", "Tiếng Việt"),
        new("he", "עברית"), new("uk", "Українська"), new("el", "Ελληνικά"), new("ms", "Bahasa Melayu"),
        new("cs", "Čeština"), new("ro", "Română"), new("da", "Dansk"), new("hu", "Magyar"),
        new("ta", "தமிழ்"), new("no", "Norsk"), new("th", "ไทย"), new("ur", "اردو"),
        new("hr", "Hrvatski"), new("bg", "Български"), new("lt", "Lietuvių"), new("la", "Latina"),
        new("mi", "Māori"), new("ml", "മലയാളം"), new("cy", "Cymraeg"), new("sk", "Slovenčina"),
        new("te", "తెలుగు"), new("fa", "فارسی"), new("lv", "Latviešu"), new("bn", "বাংলা"),
        new("sr", "Српски"), new("az", "Azərbaycanca"), new("sl", "Slovenščina"), new("kn", "ಕನ್ನಡ"),
        new("et", "Eesti"), new("mk", "Македонски"), new("br", "Brezhoneg"), new("eu", "Euskara"),
        new("is", "Íslenska"), new("hy", "Հայերեն"), new("ne", "नेपाली"), new("mn", "Монгол"),
        new("bs", "Bosanski"), new("kk", "Қазақша"), new("sq", "Shqip"), new("sw", "Kiswahili"),
        new("gl", "Galego"), new("mr", "मराठी"), new("pa", "ਪੰਜਾਬੀ"), new("si", "සිංහල"),
        new("km", "ភាសាខ្មែរ"), new("sn", "chiShona"), new("yo", "Yorùbá"), new("so", "Soomaali"),
        new("af", "Afrikaans"), new("oc", "Occitan"), new("ka", "ქართული"), new("be", "Беларуская"),
        new("tg", "Тоҷикӣ"), new("sd", "سنڌي"), new("gu", "ગુજરાતી"), new("am", "አማርኛ"),
        new("yi", "ייִדיש"), new("lo", "ລາວ"), new("uz", "Oʻzbekcha"), new("fo", "Føroyskt"),
        new("ht", "Kreyòl ayisyen"), new("ps", "پښتو"), new("tk", "Türkmen"), new("nn", "Nynorsk"),
        new("mt", "Malti"), new("sa", "संस्कृतम्"), new("lb", "Lëtzebuergesch"), new("my", "မြန်မာ"),
        new("bo", "བོད་སྐད་"), new("tl", "Filipino"), new("mg", "Malagasy"), new("as", "অসমীয়া"),
        new("tt", "Татарча"), new("haw", "ʻŌlelo Hawaiʻi"), new("ln", "Lingála"), new("ha", "Hausa"),
        new("ba", "Башҡортса"), new("jw", "Basa Jawa"), new("su", "Basa Sunda")
    ];
}

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
    public string Language { get; init; } = AppDefaults.DefaultLanguage;
    public OutputMode OutputMode { get; init; } = OutputMode.Transcribe;
    public ModelKind SelectedModel { get; init; } = ModelKind.Turbo;
    public ComputeMode ComputeMode { get; init; } = ComputeMode.Automatic;
    public string MicrophoneId { get; init; } = "";
    public string Hotkey { get; init; } = AppDefaults.DefaultHotkey;
    [System.Text.Json.Serialization.JsonPropertyName("ToggleHotkey")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyToggleHotkey { get; init; }
    public bool StartWithWindows { get; init; }

    public AppSettings WithCompatibleModel() => OutputMode == OutputMode.TranslateToEnglish
        ? this with { SelectedModel = ModelKind.Large }
        : this;
}
