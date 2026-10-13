using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;
using WhisperDictation.Core;

namespace WhisperDictation.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--diagnose", var backend])
            return Diagnose(backend);
        if (args is ["--diagnose-model", var modelBackend, var diagnosticModel])
            return DiagnoseModel(modelBackend, diagnosticModel);
        if (args.Length != 4 || !int.TryParse(args[3], out var parentId))
            return 2;
        _ = WatchParentAsync(parentId);
        var useGpu = args[2] == "cuda";
        ConfigureNativePath();
        RuntimeOptions.RuntimeLibraryOrder = useGpu ? [RuntimeLibrary.Cuda] : [RuntimeLibrary.Cpu];
        using var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var connectTimeout = new CancellationTokenSource(AppDefaults.WorkerConnectTimeout);
            await pipe.ConnectAsync(connectTimeout.Token);
            var gpuBackendUsed = false;
            using var logger = LogProvider.AddLogger((_, message) =>
            {
                // Native logs are observed only in memory; no audio or transcript is logged.
                if (message is not null && message.Contains("CUDA0", StringComparison.OrdinalIgnoreCase)
                    && (message.Contains("buffer", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("using", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("total size", StringComparison.OrdinalIgnoreCase)))
                    gpuBackendUsed = true;
            });
            using var factory = WhisperFactory.FromPath(args[1], new WhisperFactoryOptions
            {
                UseGpu = useGpu,
                UseFlashAttention = useGpu,
                GpuDevice = 0
            });
            using var processor = factory.CreateBuilder()
                .WithLanguage(AppDefaults.Language)
                .WithNoContext()
                .WithTemperature(0)
                .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
                .Build();
            if (useGpu && (!gpuBackendUsed || RuntimeOptions.LoadedLibrary != RuntimeLibrary.Cuda))
                throw new InvalidOperationException(UiText.GpuFallback);
            var engine = useGpu ? UiText.GpuEngine : UiText.CpuEngine;
            await WorkerProtocol.WriteMessageAsync(pipe, new(true, Engine: engine), CancellationToken.None);
            while (pipe.IsConnected)
            {
                var samples = await WorkerProtocol.ReadAudioAsync(pipe, CancellationToken.None);
                try
                {
                    var text = new StringBuilder();
                    if (DictationController.ContainsSpeech(samples))
                    {
                        await foreach (var segment in processor.ProcessAsync(samples))
                        {
                            var trimmed = segment.Text.Trim();
                            if (trimmed.Length == 0)
                                continue;
                            if (text.Length > 0)
                                text.Append(' ');
                            text.Append(trimmed);
                        }
                    }
                    await WorkerProtocol.WriteMessageAsync(pipe, new(true, text.ToString(), engine), CancellationToken.None);
                }
                finally { Array.Clear(samples); }
            }
            return 0;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException)
        {
            return 1;
        }
        catch (Exception)
        {
            try
            {
                if (pipe.IsConnected)
                    await WorkerProtocol.WriteMessageAsync(pipe, new(false, Error: UiText.InvalidModel), CancellationToken.None);
            }
            catch (IOException) { }
            return 1;
        }
    }

    private static int Diagnose(string backend)
    {
        try
        {
            ConfigureNativePath();
            RuntimeOptions.RuntimeLibraryOrder = backend == "cuda" ? [RuntimeLibrary.Cuda] : [RuntimeLibrary.Cpu];
            using var logger = LogProvider.AddLogger((_, _) => { });
            var runtimeInfo = WhisperFactory.GetRuntimeInfo();
            Console.WriteLine(JsonSerializer.Serialize(new { Success = true, Backend = RuntimeOptions.LoadedLibrary.ToString(), RuntimeInfo = runtimeInfo }));
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Success = false, Error = UiText.WorkerFailed }));
            return 1;
        }
    }

    private static int DiagnoseModel(string backend, string modelPath)
    {
        try
        {
            ConfigureNativePath();
            var useGpu = backend == "cuda";
            RuntimeOptions.RuntimeLibraryOrder = useGpu ? [RuntimeLibrary.Cuda] : [RuntimeLibrary.Cpu];
            using var logger = LogProvider.AddConsoleLogging(WhisperLogLevel.Debug);
            using var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu, UseFlashAttention = useGpu, GpuDevice = 0 });
            using var processor = factory.CreateBuilder().WithLanguage(AppDefaults.Language).Build();
            Console.WriteLine(JsonSerializer.Serialize(new { Success = true, Backend = RuntimeOptions.LoadedLibrary.ToString() }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Success = false, Error = ex.GetType().Name }));
            return 1;
        }
    }

    private static void ConfigureNativePath()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        var paths = new List<string> { AppContext.BaseDirectory };
        if (!string.IsNullOrEmpty(cudaPath))
        {
            paths.Add(Path.Combine(cudaPath, "bin", "x64"));
            paths.Add(Path.Combine(cudaPath, "bin"));
        }
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, paths) + Path.PathSeparator
            + Environment.GetEnvironmentVariable("PATH"));
    }

    private static async Task WatchParentAsync(int parentId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentId);
            await parent.WaitForExitAsync();
        }
        catch (ArgumentException) { }
        Environment.Exit(0);
    }
}
