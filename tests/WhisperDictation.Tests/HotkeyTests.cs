using WhisperDictation.Core;
using Xunit;

namespace WhisperDictation.Tests;

public class HotkeyTests
{
    [Fact]
    public void DefaultShortcutIsShiftSpace()
    {
        Assert.Equal("Shift+Space", AppDefaults.DefaultHotkey);
        Assert.True(HotkeyChord.TryParse(AppDefaults.DefaultHotkey, out var chord));
        Assert.Equal(new(KeyModifiers.Shift, HotkeyChord.SpaceKey), chord);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space", KeyModifiers.Control | KeyModifiers.Alt, HotkeyChord.SpaceKey)]
    [InlineData(" ctrl + shift + f9 ", KeyModifiers.Control | KeyModifiers.Shift, 0x78)]
    [InlineData("Win+Z", KeyModifiers.Windows, 0x5A)]
    [InlineData("Alt+1", KeyModifiers.Alt, 0x31)]
    [InlineData("Alt+\"", KeyModifiers.Alt, HotkeyChord.QuoteKey)]
    public void ParsesConfiguredChord(string text, KeyModifiers modifiers, int key)
    {
        Assert.True(HotkeyChord.TryParse(text, out var chord));
        Assert.Equal(new(modifiers, key), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Ctrl+Ctrl+Space")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Alt+Ö")]
    [InlineData("Ctrl++A")]
    public void RejectsInvalidChord(string text) => Assert.False(HotkeyChord.TryParse(text, out _));

    [Theory]
    [InlineData(KeyModifiers.Control, 0x45, "Ctrl+E")]
    [InlineData(KeyModifiers.Control, 0x41, "Ctrl+A")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, 0x78, "Ctrl+Shift+F9")]
    [InlineData(KeyModifiers.Alt, HotkeyChord.QuoteKey, "Alt+\"")]
    public void PressedShortcutFormatsAndReloads(KeyModifiers modifiers, int key, string display)
    {
        Assert.True(HotkeyChord.TryCreate(modifiers, key, out var chord));
        Assert.Equal(display, chord!.ToDisplayString());
        Assert.True(HotkeyChord.TryParse(display, out var reloaded));
        Assert.Equal(chord, reloaded);
    }

    [Theory]
    [InlineData(KeyModifiers.None, 0x41)]
    [InlineData(KeyModifiers.Control, HotkeyChord.EscapeKey)]
    [InlineData(KeyModifiers.Control, 0x61)]
    public void CaptureRejectsUnsupportedKeys(KeyModifiers modifiers, int key) =>
        Assert.False(HotkeyChord.TryCreate(modifiers, key, out _));

    [Fact]
    public void QuoteShortcutStartsAndStopsUsingItsLayoutIndependentKey()
    {
        var chord = new HotkeyChord(KeyModifiers.Alt, HotkeyChord.QuoteKey);
        var machine = new HotkeyStateMachine(chord);

        Assert.Equal(new(HotkeyAction.Toggle, true), machine.Process(HotkeyChord.QuoteKey, true, KeyModifiers.Alt));
        Assert.Equal(new(HotkeyAction.Stop, true), machine.Process(HotkeyChord.QuoteKey, false, KeyModifiers.Alt, HotkeyStateMachine.HoldThreshold));
    }

    [Fact]
    public void ChangingShortcutIgnoresHeldAndRepeatedKeysUntilReleased()
    {
        var gate = new HotkeyCaptureGate();
        gate.BeginCapture();
        Assert.False(gate.Observe(0x11, true));
        Assert.False(gate.Observe(0x41, true));
        gate.EndCapture();
        Assert.False(gate.Observe(0x41, true));
        Assert.False(gate.Observe(0x41, false));
        Assert.False(gate.Observe(0x11, false));
        Assert.True(gate.Observe(0x11, true));
        Assert.True(gate.Observe(0x41, true));
    }

    [Fact]
    public void ShortPressTogglesWithoutStoppingOnRelease()
    {
        var machine = CreateMachine();
        Assert.Equal(new(HotkeyAction.Toggle, true), machine.Process(0x20, true, Modifiers));
        Assert.Equal(new(HotkeyAction.None, true), machine.Process(0x20, true, Modifiers));
        Assert.Equal(new(HotkeyAction.None, true), machine.Process(0x20, false, Modifiers, TimeSpan.FromMilliseconds(100)));
        Assert.False(machine.IsHeld);
        Assert.Equal(new(HotkeyAction.Toggle, true), machine.Process(0x20, true, Modifiers));
    }

    [Fact]
    public void HoldingShortcutStopsWhenReleased()
    {
        var machine = new HotkeyStateMachine(new(Modifiers, 0x44));
        Assert.Equal(new(HotkeyAction.Toggle, true), machine.Process(0x44, true, Modifiers));
        Assert.Equal(new(HotkeyAction.None, true), machine.Process(0x44, true, Modifiers));
        Assert.Equal(new(HotkeyAction.Stop, true), machine.Process(0x44, false, Modifiers, HotkeyStateMachine.HoldThreshold));
    }

    [Fact]
    public void ReleasingModifierStopsAndSuppressesRemainingHeldKey()
    {
        var machine = CreateMachine();
        machine.Process(0x20, true, Modifiers);
        Assert.Equal(HotkeyAction.Stop, machine.Process(0x11, false, KeyModifiers.Alt, HotkeyStateMachine.HoldThreshold).Action);
        Assert.True(machine.Process(0x20, true, KeyModifiers.Alt).Suppress);
        Assert.True(machine.Process(0x20, false, KeyModifiers.Alt).Suppress);
        Assert.False(machine.Process(0x20, true, KeyModifiers.None).Suppress);
    }

    [Fact]
    public void EscapeCancelsAndHeldShortcutCannotRestartUntilReleased()
    {
        var machine = CreateMachine();
        machine.Process(0x20, true, Modifiers);
        Assert.Equal(HotkeyAction.Cancel, machine.Process(HotkeyChord.EscapeKey, true, Modifiers).Action);
        Assert.Equal(new(HotkeyAction.None, true), machine.Process(0x20, true, Modifiers));
        machine.Process(0x20, false, Modifiers);
        Assert.Equal(HotkeyAction.Toggle, machine.Process(0x20, true, Modifiers).Action);
    }

    private const KeyModifiers Modifiers = KeyModifiers.Control | KeyModifiers.Alt;
    private static HotkeyStateMachine CreateMachine() => new(new(Modifiers, HotkeyChord.SpaceKey));
}
