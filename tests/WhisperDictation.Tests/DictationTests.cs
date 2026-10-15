using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;
using WhisperDictation.Desktop.ViewModels;
using Xunit;

namespace WhisperDictation.Tests;

public class DictationTests
{
    [Fact]
    public async Task LanguageModeAndModelAreSavedAndAppliedTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), "Whisper-language-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = new Fixture();
            var store = new SettingsStore(root);
            store.Save(new());
            HotkeyChord.TryParse(AppDefaults.DefaultHotkey, out var chord);
            using var hotkeys = new GlobalHotkeyService(chord!);
            var viewModel = new MainViewModel(store, fixture.History, fixture.Controller, fixture.Engine, hotkeys, new());

            viewModel.SelectedLanguage = viewModel.Languages.Single(choice => choice.Code == "tr");
            viewModel.SelectedOutputMode = viewModel.OutputModes.Single(choice => choice.Mode == OutputMode.TranslateToEnglish);
            Assert.Equal(ModelKind.Large, viewModel.SelectedModel.Kind);
            Assert.False(viewModel.CanSelectModel);
            Assert.Empty(fixture.Engine.PreparedPath);
            await Task.Delay(400);

            Assert.Equal(ModelKind.Large, store.Load().SelectedModel);
            Assert.Equal("tr", fixture.Engine.PreparedLanguage);
            Assert.Equal(OutputMode.TranslateToEnglish, fixture.Engine.PreparedOutputMode);
            Assert.Equal(store.GetModelPath(ModelKind.Large), fixture.Engine.PreparedPath);
            viewModel.SelectedOutputMode = viewModel.OutputModes.Single(choice => choice.Mode == OutputMode.Transcribe);
            Assert.True(viewModel.CanSelectModel);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReleasedDictationIsSavedAndInsertedExactlyOnce()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(new() { SelectedModel = ModelKind.Large });
        fixture.Controller.Start(new());
        await fixture.Controller.StopAsync();
        await fixture.Controller.StopAsync();
        Assert.Equal(1, fixture.Audio.StartCalls);
        Assert.Equal(1, fixture.Output.InsertCalls);
        Assert.Equal(ModelKind.Large, Assert.Single(fixture.History.Entries).Model);
        Assert.Equal("İstanbul, ölçüm ve API testi.", fixture.Controller.LastText);
        Assert.Equal(UiText.Transferred, fixture.Controller.Status);
        Assert.All(fixture.Audio.Samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public async Task SilenceDoesNotCallEngineOrWriteHistory()
    {
        var fixture = new Fixture();
        fixture.Audio.Samples = new float[AppDefaults.SampleRate];
        fixture.Controller.Start(new());
        await fixture.Controller.StopAsync();
        Assert.Equal(0, fixture.Engine.ProcessCalls);
        Assert.Empty(fixture.History.Entries);
        Assert.Equal(UiText.EmptyAudio, fixture.Controller.Status);
    }

    [Fact]
    public async Task CancelDuringRecordingDiscardsAudio()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(new());
        await fixture.Controller.CancelAsync();
        await fixture.Controller.StopAsync();
        Assert.Empty(fixture.History.Entries);
        Assert.Equal(0, fixture.Output.InsertCalls);
        Assert.All(fixture.Audio.Samples, sample => Assert.Equal(0, sample));
        Assert.Equal(DictationState.Idle, fixture.Controller.State);
    }

    [Fact]
    public async Task CancelDuringInferenceDoesNotSaveOrInsert()
    {
        var fixture = new Fixture();
        fixture.Engine.BlockInference = true;
        fixture.Controller.Start(new());
        var pending = fixture.Controller.StopAsync();
        await fixture.Engine.Started.Task;
        await fixture.Controller.CancelAsync();
        await pending;
        Assert.Empty(fixture.History.Entries);
        Assert.Equal(0, fixture.Output.InsertCalls);
        Assert.Equal(UiText.Cancelled, fixture.Controller.Status);
    }

    [Fact]
    public async Task FocusChangePreservesResultInHistory()
    {
        var fixture = new Fixture();
        fixture.Output.Result = InsertionStatus.TargetChanged;
        fixture.Controller.Start(new());
        await fixture.Controller.StopAsync();
        Assert.Single(fixture.History.Entries);
        Assert.Equal(UiText.TargetChanged, fixture.Controller.Status);
        Assert.NotEmpty(fixture.Controller.LastText);
    }

    [Fact]
    public async Task BlockedInsertionPreservesResultInHistory()
    {
        var fixture = new Fixture();
        fixture.Output.Result = InsertionStatus.Blocked;
        fixture.Controller.Start(new());
        await fixture.Controller.StopAsync();
        Assert.Single(fixture.History.Entries);
        Assert.Equal(UiText.InputBlocked, fixture.Controller.Status);
    }

    [Fact]
    public async Task BrokenMicrophoneCannotLeaveSessionRecording()
    {
        var fixture = new Fixture();
        fixture.Audio.FailStop = true;
        fixture.Controller.Start(new());
        await fixture.Controller.StopAsync();
        Assert.Equal(DictationState.Error, fixture.Controller.State);
        Assert.Empty(fixture.History.Entries);
        Assert.Equal(0, fixture.Output.InsertCalls);
    }

    [Fact]
    public async Task ChangingModelReinitializesEngineAndDoesNotRecordWhileLoading()
    {
        var fixture = new Fixture();
        fixture.Engine.BlockPreparation = true;
        var preparation = fixture.Controller.PrepareAsync("large.bin", ComputeMode.Cpu);
        fixture.Controller.Start(new());
        Assert.Equal(0, fixture.Audio.StartCalls);
        Assert.Equal(DictationState.Loading, fixture.Controller.State);
        fixture.Engine.Prepared.TrySetResult();
        await preparation;
        Assert.Equal("large.bin", fixture.Engine.PreparedPath);
        Assert.Equal(ComputeMode.Cpu, fixture.Engine.PreparedMode);
        Assert.Equal(UiText.Ready, fixture.Controller.Status);
    }

    [Fact]
    public async Task HistoryFailureStillAllowsCopyAndInsertion()
    {
        var fixture = new Fixture();
        fixture.History.FailWrite = true;
        fixture.Controller.Start(new());
        await fixture.Controller.StopAsync();
        Assert.NotEmpty(fixture.Controller.LastText);
        Assert.Equal(1, fixture.Output.InsertCalls);
        Assert.Equal(UiText.HistoryWriteFailed, fixture.Controller.Status);
    }

    private sealed class Fixture
    {
        public FakeAudio Audio { get; } = new();
        public FakeEngine Engine { get; } = new();
        public FakeOutput Output { get; } = new();
        public FakeHistory History { get; } = new();
        public DictationController Controller { get; }
        public Fixture() => Controller = new(Audio, Engine, Output, History);
    }

    private sealed class FakeAudio : IAudioCapture
    {
        public event Action? DeviceLost { add { } remove { } }
        public event Action? LimitReached { add { } remove { } }
        public float[] Samples { get; set; } = Enumerable.Repeat(0.1f, AppDefaults.SampleRate).ToArray();
        public int StartCalls { get; private set; }
        public bool FailStop { get; set; }
        public void Start(string microphoneId) => StartCalls++;
        public Task<float[]> StopAsync() => FailStop ? throw new IOException(UiText.MicrophoneLost) : Task.FromResult(Samples);
    }

    private sealed class FakeEngine : ITranscriber
    {
        public bool IsReady => true;
        public string Engine => "CPU";
        public int ProcessCalls { get; private set; }
        public bool BlockInference { get; set; }
        public bool BlockPreparation { get; set; }
        public string PreparedPath { get; private set; } = "";
        public ComputeMode PreparedMode { get; private set; }
        public string PreparedLanguage { get; private set; } = "";
        public OutputMode PreparedOutputMode { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Prepared { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PrepareAsync(string path, ComputeMode mode, string language, OutputMode outputMode, CancellationToken token)
        {
            PreparedPath = path;
            PreparedMode = mode;
            PreparedLanguage = language;
            PreparedOutputMode = outputMode;
            if (BlockPreparation) await Prepared.Task.WaitAsync(token);
        }
        public async Task<TranscriptionResult> TranscribeAsync(float[] audio, CancellationToken token)
        {
            ProcessCalls++;
            Started.TrySetResult();
            if (BlockInference) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new("İstanbul, ölçüm ve API testi.", Engine);
        }
        public void Reset() { }
    }

    private sealed class FakeOutput : ITextOutput
    {
        public int InsertCalls { get; private set; }
        public InsertionStatus Result { get; set; } = InsertionStatus.Inserted;
        public TargetWindow CaptureTarget() => new(123, 124, 125);
        public Task<InsertionStatus> InsertAsync(TargetWindow target, string text, CancellationToken token)
        { InsertCalls++; return Task.FromResult(Result); }
    }

    private sealed class FakeHistory : IHistoryStore
    {
        public List<HistoryEntry> Entries { get; } = [];
        public bool FailWrite { get; set; }
        public Task<HistoryEntry> AddAsync(ModelKind model, string text)
        {
            if (FailWrite) throw new IOException();
            var entry = new HistoryEntry(Entries.Count + 1, DateTimeOffset.UtcNow, model, text);
            Entries.Add(entry);
            return Task.FromResult(entry);
        }
        public Task<IReadOnlyList<HistoryEntry>> ReadAsync() => Task.FromResult<IReadOnlyList<HistoryEntry>>(Entries);
        public Task DeleteAsync(long id) => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;
    }
}
