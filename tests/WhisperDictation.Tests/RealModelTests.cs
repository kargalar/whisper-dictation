using System.Diagnostics;
using NAudio.Wave;
using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;
using Xunit;
using Xunit.Abstractions;

namespace WhisperDictation.Tests;

public class RealModelTests(ITestOutputHelper output)
{
    [TurkishModelTheory]
    [InlineData(ModelKind.Turbo, OutputMode.Transcribe, "tr")]
    [InlineData(ModelKind.Turbo, OutputMode.Transcribe, "auto")]
    [InlineData(ModelKind.Large, OutputMode.Transcribe, "tr")]
    [InlineData(ModelKind.Large, OutputMode.TranslateToEnglish, "tr")]
    [InlineData(ModelKind.Turbo, OutputMode.TranslateToEnglish, "tr")]
    public async Task TurkishSpeechUsesChosenOutputLanguage(ModelKind model, OutputMode outputMode, string language)
    {
        // FLEURS tr_tr test sample: "Aynı ay, başka bir yolcu uçağı ... bir duvara çarptı ..."
        var settings = new AppSettings { SelectedModel = model, OutputMode = outputMode }.WithCompatibleModel();
        var modelPath = Path.Combine(Environment.GetEnvironmentVariable("WHISPER_TEST_MODELS")!,
            ModelChoice.All.Single(choice => choice.Kind == settings.SelectedModel).FileName);
        using var reader = new WaveFileReader(Environment.GetEnvironmentVariable("WHISPER_TEST_TURKISH_AUDIO")!);
        Assert.Equal(AppDefaults.SampleRate, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        var provider = reader.ToSampleProvider();
        var samples = new float[reader.SampleCount];
        Assert.Equal(samples.Length, provider.Read(samples));
        using var engine = new WorkerTranscriber();
        try
        {
            await engine.PrepareAsync(modelPath, ComputeMode.Cpu, language, outputMode, CancellationToken.None);
            var result = await engine.TranscribeAsync(samples, CancellationToken.None);
            output.WriteLine($"Requested {model}; used {settings.SelectedModel}/{language}/{outputMode}: {result.Text}");
            Assert.Contains(outputMode == OutputMode.Transcribe ? "duvar" : "wall", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(outputMode == OutputMode.Transcribe ? "yolcu uçağı" : "passenger plane", result.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("17", result.Text);
            // Reuse the worker to cover language/task state leaking into subsequent recordings.
            var repeated = await engine.TranscribeAsync(samples, CancellationToken.None);
            Assert.Equal(result.Text, repeated.Text);
        }
        finally { Array.Clear(samples); }
    }

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
        await engine.PrepareAsync(modelPath, mode, AppDefaults.DefaultLanguage, OutputMode.Transcribe, CancellationToken.None);
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

public sealed class TurkishModelTheoryAttribute : TheoryAttribute
{
    public TurkishModelTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHISPER_TEST_MODELS"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHISPER_TEST_TURKISH_AUDIO")))
            Skip = "Set WHISPER_TEST_MODELS and WHISPER_TEST_TURKISH_AUDIO to the FLEURS Turkish airplane/wall sample.";
    }
}
