using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WhisperDictation.Core;

namespace WhisperDictation.Desktop;

public partial class OverlayWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int TransparentStyle = 0x00000020;
    private const int ToolWindowStyle = 0x00000080;

    public OverlayWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(
            AppContext.BaseDirectory,
            AppDefaults.AppAssetsDirectoryName,
            AppDefaults.AppIconFileName);
        if (File.Exists(iconPath))
            ProductIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(handle, ExtendedStyleIndex, GetWindowLongPtr(handle, ExtendedStyleIndex)
            | NoActivateStyle | TransparentStyle | ToolWindowStyle);
    }

    public void Display()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 24;
        Show();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
