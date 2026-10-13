using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WhisperDictation.Core;
using WhisperDictation.Desktop.ViewModels;

namespace WhisperDictation.Desktop;

public partial class MainWindow : Window
{
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(
            AppContext.BaseDirectory,
            AppDefaults.AppAssetsDirectoryName,
            AppDefaults.AppIconFileName);
        if (File.Exists(iconPath))
        {
            Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
            TitleIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        }
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void WindowStateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = isMaximized ? "▣" : "□";
        MaximizeButton.ToolTip = isMaximized ? UiText.RestoreWindow : UiText.MaximizeWindow;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            (DataContext as MainViewModel)?.CancelHotkeyCapture();
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void BeginHotkeyCapture(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.BeginHotkeyCapture();
        (sender as Button)?.Focus();
    }

    private void CancelHotkeyCapture(object sender, KeyboardFocusChangedEventArgs e) =>
        (DataContext as MainViewModel)?.CancelHotkeyCapture();
}
