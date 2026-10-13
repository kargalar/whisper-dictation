using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WhisperDictation.Core;

namespace WhisperDictation.Desktop.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly NativeMethods.HookCallback callback;
    private readonly HotkeyCaptureGate captureGate = new();
    private nint hook;
    private HotkeyChord chord;
    private HotkeyStateMachine machine;
    private long pressTimestamp;
    private int quotePhysicalKey;
    private bool suppressEscape;
    public bool CancellationAvailable { get; set; }
    public event Action<HotkeyAction>? Triggered;
    public event Action<KeyModifiers, int>? CaptureKeyPressed;
    public event Action? CaptureCancelled;

    public GlobalHotkeyService(HotkeyChord chord)
    {
        this.chord = chord;
        machine = new(chord);
        callback = HandleKeyboard;
        hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, callback,
            NativeMethods.GetModuleHandle(null), 0);
        if (hook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Update(HotkeyChord newChord)
    {
        chord = newChord;
        EndCapture();
    }

    public void BeginCapture()
    {
        captureGate.BeginCapture();
        ResetMachines();
        suppressEscape = false;
    }

    public void EndCapture()
    {
        captureGate.EndCapture();
        ResetMachines();
        suppressEscape = false;
    }

    public void Reset()
    {
        ResetMachines();
        captureGate.Reset();
        suppressEscape = false;
    }

    private nint HandleKeyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
        if ((data.Flags & NativeMethods.LlkhfInjected) != 0)
            return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
        var message = (int)wParam;
        var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        if (!isDown && message is not (NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp))
            return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
        var physicalKey = (int)data.VirtualKey;
        var firstPress = isDown && !captureGate.HasAny(physicalKey);
        if (!captureGate.Observe(physicalKey, isDown))
        {
            if (captureGate.IsCapturing && firstPress)
            {
                if (physicalKey == HotkeyChord.EscapeKey)
                    CaptureCancelled?.Invoke();
                else if (!NativeMethods.ModifierKeys.Contains(physicalKey))
                {
                    var capturedModifiers = GetModifiers();
                    var capturedKey = NormalizeQuoteKey(physicalKey, isDown, ref capturedModifiers);
                    CaptureKeyPressed?.Invoke(capturedModifiers, capturedKey);
                }
            }
            return 1;
        }
        var modifiers = GetModifiers();
        var key = NormalizeQuoteKey(physicalKey, isDown, ref modifiers);
        if (isDown && key == chord.VirtualKey && !machine.IsHeld)
            pressTimestamp = Stopwatch.GetTimestamp();
        var heldDuration = pressTimestamp == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(pressTimestamp);
        var transition = machine.Process(key, isDown, modifiers, heldDuration);
        if (key == HotkeyChord.EscapeKey && ((isDown && CancellationAvailable) || suppressEscape))
        {
            suppressEscape = isDown;
            if (isDown) Triggered?.Invoke(HotkeyAction.Cancel);
            return 1;
        }
        if (transition.Action != HotkeyAction.None)
            Triggered?.Invoke(transition.Action);
        return transition.Suppress
            ? 1
            : NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
    }

    private void ResetMachines()
    {
        machine = new(chord);
        pressTimestamp = 0;
        quotePhysicalKey = 0;
    }

    private int NormalizeQuoteKey(int physicalKey, bool isDown, ref KeyModifiers modifiers)
    {
        if (!isDown && quotePhysicalKey == physicalKey)
        {
            quotePhysicalKey = 0;
            modifiers &= ~KeyModifiers.Shift;
            return HotkeyChord.QuoteKey;
        }

        if (!captureGate.IsCapturing && chord.VirtualKey != HotkeyChord.QuoteKey)
            return physicalKey;

        if (!modifiers.HasFlag(KeyModifiers.Shift))
            return physicalKey;

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var foregroundThread = foregroundWindow == 0
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var layout = NativeMethods.GetKeyboardLayout(foregroundThread);
        var mapping = NativeMethods.VkKeyScanEx('"', layout);
        if (mapping == -1 || (mapping & 0xFF) != physicalKey || ((mapping >> 8) & 0xFF) != 1)
            return physicalKey;

        quotePhysicalKey = physicalKey;
        modifiers &= ~KeyModifiers.Shift;
        return HotkeyChord.QuoteKey;
    }

    private KeyModifiers GetModifiers()
    {
        var value = KeyModifiers.None;
        if (HasAny(NativeMethods.ControlKey, NativeMethods.LeftControlKey, NativeMethods.RightControlKey)) value |= KeyModifiers.Control;
        if (HasAny(NativeMethods.AltKey, NativeMethods.LeftAltKey, NativeMethods.RightAltKey)) value |= KeyModifiers.Alt;
        if (HasAny(NativeMethods.ShiftKey, NativeMethods.LeftShiftKey, NativeMethods.RightShiftKey)) value |= KeyModifiers.Shift;
        if (HasAny(NativeMethods.LeftWindowsKey, NativeMethods.RightWindowsKey)) value |= KeyModifiers.Windows;
        return value;
    }

    private bool HasAny(params int[] keys) => captureGate.HasAny(keys);

    public void Dispose()
    {
        if (hook == 0) return;
        NativeMethods.UnhookWindowsHookEx(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }
}
