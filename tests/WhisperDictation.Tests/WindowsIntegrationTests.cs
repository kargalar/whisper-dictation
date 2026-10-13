using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;
using WhisperDictation.Desktop.ViewModels;
using Xunit;

namespace WhisperDictation.Tests;

public class WindowsIntegrationTests
{
    [Fact]
    public void StartupCommandQuotesExecutablePathAndStartsInBackground()
    {
        var command = WindowsStartupService.BuildRunCommand("C:\\Program Files\\Whisper Dictation\\WhisperDictation.exe");

        Assert.Equal("\"C:\\Program Files\\Whisper Dictation\\WhisperDictation.exe\" --background", command);
    }

    [Fact]
    public async Task CapturedShortcutImmediatelyAppliesAndPersists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Whisper-hotkey-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var settings = store.Load();
            var history = new HistoryStore(store.DataDirectory);
            HotkeyChord.TryParse(settings.Hotkey, out var chord);
            using var hotkeys = new GlobalHotkeyService(chord!);
            using var engine = new WorkerTranscriber();
            await using var microphone = new AudioCaptureService();
            var controller = new DictationController(microphone, engine, new TextOutputService(), history);
            var viewModel = new MainViewModel(store, history, controller, engine, hotkeys, settings);

            viewModel.BeginHotkeyCapture();
            Assert.True(viewModel.IsCapturingHotkey);
            Assert.Equal(UiText.HotkeyCapturePrompt, viewModel.HotkeyCaptureDisplay);
            viewModel.CaptureHotkey(KeyModifiers.Control, 0x45);
            Assert.False(viewModel.IsCapturingHotkey);
            Assert.Equal("Ctrl+E", viewModel.ActiveHotkey);
            Assert.Equal("Ctrl+E", store.Load().Hotkey);

            viewModel.BeginHotkeyCapture();
            viewModel.CaptureHotkey(KeyModifiers.Control, 0x41);
            Assert.Equal("Ctrl+A", viewModel.ActiveHotkey);
            Assert.Equal("Ctrl+A", store.Load().Hotkey);

            viewModel.BeginHotkeyCapture();
            viewModel.CaptureHotkey(KeyModifiers.Control, 0x54);
            Assert.Equal("Ctrl+T", viewModel.ActiveHotkey);
            Assert.Equal("Ctrl+T", store.Load().Hotkey);

            viewModel.BeginHotkeyCapture();
            viewModel.CaptureHotkey(KeyModifiers.Alt, HotkeyChord.QuoteKey);
            Assert.Equal("Alt+\"", viewModel.ActiveHotkey);
            Assert.Equal("Alt+\"", store.Load().Hotkey);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidModelFailsInsideWorkerAndLeavesHostAlive()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), "Whisper-invalid-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(modelPath, new byte[64]);
        try
        {
            using var engine = new WorkerTranscriber();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                engine.PrepareAsync(modelPath, ComputeMode.Cpu, CancellationToken.None));
            Assert.Equal(UiText.InvalidModel, error.Message);
            Assert.False(engine.IsReady);
        }
        finally { File.Delete(modelPath); }
    }

    [Fact]
    public async Task MissingModelDoesNotStartWorker()
    {
        using var engine = new WorkerTranscriber();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.PrepareAsync("missing-model.bin", ComputeMode.Automatic, CancellationToken.None));
        Assert.Equal(UiText.MissingModel, error.Message);
        Assert.False(engine.IsReady);
    }

    [WindowsInputFact]
    public async Task UnicodeInputReachesSeparateWindowsProcess()
    {
        var pipeName = "WhisperInputProbe-" + Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "probe", "WhisperDictation.InputProbe.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(pipeName);
        using var process = Process.Start(startInfo)!;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("capture");
            var captured = JsonSerializer.Deserialize<ProbeTarget>((await reader.ReadLineAsync(timeout.Token))!)!;
            for (var attempt = 0; captured.ProcessId != process.Id && attempt < 10; attempt++)
            {
                await Task.Delay(100, timeout.Token);
                await writer.WriteLineAsync("capture");
                captured = JsonSerializer.Deserialize<ProbeTarget>((await reader.ReadLineAsync(timeout.Token))!)!;
            }
            Assert.Equal((uint)process.Id, captured.ProcessId);
            var target = new TargetWindow((nint)captured.Window, (nint)captured.Focus, captured.ProcessId);
            const string text = "İstanbul: ığüşöç İĞÜŞÖÇ, API testi.";
            var result = await new TextOutputService().InsertAsync(target, text, timeout.Token);
            Assert.Equal(InsertionStatus.Inserted, result);
            await writer.WriteLineAsync("read");
            var content = JsonSerializer.Deserialize<ProbeContent>((await reader.ReadLineAsync(timeout.Token))!)!;
            Assert.Equal(text, content.Text);
            Assert.True(captured.Clipboard == content.Clipboard, "Unicode injection changed the clipboard.");
            var wrongTarget = target with { Window = 0x1234 };
            Assert.Equal(InsertionStatus.TargetChanged, await new TextOutputService().InsertAsync(wrongTarget, "yanlış", timeout.Token));
            await writer.WriteLineAsync("close");
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private sealed record ProbeTarget(long Window, long Focus, uint ProcessId, string Clipboard);
    private sealed record ProbeContent(string Text, string Clipboard);
}

public sealed class WindowsInputFactAttribute : FactAttribute
{
    public WindowsInputFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WHISPER_TEST_INPUT") != "1")
            Skip = "Set WHISPER_TEST_INPUT=1 in an interactive desktop session to test foreground input.";
    }
}
