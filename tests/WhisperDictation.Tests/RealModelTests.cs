using System.Diagnostics;
using NAudio.Wave;
using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;
using Xunit;
using Xunit.Abstractions;

namespace WhisperDictation.Tests;

public class RealModelTests(ITestOutputHelper output)
{
    [RealModelTheory]
    [InlineData(ModelKind.Turbo, ComputeMode.Automatic)]
    [InlineData(ModelKind.Turbo, ComputeMode.Cpu)]
    [InlineData(ModelKind.Large, ComputeMode.Automatic)]
    [InlineData(ModelKind.Large, ComputeMode.Cpu)]
    public async Task BundledModelLoadsAndTranscribesRealAudio(ModelKind model, ComputeMode mode)
    {
        var modelRoot = Environment.GetEnvironmentVariable("WHISPER_TEST_MODELS")!;
        var audioPath = Environment.GetEnvironmentVariable("WHISPER_TEST_AUDIO")!;
        var modelPath = Path.Combine(modelRoot, ModelChoice.All.Single(choice => choice.Kind == model).FileName);
        using var engine = new WorkerTranscriber();
        var timer = Stopwatch.StartNew();
        await engine.PrepareAsync(modelPath, mode, CancellationToken.None);
        output.WriteLine($"{model}/{mode}: loaded in {timer.Elapsed.TotalSeconds:F2}s, engine={engine.Engine}");
        Assert.True(engine.IsReady);
        Assert.Equal(mode == ComputeMode.Automatic ? UiText.GpuEngine : UiText.CpuEngine, engine.Engine);

        using var reader = new WaveFileReader(audioPath);
        Assert.Equal(AppDefaults.SampleRate, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        var bytes = new byte[(int)reader.Length];
        reader.ReadExactly(bytes);
        var samples = new float[bytes.Length / sizeof(short)];
        const float pcmAmplitudeScale = 32768f;
        for (var index = 0; index < samples.Length; index++)
            samples[index] = BitConverter.ToInt16(bytes, index * sizeof(short)) / pcmAmplitudeScale;
        Array.Clear(bytes);
        try
        {
            timer.Restart();
            var result = await engine.TranscribeAsync(samples, CancellationToken.None);
            Assert.Equal(mode == ComputeMode.Automatic ? UiText.GpuEngine : UiText.CpuEngine, result.Engine);
            output.WriteLine($"{model}/{mode}: decoded in {timer.Elapsed.TotalSeconds:F2}s; text={result.Text}");
            Assert.NotEmpty(result.Text);
            Assert.Contains("test", result.Text, StringComparison.OrdinalIgnoreCase);
            var silentResult = await engine.TranscribeAsync(new float[AppDefaults.SampleRate], CancellationToken.None);
            Assert.Empty(silentResult.Text);
        }
        finally { Array.Clear(samples); }
    }
}

public sealed class RealModelTheoryAttribute : TheoryAttribute
{
    public RealModelTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHISPER_TEST_MODELS"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHISPER_TEST_AUDIO")))
            Skip = "Set WHISPER_TEST_MODELS and WHISPER_TEST_AUDIO for real local model verification.";
    }
}
