using System.Buffers.Binary;
using System.Text.Json;

namespace WhisperDictation.Core;

public sealed record WorkerResponse(bool Success, string Text = "", string Engine = "", string Error = "");

public static class WorkerProtocol
{
    public static async Task WriteMessageAsync(Stream stream, WorkerResponse response, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        if (bytes.Length > AppDefaults.MaximumMessageBytes)
            throw new IOException(UiText.WorkerFailed);
        await WriteBytesAsync(stream, bytes, token);
    }

    public static async Task<WorkerResponse> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        var bytes = await ReadBytesAsync(stream, AppDefaults.MaximumMessageBytes, token);
        return JsonSerializer.Deserialize<WorkerResponse>(bytes) ?? throw new IOException(UiText.WorkerFailed);
    }

    public static Task WriteAudioAsync(Stream stream, float[] audio, CancellationToken token)
    {
        var bytes = new byte[audio.Length * sizeof(float)];
        Buffer.BlockCopy(audio, 0, bytes, 0, bytes.Length);
        return WriteAndClearAsync(stream, bytes, token);
    }

    public static async Task<float[]> ReadAudioAsync(Stream stream, CancellationToken token)
    {
        var bytes = await ReadBytesAsync(stream, AppDefaults.MaximumAudioBytes, token);
        try
        {
            if (bytes.Length % sizeof(float) != 0)
                throw new IOException(UiText.WorkerFailed);
            var samples = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }
        finally { Array.Clear(bytes); }
    }

    private static async Task WriteAndClearAsync(Stream stream, byte[] bytes, CancellationToken token)
    {
        try { await WriteBytesAsync(stream, bytes, token); }
        finally { Array.Clear(bytes); }
    }

    private static async Task WriteBytesAsync(Stream stream, byte[] bytes, CancellationToken token)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, int maximumLength, CancellationToken token)
    {
        var prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length < 0 || length > maximumLength)
            throw new IOException(UiText.WorkerFailed);
        var bytes = new byte[length];
        try { await stream.ReadExactlyAsync(bytes, token); }
        catch { Array.Clear(bytes); throw; }
        return bytes;
    }
}
