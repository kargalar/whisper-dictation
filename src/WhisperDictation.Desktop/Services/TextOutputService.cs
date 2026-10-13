using System.Runtime.InteropServices;
using WhisperDictation.Core;

namespace WhisperDictation.Desktop.Services;

public sealed class TextOutputService : ITextOutput
{
    private const int CharactersPerBatch = 32;
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(5);

    public TargetWindow CaptureTarget()
    {
        var window = NativeMethods.GetForegroundWindow();
        var threadId = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var info = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        NativeMethods.GetGUIThreadInfo(threadId, ref info);
        return new(window, info.Focus, processId);
    }

    public async Task<InsertionStatus> InsertAsync(TargetWindow target, string text, CancellationToken cancellationToken)
    {
        if (target.Window == 0 || target.ProcessId == Environment.ProcessId)
            return InsertionStatus.Blocked;
        var deadline = DateTime.UtcNow + ModifierReleaseTimeout;
        while (NativeMethods.ModifierKeys.Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0))
        {
            if (DateTime.UtcNow >= deadline) return InsertionStatus.Blocked;
            if (CaptureTarget() != target) return InsertionStatus.TargetChanged;
            await Task.Delay(20, cancellationToken);
        }
        var normalizedText = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        for (var offset = 0; offset < normalizedText.Length; offset += CharactersPerBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CaptureTarget() != target)
                return InsertionStatus.TargetChanged;
            if (NativeMethods.ModifierKeys.Any(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0))
                return InsertionStatus.Blocked;
            var length = Math.Min(CharactersPerBatch, normalizedText.Length - offset);
            var inputs = new NativeMethods.Input[length * 2];
            for (var index = 0; index < length; index++)
            {
                var character = normalizedText[offset + index];
                var keyboard = new NativeMethods.KeyboardInput
                {
                    ScanCode = character,
                    Flags = NativeMethods.KeyEventUnicode
                };
                inputs[index * 2] = new() { Type = NativeMethods.InputKeyboard, Data = new() { Keyboard = keyboard } };
                keyboard.Flags |= NativeMethods.KeyEventKeyUp;
                inputs[index * 2 + 1] = new() { Type = NativeMethods.InputKeyboard, Data = new() { Keyboard = keyboard } };
            }
            if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) != inputs.Length)
                return InsertionStatus.Blocked;
            // Yield between batches so the target can consume input and focus changes can be observed.
            await Task.Delay(1, cancellationToken);
        }
        return InsertionStatus.Inserted;
    }
}
