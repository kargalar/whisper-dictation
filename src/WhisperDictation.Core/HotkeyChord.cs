namespace WhisperDictation.Core;

[Flags]
public enum KeyModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

public sealed record HotkeyChord(KeyModifiers Modifiers, int VirtualKey)
{
    public const int SpaceKey = 0x20;
    public const int EscapeKey = 0x1B;
    public const int QuoteKey = 0x100;
    public const int FirstFunctionKey = 0x70;
    private const int LastFunctionKey = FirstFunctionKey + 23;
    private const KeyModifiers SupportedModifiers = KeyModifiers.Control | KeyModifiers.Alt
        | KeyModifiers.Shift | KeyModifiers.Windows;

    public static bool TryCreate(KeyModifiers modifiers, int virtualKey, out HotkeyChord? chord)
    {
        chord = null;
        if (modifiers == KeyModifiers.None || (modifiers & ~SupportedModifiers) != 0)
            return false;
        if (virtualKey != SpaceKey && virtualKey != QuoteKey && virtualKey is not (>= 0x30 and <= 0x39)
            and not (>= 0x41 and <= 0x5A) and not (>= FirstFunctionKey and <= LastFunctionKey))
            return false;
        chord = new(modifiers, virtualKey);
        return true;
    }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        parts.Add(VirtualKey switch
        {
            SpaceKey => "Space",
            QuoteKey => "\"",
            >= FirstFunctionKey and <= LastFunctionKey => "F" + (VirtualKey - FirstFunctionKey + 1),
            _ => ((char)VirtualKey).ToString()
        });
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" => KeyModifiers.Control,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                "WIN" => KeyModifiers.Windows,
                _ => KeyModifiers.None
            };
            if (modifier == KeyModifiers.None || modifiers.HasFlag(modifier))
                return false;
            modifiers |= modifier;
        }
        var key = parts[^1].ToUpperInvariant();
        var virtualKey = key == "SPACE" ? SpaceKey : 0;
        if (key == "\"")
            virtualKey = QuoteKey;
        else if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            virtualKey = key[0];
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var functionKey) && functionKey is >= 1 and <= 24)
            virtualKey = FirstFunctionKey + functionKey - 1;
        return TryCreate(modifiers, virtualKey, out chord);
    }
}

public enum HotkeyAction { None, Stop, Toggle, Cancel }
public readonly record struct HotkeyTransition(HotkeyAction Action, bool Suppress);

public sealed class HotkeyStateMachine(HotkeyChord chord)
{
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(300);
    public bool IsHeld { get; private set; }
    private bool suppressKeyUntilRelease;

    public HotkeyTransition Process(int key, bool isDown, KeyModifiers modifiers, TimeSpan heldDuration = default)
    {
        if (key == HotkeyChord.EscapeKey && isDown && IsHeld)
        {
            IsHeld = false;
            return new(HotkeyAction.Cancel, true);
        }
        if (key == chord.VirtualKey)
        {
            if (!isDown)
            {
                var action = IsHeld && heldDuration >= HoldThreshold ? HotkeyAction.Stop : HotkeyAction.None;
                var suppress = suppressKeyUntilRelease;
                IsHeld = false;
                suppressKeyUntilRelease = false;
                return new(action, suppress);
            }
            if (suppressKeyUntilRelease)
                return new(HotkeyAction.None, true);
            if (modifiers == chord.Modifiers)
            {
                suppressKeyUntilRelease = true;
                IsHeld = true;
                return new(HotkeyAction.Toggle, true);
            }
        }
        if (IsHeld && (modifiers & chord.Modifiers) != chord.Modifiers && heldDuration >= HoldThreshold)
        {
            IsHeld = false;
            return new(HotkeyAction.Stop, false);
        }
        return new(HotkeyAction.None, false);
    }
}
