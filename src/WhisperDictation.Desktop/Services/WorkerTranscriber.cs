using System.Diagnostics;
using System.IO.Pipes;
using WhisperDictation.Core;

namespace WhisperDictation.Desktop.Services;

public sealed class WorkerTranscriber : ITranscriber, IDisposable
{
    private Process? process;
    private NamedPipeServerStream? pipe;
    private string modelPath = "";
    private string language = AppDefaults.DefaultLanguage;
    private OutputMode outputMode = OutputMode.Transcribe;
    private ComputeMode mode;
    private bool usingGpu;
    private bool fellBack;
    public bool IsReady => pipe is { IsConnected: true } && process is { HasExited: false };
    public string Engine { get; private set; } = UiText.UnknownEngine;

    public async Task PrepareAsync(string modelPath, ComputeMode mode, string language, OutputMode outputMode, CancellationToken cancellationToken)
    {
        Reset();
        if (!File.Exists(modelPath))
            throw new InvalidOperationException(UiText.MissingModel);
        this.modelPath = modelPath;
        this.language = language;
        this.outputMode = outputMode;
        this.mode = mode;
        fellBack = false;
        try
        {
            if (mode == ComputeMode.Automatic)
            {
                try { await StartWorkerAsync(useGpu: true, cancellationToken); return; }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    Reset();
                    fellBack = true;
                }
            }
            await StartWorkerAsync(useGpu: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Reset(); throw; }
        catch (Exception ex)
        {
            Reset();
            throw new InvalidOperationException(ex.Message == UiText.InvalidModel ? UiText.InvalidModel : UiText.WorkerFailed, ex);
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(float[] audio, CancellationToken cancellationToken)
    {
        if (!IsReady)
            await PrepareAsync(modelPath, mode, language, outputMode, cancellationToken);
        try { return await ProcessAudioAsync(audio, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && usingGpu)
        {
            Reset();
            fellBack = true;
            try
            {
                await StartWorkerAsync(useGpu: false, cancellationToken);
                return await ProcessAudioAsync(audio, cancellationToken);
            }
            catch { Reset(); throw; }
        }
        catch { Reset(); throw; }
    }

    private async Task StartWorkerAsync(bool useGpu, CancellationToken cancellationToken)
    {
        var logFile = Path.Combine(AppContext.BaseDirectory, "startup.log");
        File.AppendAllText(logFile, $"[{DateTime.Now}] StartWorkerAsync (useGpu={useGpu})\n");
        var workerPath = Path.Combine(AppContext.BaseDirectory, "worker", AppDefaults.WorkerFileName);
        if (!File.Exists(workerPath))
        {
            File.AppendAllText(logFile, $"[{DateTime.Now}] Worker not found at {workerPath}\n");
            throw new InvalidOperationException(UiText.WorkerFailed);
        }
        var pipeName = "WhisperDictation-" + Guid.NewGuid().ToString("N");
        pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var startInfo = new ProcessStartInfo(workerPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(workerPath)!
        };
        foreach (var argument in new[] { pipeName, modelPath, useGpu ? "cuda" : "cpu", language,
                     outputMode.ToString(), Environment.ProcessId.ToString() })
            startInfo.ArgumentList.Add(argument);
        File.AppendAllText(logFile, $"[{DateTime.Now}] Starting worker process with model={modelPath}...\n");
        process = Process.Start(startInfo) ?? throw new InvalidOperationException(UiText.WorkerFailed);
        File.AppendAllText(logFile, $"[{DateTime.Now}] Worker process started (Pid={process.Id}). Waiting for pipe connection...\n");
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(AppDefaults.WorkerConnectTimeout);
        try { await pipe.WaitForConnectionAsync(connectTimeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            File.AppendAllText(logFile, $"[{DateTime.Now}] Worker pipe connection timed out!\n");
            throw new InvalidOperationException(UiText.WorkerFailed);
        }
        File.AppendAllText(logFile, $"[{DateTime.Now}] Worker pipe connected! Waiting for load response...\n");
        using var loadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loadTimeout.CancelAfter(AppDefaults.ModelLoadTimeout);
        WorkerResponse response;
        try { response = await WorkerProtocol.ReadMessageAsync(pipe, loadTimeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            File.AppendAllText(logFile, $"[{DateTime.Now}] Worker model load timed out!\n");
            throw new InvalidOperationException(UiText.WorkerFailed);
        }
        File.AppendAllText(logFile, $"[{DateTime.Now}] Worker response: Success={response.Success}, Engine={response.Engine}, Error={response.Error}\n");
        if (!response.Success)
            throw new InvalidOperationException(response.Error);
        usingGpu = useGpu;
        Engine = fellBack ? UiText.GpuFallback : response.Engine;
    }

    private async Task<TranscriptionResult> ProcessAudioAsync(float[] audio, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AppDefaults.TranscriptionTimeout);
        try
        {
            await WorkerProtocol.WriteAudioAsync(pipe!, audio, timeout.Token);
            var response = await WorkerProtocol.ReadMessageAsync(pipe!, timeout.Token);
            if (!response.Success)
                throw new InvalidOperationException(response.Error);
            return new(response.Text, Engine);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException(UiText.WorkerFailed); }
        catch (IOException) { throw new InvalidOperationException(UiText.WorkerFailed); }
    }

    public void Reset()
    {
        pipe?.Dispose();
        pipe = null;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose();
            process = null;
        }
        usingGpu = false;
        Engine = UiText.UnknownEngine;
    }

    public void Dispose() => Reset();
}
