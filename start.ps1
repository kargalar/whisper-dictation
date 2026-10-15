$ErrorActionPreference = 'Stop'
$app = Join-Path $PWD 'src\WhisperDictation.Desktop\bin\Release\net10.0-windows'

Get-Process -Name WhisperDictation -ErrorAction SilentlyContinue | Stop-Process -Force
Wait-Process -Name WhisperDictation -Timeout 15 -ErrorAction SilentlyContinue

dotnet build .\WhisperDictation.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release build başarısız oldu.' }

$models = Join-Path $app 'models'
New-Item -ItemType Directory -Path $models -Force | Out-Null
foreach ($name in @('ggml-large-v3-turbo.bin', 'ggml-large-v3.bin')) {
    $target = Join-Path $models $name
    if (-not (Test-Path -LiteralPath $target)) {
        Copy-Item -LiteralPath (Join-Path $PWD "models\$name") -Destination $target
    }
}

Start-Process -FilePath (Join-Path $app 'WhisperDictation.exe') -WorkingDirectory $app