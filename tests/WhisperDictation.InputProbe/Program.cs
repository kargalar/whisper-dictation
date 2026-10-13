using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WhisperDictation.Desktop.Services;

namespace WhisperDictation.InputProbe;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 1) return;
        var application = new Application();
        var textBox = new TextBox { AcceptsReturn = true, FontSize = 20, Margin = new Thickness(20) };
        var window = new Window
        {
            Title = "Whisper Dictation — input validation",
            Content = textBox,
            Width = 480,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Loaded += async (_, _) =>
        {
            using var pipe = new NamedPipeServerStream(args[0], PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            while (await reader.ReadLineAsync() is { } command)
            {
                if (command == "close") break;
                if (command == "capture")
                {
                    var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                    var currentThread = GetCurrentThreadId();
                    var attached = AttachThreadInput(currentThread, foregroundThread, true);
                    SetForegroundWindow(new WindowInteropHelper(window).Handle);
                    window.Activate();
                    Keyboard.Focus(textBox);
                    if (attached) AttachThreadInput(currentThread, foregroundThread, false);
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    var target = new TextOutputService().CaptureTarget();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        Window = (long)target.Window,
                        Focus = (long)target.Focus,
                        target.ProcessId,
                        Clipboard = ClipboardFingerprint()
                    }));
                }
                else if (command == "read")
                {
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { Text = textBox.Text, Clipboard = ClipboardFingerprint() }));
                }
            }
            window.Close();
        };
        application.Run(window);
    }

    private static string ClipboardFingerprint()
    {
        return Clipboard.ContainsText() ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Clipboard.GetText()))) : "";
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint firstThread, uint secondThread, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
