namespace WhisperDictation.Core;

// Call from one synchronization context. Capture callbacks are dispatched by the UI host.
public sealed class DictationController(IAudioCapture audio, ITranscriber transcriber, ITextOutput output, IHistoryStore history)
{
    private CancellationTokenSource? operation;
    private TargetWindow? target;
    private ModelKind recordingModel;
    public DictationState State { get; private set; }
    public string Status { get; private set; } = UiText.Idle;
    public string LastText { get; private set; } = "";
    public bool IsBusy => State is DictationState.Loading or DictationState.Recording or DictationState.Transcribing;
    public event Action? Changed;
    public event Action<HistoryEntry>? HistoryAdded;
    public event Action? MicrophoneFallback;

    public async Task PrepareAsync(string path, ComputeMode mode, string language = AppDefaults.DefaultLanguage,
        OutputMode outputMode = OutputMode.Transcribe)
    {
        if (IsBusy)
            return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        SetState(DictationState.Loading, UiText.Loading);
        try
        {
            await transcriber.PrepareAsync(path, mode, language, outputMode, cancellation.Token);
            SetState(DictationState.Idle, UiText.Ready);
        }
        catch (OperationCanceledException) { SetState(DictationState.Idle, UiText.Cancelled); }
        catch (Exception ex) { SetState(DictationState.Error, ex.Message); }
        finally { operation = null; }
    }

    public void Start(AppSettings settings)
    {
        if (IsBusy)
            return;
        if (!transcriber.IsReady)
        {
            SetState(DictationState.Error, UiText.ModelNotReady);
            return;
        }
        try
        {
            target = output.CaptureTarget();
            recordingModel = settings.SelectedModel;
            try
            {
                audio.Start(settings.MicrophoneId);
            }
            catch when (!string.IsNullOrEmpty(settings.MicrophoneId))
            {
                audio.Start("");
                MicrophoneFallback?.Invoke();
            }
            SetState(DictationState.Recording, UiText.Listening);
        }
        catch (Exception) { SetState(DictationState.Error, UiText.MicrophoneFailed); }
    }

    public async Task StopAsync()
    {
        if (State != DictationState.Recording)
            return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        SetState(DictationState.Transcribing, UiText.Transcribing);
        float[] samples = [];
        try
        {
            samples = await audio.StopAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ContainsSpeech(samples))
            {
                SetState(DictationState.Idle, UiText.EmptyAudio);
                return;
            }
            var result = await transcriber.TranscribeAsync(samples, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            LastText = result.Text.Trim();
            Changed?.Invoke();
            if (LastText.Length == 0)
            {
                SetState(DictationState.Idle, UiText.EmptyAudio);
                return;
            }
            var historySaved = true;
            try
            {
                var entry = await history.AddAsync(recordingModel, LastText);
                HistoryAdded?.Invoke(entry);
            }
            catch (Exception) { historySaved = false; }
            cancellation.Token.ThrowIfCancellationRequested();
            var status = await output.InsertAsync(target!, LastText, cancellation.Token);
            SetState(DictationState.Idle, !historySaved ? UiText.HistoryWriteFailed : status switch
            {
                InsertionStatus.Inserted => UiText.Transferred,
                InsertionStatus.TargetChanged => UiText.TargetChanged,
                _ => UiText.InputBlocked
            });
        }
        catch (OperationCanceledException) { SetState(DictationState.Idle, UiText.Cancelled); }
        catch (Exception ex) { SetState(DictationState.Error, ex.Message); }
        finally
        {
            Array.Clear(samples);
            operation = null;
        }
    }

    public async Task CancelAsync(string? message = null)
    {
        if (State == DictationState.Recording)
        {
            SetState(DictationState.Transcribing, UiText.Cancelled);
            try { Array.Clear(await audio.StopAsync()); }
            catch (Exception) { /* Disconnection may have already stopped capture. */ }
            SetState(DictationState.Idle, message ?? UiText.Cancelled);
        }
        else if (operation is not null)
        {
            operation.Cancel();
        }
    }

    public static bool ContainsSpeech(float[] samples)
    {
        if (samples.Length < AppDefaults.SampleRate * AppDefaults.MinimumRecordingSeconds)
            return false;
        double energy = 0;
        foreach (var sample in samples)
            energy += sample * sample;
        return Math.Sqrt(energy / samples.Length) >= AppDefaults.SilenceRmsThreshold;
    }

    private void SetState(DictationState state, string status)
    {
        State = state;
        Status = status;
        Changed?.Invoke();
    }
}
