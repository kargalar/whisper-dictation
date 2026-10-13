using System.Buffers.Binary;
using WhisperDictation.Core;
using Xunit;

namespace WhisperDictation.Tests;

public sealed class StorageAndProtocolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "WhisperDictationTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HistoryPersistsUnicodeAndSupportsIndividualAndCompleteDeletion()
    {
        var store = new HistoryStore(root);
        var first = await store.AddAsync(ModelKind.Turbo, "İı Şş Ğğ Öö Üü Çç SQL ' $text");
        var second = await store.AddAsync(ModelKind.Large, "API ve Windows testi.");
        var reopened = new HistoryStore(root);
        var entries = await reopened.ReadAsync();
        Assert.Equal(second, entries[0]);
        Assert.Equal(first, entries[1]);
        await reopened.DeleteAsync(first.Id);
        Assert.Equal(second, Assert.Single(await reopened.ReadAsync()));
        await reopened.ClearAsync();
        Assert.Empty(await new HistoryStore(root).ReadAsync());
    }

    [Fact]
    public void SettingsAndBundledModelsSurviveMovingTheApplication()
    {
        var store = new SettingsStore(root);
        var path = Path.Combine(root, "models", "ggml-large-v3.bin");
        var settings = new AppSettings
        {
            SelectedModel = ModelKind.Large,
            Hotkey = "Ctrl+Shift+F9",
            ComputeMode = ComputeMode.Cpu,
            MicrophoneId = "device-id",
            StartWithWindows = true
        };
        store.Save(settings);
        var reopened = new SettingsStore(root).Load();
        Assert.Equal(settings with { LegacyToggleHotkey = null }, reopened);
        Assert.Equal(path, store.GetModelPath(reopened.SelectedModel));
        var movedRoot = Path.Combine(root, "moved");
        Assert.Equal(Path.Combine(movedRoot, "models", "ggml-large-v3.bin"), new SettingsStore(movedRoot).GetModelPath(reopened.SelectedModel));
    }

    [Fact]
    public void LegacyManualModelPathsAreIgnoredWithoutLosingUserPreferences()
    {
        var store = new SettingsStore(root);
        Directory.CreateDirectory(store.DataDirectory);
        File.WriteAllText(Path.Combine(store.DataDirectory, "settings.json"), """
            { "SelectedModel": 1, "TurboModelPath": "C:/old/turbo.bin", "LargeModelPath": "C:/old/large.bin", "Hotkey": "Ctrl+Shift+F9" }
            """);
        var settings = store.Load();
        Assert.Equal(ModelKind.Large, settings.SelectedModel);
        Assert.Equal("Ctrl+Shift+F9", settings.Hotkey);
        Assert.Null(settings.LegacyToggleHotkey);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(Path.Combine(root, "models", "ggml-large-v3.bin"), store.GetModelPath(settings.SelectedModel));
    }

    [Fact]
    public void LegacyToggleShortcutBecomesTheSingleShortcut()
    {
        var store = new SettingsStore(root);
        Directory.CreateDirectory(store.DataDirectory);
        File.WriteAllText(Path.Combine(store.DataDirectory, "settings.json"), """
            { "Hotkey": "Ctrl+Alt+Space", "ToggleHotkey": "Ctrl+Alt+D" }
            """);

        var settings = store.Load();

        Assert.Equal("Ctrl+Alt+D", settings.Hotkey);
        Assert.Null(settings.LegacyToggleHotkey);
    }

    [Fact]
    public void CorruptSettingsRecoverDefaultsWithoutOverwritingOriginal()
    {
        var store = new SettingsStore(root);
        store.Save(new());
        var path = Path.Combine(store.DataDirectory, "settings.json");
        File.WriteAllText(path, "broken-json");
        Assert.Equal(new AppSettings(), store.Load());
        Assert.True(store.RecoveredDefaults);
        Assert.Equal("broken-json", File.ReadAllText(path));
    }

    [Fact]
    public async Task BinaryAudioAndEnglishResponseRoundTrip()
    {
        using var stream = new MemoryStream();
        var samples = new[] { 0f, -0.5f, 0.1f, 1f };
        await WorkerProtocol.WriteAudioAsync(stream, samples, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(samples, await WorkerProtocol.ReadAudioAsync(stream, CancellationToken.None));
        stream.SetLength(0);
        var response = new WorkerResponse(true, "English: speech recognition works.", "CPU");
        await WorkerProtocol.WriteMessageAsync(stream, response, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(response, await WorkerProtocol.ReadMessageAsync(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AppDefaults.MaximumAudioBytes + 1)]
    public async Task InvalidFrameLengthsAreRejectedBeforeAllocating(int length)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        using var stream = new MemoryStream(prefix);
        await Assert.ThrowsAsync<IOException>(() => WorkerProtocol.ReadAudioAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedFramesFailInsteadOfReturningPartialAudio()
    {
        using var stream = new MemoryStream();
        await WorkerProtocol.WriteAudioAsync(stream, [0.1f, 0.2f], CancellationToken.None);
        stream.SetLength(stream.Length - 1);
        stream.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => WorkerProtocol.ReadAudioAsync(stream, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
