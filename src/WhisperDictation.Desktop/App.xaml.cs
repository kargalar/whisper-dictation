using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;
using WhisperDictation.Desktop.ViewModels;
using Forms = System.Windows.Forms;

namespace WhisperDictation.Desktop;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private bool ownsMutex;
    private bool exiting;
    private GlobalHotkeyService? hotkeys;
    private AudioCaptureService? audio;
    private WorkerTranscriber? transcriber;
    private Forms.NotifyIcon? tray;
    private Icon? appIcon;
    private MainWindow? window;
    private OverlayWindow? overlay;
    private MainViewModel? viewModel;
    private DispatcherTimer? overlayTimer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startInBackground = e.Args.Contains(AppDefaults.BackgroundLaunchArgument, StringComparer.OrdinalIgnoreCase);
        instanceMutex = new Mutex(true, "Local\\WhisperDictation", out ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show(UiText.AlreadyRunning, UiText.Title);
            Shutdown();
            return;
        }
        try
        {
            var store = new SettingsStore(AppContext.BaseDirectory);
            var settings = store.Load();
            var history = new HistoryStore(store.DataDirectory);
            HotkeyChord.TryParse(settings.Hotkey, out var chord);
            hotkeys = new(chord!);
            audio = new();
            transcriber = new();
            var controller = new DictationController(audio, transcriber, new TextOutputService(), history);
            viewModel = new(store, history, controller, transcriber, hotkeys, settings);
            window = new() { DataContext = viewModel };
            MainWindow = window;
            overlay = new() { DataContext = viewModel };
            overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            overlayTimer.Tick += (_, _) => { overlayTimer.Stop(); if (!viewModel.IsBusy) overlay.Hide(); };
            viewModel.PropertyChanged += OnViewModelChanged;
            hotkeys.Triggered += action => Dispatcher.BeginInvoke(new Action(() => RunUiTask(() => viewModel.HandleHotkeyAsync(action))));
            hotkeys.CaptureKeyPressed += (modifiers, key) => Dispatcher.BeginInvoke(new Action(() => viewModel.CaptureHotkey(modifiers, key)));
            hotkeys.CaptureCancelled += () => Dispatcher.BeginInvoke(new Action(viewModel.CancelHotkeyCapture));
            audio.DeviceLost += () => Dispatcher.BeginInvoke(new Action(() => RunUiTask(() => viewModel.CancelAsync(UiText.MicrophoneLost))));
            audio.LimitReached += () => Dispatcher.BeginInvoke(new Action(() => RunUiTask(viewModel.StopAsync)));
            audio.LevelChanged += level => Dispatcher.BeginInvoke(new Action(() => viewModel.SetAudioLevel(level)));
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SessionEnding += (_, _) => transcriber.Reset();
            InitializeTray();
            if (!startInBackground)
                window.Show();
            await viewModel.InitializeAsync();
            // Optional smoke-test switch closes cleanly without recording or model downloads.
            if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                SaveSmokeScreenshot();
                await QuitAsync();
            }
        }
        catch (Exception)
        {
            MessageBox.Show(UiText.StorageFailed, UiText.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async void RunUiTask(Func<Task> task)
    {
        try { await task(); }
        catch (Exception) { if (viewModel is not null) viewModel.Status = UiText.UnexpectedError; }
    }

    private void InitializeTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(UiText.Open, null, (_, _) => ShowMainWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(UiText.Quit, null, (_, _) => RunUiTask(QuitAsync));
        var processPath = Environment.ProcessPath;
        appIcon = processPath is null ? null : Icon.ExtractAssociatedIcon(processPath);
        appIcon ??= (Icon)SystemIcons.Application.Clone();
        tray = new Forms.NotifyIcon
        {
            Text = UiText.Title,
            Icon = appIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void SaveSmokeScreenshot()
    {
        var bitmap = new RenderTargetBitmap((int)window!.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-test.png"));
        encoder.Save(output);
    }

    private void ShowMainWindow()
    {
        window!.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowOverlay))
        {
            overlayTimer!.Stop();
            if (viewModel!.ShowOverlay)
            {
                overlay!.Display();
                if (!viewModel.IsBusy) overlayTimer.Start();
            }
            else overlay!.Hide();
        }
        if (e.PropertyName == nameof(MainViewModel.Status) && tray is not null)
        {
            var text = UiText.Title + " · " + viewModel!.Status;
            tray.Text = text[..Math.Min(text.Length, 63)];
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff)
            Dispatcher.BeginInvoke(new Action(() => { viewModel?.CancelHotkeyCapture(); hotkeys?.Reset(); RunUiTask(() => viewModel!.CancelAsync()); }));
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
            Dispatcher.BeginInvoke(new Action(() => hotkeys?.Reset()));
    }

    private async Task QuitAsync()
    {
        if (exiting) return;
        exiting = true;
        hotkeys?.Dispose();
        if (viewModel is not null) await viewModel.CancelAsync();
        transcriber?.Dispose();
        if (audio is not null) await audio.DisposeAsync();
        if (window is not null) window.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        overlayTimer?.Stop();
        hotkeys?.Dispose();
        transcriber?.Dispose();
        tray?.Dispose();
        appIcon?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
