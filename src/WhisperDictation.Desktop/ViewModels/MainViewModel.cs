using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperDictation.Core;
using WhisperDictation.Desktop.Services;

namespace WhisperDictation.Desktop.ViewModels;

public sealed record ComputeChoice(ComputeMode Mode, string Name);

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore settingsStore;
    private readonly IHistoryStore historyStore;
    private readonly DictationController controller;
    private readonly ITranscriber transcriber;
    private readonly GlobalHotkeyService hotkeyService;
    private readonly WindowsStartupService windowsStartupService;
    private AppSettings activeSettings;
    private HotkeyAction activeRecordingAction;
    private int nextWaveformBar;
    private CancellationTokenSource? settingsSaveCancellation;
    private bool isInitializing = true;

    [ObservableProperty] private string status = UiText.Idle;
    [ObservableProperty] private string lastText = "";
    [ObservableProperty] private string engine = UiText.UnknownEngine;
    [ObservableProperty] private string hotkey = AppDefaults.DefaultHotkey;
    [ObservableProperty] private string activeHotkey = AppDefaults.DefaultHotkey;
    [ObservableProperty] private bool isCapturingHotkey;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private ModelChoice selectedModel = ModelChoice.All[0];
    [ObservableProperty] private MicrophoneChoice? selectedMicrophone;
    [ObservableProperty] private ComputeChoice selectedCompute;
    [ObservableProperty] private LanguageChoice selectedLanguage;
    [ObservableProperty] private OutputModeChoice selectedOutputMode;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRecording;
    [ObservableProperty] private bool showOverlay;

    public IReadOnlyList<ModelChoice> Models => ModelChoice.All;
    public IReadOnlyList<LanguageChoice> Languages => LanguageChoice.All;
    public IReadOnlyList<OutputModeChoice> OutputModes => OutputModeChoice.All;
    public IReadOnlyList<ComputeChoice> ComputeChoices { get; } =
    [new(ComputeMode.Automatic, UiText.AutomaticCompute), new(ComputeMode.Cpu, UiText.CpuCompute)];
    public ObservableCollection<MicrophoneChoice> Microphones { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ObservableCollection<double> AudioWaveform { get; } = [3, 3, 3, 3, 3, 3, 3];
    public bool CanConfigure => !IsBusy;
    public bool CanSelectModel => SelectedOutputMode.Mode == OutputMode.Transcribe;
    public bool HasText => !string.IsNullOrEmpty(LastText);
    public string HotkeyCaptureDisplay => IsCapturingHotkey ? UiText.HotkeyCapturePrompt : Hotkey;

    public MainViewModel(SettingsStore settingsStore, IHistoryStore historyStore, DictationController controller,
        ITranscriber transcriber, GlobalHotkeyService hotkeyService, AppSettings settings,
        WindowsStartupService? windowsStartupService = null)
    {
        this.settingsStore = settingsStore;
        this.historyStore = historyStore;
        this.controller = controller;
        this.transcriber = transcriber;
        this.hotkeyService = hotkeyService;
        this.windowsStartupService = windowsStartupService ?? new();
        settings = settings.WithCompatibleModel();
        activeSettings = settings;
        selectedCompute = ComputeChoices.First(choice => choice.Mode == settings.ComputeMode);
        selectedLanguage = Languages.First(choice => choice.Code == settings.Language);
        selectedOutputMode = OutputModes.First(choice => choice.Mode == settings.OutputMode);
        SelectedModel = Models.First(choice => choice.Kind == settings.SelectedModel);
        Hotkey = ActiveHotkey = settings.Hotkey;
        StartWithWindows = settings.StartWithWindows;
        RefreshMicrophones();
        SelectedMicrophone = Microphones.FirstOrDefault(choice => choice.Id == settings.MicrophoneId) ?? Microphones.First();
        isInitializing = false;
        controller.Changed += UpdateState;
        controller.HistoryAdded += entry => History.Insert(0, entry);
        controller.MicrophoneFallback += () => SwitchToDefaultMicrophone(UiText.MicrophoneLostFallback);
    }

    public async Task InitializeAsync()
    {
        try
        {
            foreach (var entry in await historyStore.ReadAsync()) History.Add(entry);
        }
        catch (Exception) { Status = UiText.HistoryReadFailed; }
        await controller.PrepareAsync(settingsStore.GetModelPath(activeSettings.SelectedModel), activeSettings.ComputeMode,
            activeSettings.Language, activeSettings.OutputMode);
    }

    public async Task HandleHotkeyAsync(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Stop:
                if (activeRecordingAction == HotkeyAction.Toggle)
                {
                    activeRecordingAction = HotkeyAction.None;
                    await controller.StopAsync();
                }
                break;
            case HotkeyAction.Toggle:
                if (controller.State == DictationState.Recording)
                {
                    activeRecordingAction = HotkeyAction.None;
                    await controller.StopAsync();
                }
                else
                {
                    controller.Start(activeSettings);
                    activeRecordingAction = controller.State == DictationState.Recording
                        ? HotkeyAction.Toggle : HotkeyAction.None;
                }
                break;
            case HotkeyAction.Cancel: await controller.CancelAsync(); break;
        }
        if (action == HotkeyAction.Cancel)
            activeRecordingAction = HotkeyAction.None;
    }

    public Task CancelAsync(string? message = null) => controller.CancelAsync(message);
    public Task StopAsync() => controller.StopAsync();
    public void SetAudioLevel(float level)
    {
        var audioLevel = Math.Min(100, level * 200);
        AudioWaveform[nextWaveformBar] = audioLevel == 0 ? 3 : 3 + audioLevel * 0.15;
        nextWaveformBar = (nextWaveformBar + 1) % AudioWaveform.Count;
    }

    public void BeginHotkeyCapture()
    {
        if (IsCapturingHotkey || !CanConfigure)
            return;
        hotkeyService.BeginCapture();
        IsCapturingHotkey = true;
    }

    public void CancelHotkeyCapture()
    {
        if (!IsCapturingHotkey)
            return;
        hotkeyService.EndCapture();
        IsCapturingHotkey = false;
    }

    public void CaptureHotkey(KeyModifiers modifiers, int virtualKey)
    {
        if (!IsCapturingHotkey)
            return;
        if (!HotkeyChord.TryCreate(modifiers, virtualKey, out var chord))
        {
            Status = UiText.InvalidHotkey;
            return;
        }
        try
        {
            var display = chord!.ToDisplayString();
            var updatedSettings = activeSettings with { Hotkey = display };
            settingsStore.Save(updatedSettings);
            activeSettings = updatedSettings;
            Hotkey = ActiveHotkey = updatedSettings.Hotkey;
            HotkeyChord.TryParse(updatedSettings.Hotkey, out var updatedChord);
            hotkeyService.Update(updatedChord!);
            IsCapturingHotkey = false;
            Status = UiText.HotkeySaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CancelHotkeyCapture();
            Status = UiText.StorageFailed;
        }
    }

    private void UpdateState()
    {
        Status = controller.Status;
        LastText = controller.LastText;
        Engine = transcriber.Engine;
        IsBusy = controller.IsBusy;
        IsRecording = controller.State == DictationState.Recording;
        if (controller.State != DictationState.Recording)
            activeRecordingAction = HotkeyAction.None;
        ShowOverlay = IsBusy || controller.State == DictationState.Error;
        hotkeyService.CancellationAvailable = IsBusy;
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfigure));
        ApplySettingsCommand.NotifyCanExecuteChanged();
        RefreshMicrophonesCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastTextChanged(string value) => OnPropertyChanged(nameof(HasText));
    partial void OnHotkeyChanged(string value) => OnPropertyChanged(nameof(HotkeyCaptureDisplay));
    partial void OnIsCapturingHotkeyChanged(bool value)
    {
        OnPropertyChanged(nameof(HotkeyCaptureDisplay));
    }

    partial void OnSelectedOutputModeChanged(OutputModeChoice value)
    {
        if (value.Mode == OutputMode.TranslateToEnglish)
            SelectedModel = Models.Single(choice => choice.Kind == ModelKind.Large);
        OnPropertyChanged(nameof(CanSelectModel));
        ScheduleSettingsSave();
    }

    partial void OnSelectedModelChanged(ModelChoice value) => ScheduleSettingsSave();
    partial void OnSelectedComputeChanged(ComputeChoice value) => ScheduleSettingsSave();
    partial void OnSelectedLanguageChanged(LanguageChoice value) => ScheduleSettingsSave();
    partial void OnSelectedMicrophoneChanged(MicrophoneChoice? value) => ScheduleSettingsSave();
    partial void OnStartWithWindowsChanged(bool value) => ScheduleSettingsSave();

    private void ScheduleSettingsSave()
    {
        if (isInitializing)
            return;
        settingsSaveCancellation?.Cancel();
        settingsSaveCancellation?.Dispose();
        var cancellation = settingsSaveCancellation = new CancellationTokenSource();
        _ = SaveSettingsAfterSelectionAsync(cancellation.Token);
    }

    private async Task SaveSettingsAfterSelectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            if (!IsBusy)
                await ApplySettingsAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private async Task ApplySettingsAsync()
    {
        if (!HotkeyChord.TryParse(Hotkey, out var chord))
        {
            Status = UiText.InvalidHotkey;
            return;
        }
        try
        {
            var settings = (activeSettings with
            {
                SelectedModel = SelectedModel.Kind,
                ComputeMode = SelectedCompute.Mode,
                Language = SelectedLanguage.Code,
                OutputMode = SelectedOutputMode.Mode,
                MicrophoneId = SelectedMicrophone?.Id ?? "",
                Hotkey = Hotkey.Trim(),
                StartWithWindows = StartWithWindows
            }).WithCompatibleModel();
            var startupSettingChanged = settings.StartWithWindows != activeSettings.StartWithWindows;
            if (startupSettingChanged)
                windowsStartupService.SetEnabled(settings.StartWithWindows);
            try
            {
                settingsStore.Save(settings);
            }
            catch
            {
                if (startupSettingChanged)
                    windowsStartupService.SetEnabled(activeSettings.StartWithWindows);
                throw;
            }
            var reloadEngine = settings.SelectedModel != activeSettings.SelectedModel
                || settings.ComputeMode != activeSettings.ComputeMode
                || settings.Language != activeSettings.Language || settings.OutputMode != activeSettings.OutputMode || !transcriber.IsReady;
            activeSettings = settings;
            SelectedModel = Models.Single(choice => choice.Kind == settings.SelectedModel);
            hotkeyService.Update(chord!);
            ActiveHotkey = settings.Hotkey;
            if (reloadEngine)
                await controller.PrepareAsync(settingsStore.GetModelPath(settings.SelectedModel), settings.ComputeMode,
                    settings.Language, settings.OutputMode);
            else Status = UiText.SettingsSaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or Win32Exception or InvalidOperationException)
        { Status = UiText.StartupFailed; }
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    public void RefreshMicrophones()
    {
        try
        {
            var currentList = AudioCaptureService.GetMicrophones();
            var targetId = SelectedMicrophone?.Id ?? activeSettings.MicrophoneId;
            if (!Microphones.SequenceEqual(currentList))
            {
                Microphones.Clear();
                foreach (var choice in currentList) Microphones.Add(choice);
            }
            SelectedMicrophone = Microphones.FirstOrDefault(choice => choice.Id == targetId) ?? Microphones.First();
        }
        catch (Exception)
        {
            if (Microphones.Count == 0) Microphones.Add(new("", UiText.DefaultMicrophone));
            SelectedMicrophone = Microphones.First();
            Status = UiText.MicrophoneFailed;
        }
    }

    public void SwitchToDefaultMicrophone(string? statusMessage = null)
    {
        try
        {
            RefreshMicrophones();
            var defaultMic = Microphones.FirstOrDefault(choice => string.IsNullOrEmpty(choice.Id))
                ?? new MicrophoneChoice("", UiText.DefaultMicrophone);
            SelectedMicrophone = defaultMic;
            var updatedSettings = activeSettings with { MicrophoneId = "" };
            activeSettings = updatedSettings;
            try { settingsStore.Save(updatedSettings); }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(statusMessage))
                Status = statusMessage;
        }
        catch (Exception)
        {
            Status = UiText.MicrophoneFailed;
        }
    }

    public async Task HandleMicrophoneLostAsync()
    {
        await CancelAsync(UiText.MicrophoneLostFallback);
        SwitchToDefaultMicrophone(UiText.MicrophoneLostFallback);
    }

    public void OnDeviceUnavailable(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;
        if (string.Equals(activeSettings.MicrophoneId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            SwitchToDefaultMicrophone(UiText.MicrophoneLostFallback);
        }
        else
        {
            RefreshMicrophones();
        }
    }

    public void OnDevicesChanged()
    {
        if (IsRecording || IsCapturingHotkey)
            return;
        RefreshMicrophones();
    }

    [RelayCommand] private void CopyLast() => CopyText(LastText);
    [RelayCommand] private void CopyEntry(HistoryEntry? entry) { if (entry is not null) CopyText(entry.Text); }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); Status = UiText.Copied; }
        catch (ExternalException) { Status = UiText.CopyFailed; }
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(HistoryEntry? entry)
    {
        if (entry is null) return;
        try { await historyStore.DeleteAsync(entry.Id); History.Remove(entry); }
        catch (Exception) { Status = UiText.HistoryReadFailed; }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (MessageBox.Show(UiText.ConfirmClearHistory, UiText.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try { await historyStore.ClearAsync(); History.Clear(); Status = UiText.HistoryCleared; }
        catch (Exception) { Status = UiText.HistoryReadFailed; }
    }
}
