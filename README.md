<div align="center">

# Whisper Dictation

**Private, offline speech-to-text for Windows.**

Speak anywhere. Whisper Dictation types the transcription into your active app and keeps your audio on your computer.

[Download for Windows](../../releases/latest) · [Report a bug](../../issues) · [Build from source](#build-from-source)

</div>

## Features

- **Runs locally:** bundled Whisper models transcribe audio on your PC. The app makes no network connection.
- **Works across apps:** use `Shift+Space` to start or stop dictation in the focused window.
- **CPU-only release:** runs on Windows without requiring a CUDA installation. CUDA-enabled packages can be built from source.
- **Keeps a transcript history:** review, copy, or clear saved text whenever you need it.
- **Portable:** extract the release ZIP into a writable folder and run `WhisperDictation.exe`.
- **English recognition:** the bundled app is configured for English transcription.

## Download and run

1. Download all three `WhisperDictation-win-x64.zip.00x` parts from [Releases](../../releases/latest) into the same folder.
2. Join the parts in order from Command Prompt: `copy /b WhisperDictation-win-x64.zip.001+WhisperDictation-win-x64.zip.002+WhisperDictation-win-x64.zip.003 WhisperDictation-win-x64.zip`.
3. Extract the joined archive to a writable folder, such as `Documents\Whisper Dictation`.
4. Run `WhisperDictation.exe`.
5. Wait for **Ready to dictate**, focus a text field in any app, then press `Shift+Space` to start and stop.

The app starts in the system tray if **Start with Windows** is enabled. Closing the window minimizes it to the tray; right-click the tray icon and select **Quit** to exit.

## Privacy and local data

Audio is processed in memory and is not written to disk. Transcriptions and settings are stored in the app's `data` folder. This folder is not encrypted, so protect it with your Windows account and filesystem permissions. Use **Clear history** to delete saved transcriptions.

## Build from source

Requirements: Windows x64, the .NET 10 SDK, PowerShell, and (for the GPU build) the CUDA 13 Toolkit. The release ZIP is self-contained and includes its runtime, native dependencies, and Whisper models.

```powershell
dotnet restore WhisperDictation.slnx --locked-mode
dotnet build WhisperDictation.slnx
dotnet test WhisperDictation.slnx
dotnet format WhisperDictation.slnx --verify-no-changes --no-restore
./download-models.ps1
./build.ps1
```

For a CPU-only package, run `./build.ps1 -CpuOnly`. Model files are downloaded only by the developer script and checked against the pinned SHA-256 hashes in `models/catalog.json`.

The optional microphone integration test records a short sample in memory and deletes it after the test:

```powershell
$env:WHISPER_TEST_MICROPHONE = '1'
dotnet test WhisperDictation.slnx
```

## License

Whisper Dictation is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE). Third-party notices and model terms are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
