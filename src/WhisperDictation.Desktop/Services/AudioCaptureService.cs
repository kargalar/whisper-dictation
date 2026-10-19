using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WhisperDictation.Core;

namespace WhisperDictation.Desktop.Services;

public sealed record MicrophoneChoice(string Id, string Name);

public sealed class AudioCaptureService : IAudioCapture, IAsyncDisposable
{
    private static readonly TimeSpan CaptureStartPollInterval = TimeSpan.FromMilliseconds(10);
    private readonly object sync = new();
    private readonly SemaphoreSlim stopGate = new(1, 1);
    private WasapiRecorder? recorder;
    private MMDevice? device;
    private PlaybackMuteScope? playbackMute;
    private MemoryStream? buffer;
    private TaskCompletionSource? stopped;
    private Exception? recordingError;
    private volatile bool stopping;
    private bool limitReported;
    public event Action? DeviceLost;
    public event Action? LimitReached;
    public event Action<float>? LevelChanged;

    public static IReadOnlyList<MicrophoneChoice> GetMicrophones()
    {
        using var enumerator = new MMDeviceEnumerator();
        var microphones = new List<MicrophoneChoice> { new("", UiText.DefaultMicrophone) };
        foreach (var item in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (item) microphones.Add(new(item.ID, item.FriendlyName));
        }
        return microphones;
    }

    public void Start(string microphoneId)
    {
        if (recorder is not null)
            throw new InvalidOperationException(UiText.MicrophoneFailed);
        try
        {
            playbackMute = PlaybackMuteScope.Begin();
            var builder = new WasapiRecorderBuilder()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(AppDefaults.SampleRate, 1));
            if (!string.IsNullOrEmpty(microphoneId))
            {
                using var enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDevice(microphoneId);
                if (device.State != DeviceState.Active)
                    throw new InvalidOperationException(UiText.MicrophoneFailed);
                builder.WithDevice(device);
            }
            buffer = new MemoryStream();
            stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
            recordingError = null;
            stopping = false;
            limitReported = false;
            recorder = BuildRecorder(builder);
            recorder.DataAvailable += OnDataAvailable;
            recorder.RecordingStopped += (_, args) =>
            {
                recordingError = args.Exception;
                stopped.TrySetResult();
                if (!stopping)
                    DeviceLost?.Invoke();
            };
            recorder.StartRecording();
        }
        catch
        {
            RestorePlayback();
            recorder?.Dispose();
            recorder = null;
            device?.Dispose();
            device = null;
            ClearBuffer();
            throw;
        }
    }

    private static WasapiRecorder BuildRecorder(WasapiRecorderBuilder builder)
    {
        // NAudio captures the current context and posts RecordingStopped to it. Capture
        // completion must remain available even if a WPF/tray message loop is busy or gone.
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            return builder.Build();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var reportLimit = false;
        float level = 0;
        lock (sync)
        {
            if (buffer is null)
                return;
            var remaining = AppDefaults.MaximumAudioBytes - (int)buffer.Length;
            var count = Math.Min(data.Length, remaining);
            // Keep float samples aligned if the final packet reaches the memory limit.
            count -= count % sizeof(float);
            var samples = MemoryMarshal.Cast<byte, float>(data[..count]);
            foreach (var sample in samples)
                level = Math.Max(level, Math.Abs(sample));
            if ((flags & AudioClientBufferFlags.Silent) != 0)
            {
                var silence = new byte[count];
                buffer.Write(silence);
                level = 0;
            }
            else buffer.Write(data[..count]);
            if (buffer.Length >= AppDefaults.MaximumAudioBytes && !limitReported)
            {
                limitReported = true;
                reportLimit = true;
            }
        }
        LevelChanged?.Invoke(Math.Clamp(level, 0, 1));
        if (reportLimit)
            LimitReached?.Invoke();
    }

    public async Task<float[]> StopAsync()
    {
        await stopGate.WaitAsync();
        try { return await StopCoreAsync(); }
        finally { stopGate.Release(); }
    }

    private async Task<float[]> StopCoreAsync()
    {
        if (recorder is null)
            return [];
        var activeRecorder = recorder;
        stopping = true;
        try
        {
            using var timeout = new CancellationTokenSource(AppDefaults.CaptureStopTimeout);
            // NAudio's capture thread changes Starting to Capturing unconditionally.
            // Wait for that transition so an immediate release cannot lose the stop request.
            while (activeRecorder.CaptureState == CaptureState.Starting)
                await Task.Delay(CaptureStartPollInterval, timeout.Token);
            activeRecorder.StopRecording();
            await stopped!.Task.WaitAsync(timeout.Token);
            if (recordingError is not null)
                throw new InvalidOperationException(UiText.MicrophoneLost);
            lock (sync)
            {
                var samples = new float[buffer!.Length / sizeof(float)];
                Buffer.BlockCopy(buffer.GetBuffer(), 0, samples, 0, (int)buffer.Length);
                return samples;
            }
        }
        catch (OperationCanceledException error)
        {
            throw new InvalidOperationException(UiText.MicrophoneStopFailed, error);
        }
        finally
        {
            RestorePlayback();
            await activeRecorder.DisposeAsync();
            recorder = null;
            device?.Dispose();
            device = null;
            RestorePlayback();
            ClearBuffer();
            LevelChanged?.Invoke(0);
        }
    }

    private void ClearBuffer()
    {
        lock (sync)
        {
            if (buffer is null) return;
            Array.Clear(buffer.GetBuffer());
            buffer.Dispose();
            buffer = null;
        }
    }

    private void RestorePlayback()
    {
        playbackMute?.Dispose();
        playbackMute = null;
    }

    public async ValueTask DisposeAsync()
    {
        try { Array.Clear(await StopAsync()); }
        catch (Exception) { ClearBuffer(); }
    }
}
