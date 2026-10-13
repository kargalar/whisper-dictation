namespace WhisperDictation.Core;

public sealed record TargetWindow(nint Window, nint Focus, uint ProcessId);
public sealed record TranscriptionResult(string Text, string Engine);
public sealed record HistoryEntry(long Id, DateTimeOffset CreatedAt, ModelKind Model, string Text)
{
    public string DisplayDate => CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string DisplayModel => Model == ModelKind.Turbo ? "Turbo" : "Large";
}

public interface IAudioCapture
{
    event Action? DeviceLost;
    event Action? LimitReached;
    void Start(string microphoneId);
    Task<float[]> StopAsync();
}

public interface ITranscriber
{
    bool IsReady { get; }
    string Engine { get; }
    Task PrepareAsync(string modelPath, ComputeMode mode, CancellationToken cancellationToken);
    Task<TranscriptionResult> TranscribeAsync(float[] audio, CancellationToken cancellationToken);
    void Reset();
}

public enum InsertionStatus { Inserted, TargetChanged, Blocked }

public interface ITextOutput
{
    TargetWindow CaptureTarget();
    Task<InsertionStatus> InsertAsync(TargetWindow target, string text, CancellationToken cancellationToken);
}

public interface IHistoryStore
{
    Task<HistoryEntry> AddAsync(ModelKind model, string text);
    Task<IReadOnlyList<HistoryEntry>> ReadAsync();
    Task DeleteAsync(long id);
    Task ClearAsync();
}
