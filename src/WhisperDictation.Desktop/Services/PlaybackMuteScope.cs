using NAudio.CoreAudioApi;

namespace WhisperDictation.Desktop.Services;

internal sealed class PlaybackMuteScope : IDisposable
{
    private List<(MMDevice Device, bool WasMuted)>? changedDevices = [];

    private PlaybackMuteScope() { }

    public static PlaybackMuteScope Begin()
    {
        var scope = new PlaybackMuteScope();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var retained = false;
                try
                {
                    var wasMuted = endpoint.AudioEndpointVolume.Mute;
                    if (wasMuted)
                        continue;

                    endpoint.AudioEndpointVolume.Mute = true;
                    scope.changedDevices!.Add((endpoint, wasMuted));
                    retained = true;
                }
                catch (Exception)
                {
                    // An unavailable or unsupported output must not prevent microphone capture.
                }
                finally
                {
                    if (!retained)
                        endpoint.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // Audio output control is best effort; dictation should still work without it.
        }

        return scope;
    }

    public void Dispose()
    {
        var devices = Interlocked.Exchange(ref changedDevices, null);
        if (devices is null)
            return;

        foreach (var (device, wasMuted) in devices)
        {
            try { device.AudioEndpointVolume.Mute = wasMuted; }
            catch (Exception) { /* The output may have been disconnected during recording. */ }
            finally { device.Dispose(); }
        }
    }
}
