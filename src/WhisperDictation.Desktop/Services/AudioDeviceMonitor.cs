using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace WhisperDictation.Desktop.Services;

public sealed class AudioDeviceMonitor : IDisposable
{
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out nint ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out nint ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out nint ppDevice);
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [ComImport]
    [Guid("7991EEC9-7ED8-48BF-8303-6638232A03EC")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.U4)] uint newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, nint key);
    }

    private sealed class NotificationSink(AudioDeviceMonitor monitor) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, uint newState)
        {
            try
            {
                if (newState != (uint)DeviceState.Active)
                    monitor.DeviceUnavailable?.Invoke(deviceId);
                monitor.DevicesChanged?.Invoke();
            }
            catch { }
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            try { monitor.DevicesChanged?.Invoke(); } catch { }
        }

        public void OnDeviceRemoved(string deviceId)
        {
            try
            {
                monitor.DeviceUnavailable?.Invoke(deviceId);
                monitor.DevicesChanged?.Invoke();
            }
            catch { }
        }

        public void OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
        {
            try
            {
                // DataFlow.Capture == 1
                if (flow == 1)
                    monitor.DevicesChanged?.Invoke();
            }
            catch { }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, nint key) { }
    }

    private readonly IMMDeviceEnumerator? enumerator;
    private readonly NotificationSink? sink;
    public event Action<string>? DeviceUnavailable;
    public event Action? DevicesChanged;

    public AudioDeviceMonitor()
    {
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            sink = new NotificationSink(this);
            enumerator.RegisterEndpointNotificationCallback(sink);
        }
        catch
        {
            enumerator = null;
            sink = null;
        }
    }

    public void Dispose()
    {
        if (enumerator is not null && sink is not null)
        {
            try { enumerator.UnregisterEndpointNotificationCallback(sink); } catch { }
            Marshal.ReleaseComObject(enumerator);
        }
    }
}

