namespace WhisperDictation.Core;

public sealed class HotkeyCaptureGate
{
    private readonly HashSet<int> pressed = [];
    private bool waitingForRelease;
    private bool capturing;
    public bool IsCapturing => capturing;

    public void BeginCapture() => capturing = true;

    public void EndCapture()
    {
        capturing = false;
        waitingForRelease = pressed.Count > 0;
    }

    public void Reset()
    {
        pressed.Clear();
        capturing = false;
        waitingForRelease = false;
    }

    public bool HasAny(params int[] keys) => keys.Any(pressed.Contains);

    public bool Observe(int key, bool isDown)
    {
        if (isDown) pressed.Add(key);
        else pressed.Remove(key);
        if (capturing)
            return false;
        if (!waitingForRelease)
            return true;
        if (pressed.Count == 0)
            waitingForRelease = false;
        return false;
    }
}
