using System.Windows.Threading;
using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;
using Xunit;

namespace WhisperDictation.Tests;

public class MicrophoneTests
{
    [MicrophoneFact]
    public async Task ImmediateReleaseAndConcurrentStopAllowRecordingAgain()
    {
        await using var audio = new AudioCaptureService();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            audio.Start("");
            var firstStop = audio.StopAsync();
            var secondStop = audio.StopAsync();
            var results = await Task.WhenAll(firstStop, secondStop).WaitAsync(TimeSpan.FromSeconds(10));
            try { Assert.Empty(results[1]); }
            finally { foreach (var result in results) Array.Clear(result); }
        }
    }

    [MicrophoneFact]
    public async Task MicrophoneStopDoesNotDependOnCreationSynchronizationContext()
    {
        await using var audio = new AudioCaptureService();
        var previousContext = SynchronizationContext.Current;
        var inactiveContext = new InactiveSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(inactiveContext);
            audio.Start("");
            Assert.Same(inactiveContext, SynchronizationContext.Current);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        await Task.Delay(400);
        var samples = await audio.StopAsync();
        try { Assert.NotEmpty(samples); }
        finally { Array.Clear(samples); }
    }

    private sealed class InactiveSynchronizationContext : SynchronizationContext
    {
        // Represents a UI context whose message loop no longer dispatches capture notifications.
        public override void Post(SendOrPostCallback callback, object? state) { }
    }

    [MicrophoneFact]
    public async Task MicrophoneStopsAndRestartsOnWpfDispatcher()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    using var menu = new System.Windows.Forms.ContextMenuStrip();
                    using var tray = new System.Windows.Forms.NotifyIcon { ContextMenuStrip = menu };
                    await using var audio = new AudioCaptureService();
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        audio.Start("");
                        await Task.Delay(400);
                        var samples = await audio.StopAsync();
                        try { Assert.InRange(samples.Length, 1000, AppDefaults.SampleRate * 3); }
                        finally { Array.Clear(samples); }
                        Assert.Empty(await audio.StopAsync());
                    }
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            }));
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [MicrophoneFact]
    public async Task RealMicrophoneProducesMono16KhzAudioInMemory()
    {
        await using var audio = new AudioCaptureService();
        Assert.True(AudioCaptureService.GetMicrophones().Count > 1, "An active microphone is required.");
        audio.Start("");
        await Task.Delay(800);
        var samples = await audio.StopAsync();
        try
        {
            Assert.InRange(samples.Length, AppDefaults.SampleRate / 4, AppDefaults.SampleRate * 3);
            Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
            Assert.Empty(await audio.StopAsync());
        }
        finally { Array.Clear(samples); }
    }
}

public sealed class MicrophoneFactAttribute : FactAttribute
{
    public MicrophoneFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WHISPER_TEST_MICROPHONE") != "1")
            Skip = "Set WHISPER_TEST_MICROPHONE=1 to test a real microphone; audio is discarded.";
    }
}
