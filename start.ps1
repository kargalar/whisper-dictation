param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

try {
    $app = Join-Path $PSScriptRoot 'src\WhisperDictation.Desktop\bin\Release\net10.0-windows'
    $exe = Join-Path $app 'WhisperDictation.exe'
    $solution = Join-Path $PSScriptRoot 'WhisperDictation.slnx'

    # WhisperDictation zaten çalışıyorsa sonlandır
    $running = Get-Process -Name WhisperDictation -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Mevcut WhisperDictation işlemi kapatılıyor..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    }

    # Derleme adımı
    if (-not $NoBuild -or -not (Test-Path -LiteralPath $exe)) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw ".NET SDK bulunamadı. Lütfen .NET 10 SDK'nın yüklü ve PATH'e eklenmiş olduğunu kontrol edin."
        }
        Write-Host "Proje derleniyor (Release)..." -ForegroundColor Cyan
        & dotnet build $solution -c Release
        if ($LASTEXITCODE -ne 0) {
            throw 'Release build başarısız oldu.'
        }
    }

    # Model dosyalarını kopyala
    $models = Join-Path $app 'models'
    New-Item -ItemType Directory -Path $models -Force | Out-Null
    foreach ($name in @('ggml-large-v3-turbo.bin', 'ggml-large-v3.bin')) {
        $target = Join-Path $models $name
        $source = Join-Path $PSScriptRoot "models\$name"
        if (-not (Test-Path -LiteralPath $target)) {
            if (Test-Path -LiteralPath $source) {
                Write-Host "Model kopyalanıyor: $name..." -ForegroundColor Cyan
                Copy-Item -LiteralPath $source -Destination $target
            } else {
                Write-Warning "Model dosyası ana dizinde bulunamadı: $source. 'download-models.ps1' ile indirebilirsiniz."
            }
        }
    }

    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Çalıştırılabilir dosya bulunamadı: $exe"
    }

    Write-Host "WhisperDictation başlatılıyor..." -ForegroundColor Green
    Start-Process -FilePath $exe -WorkingDirectory $app
}
catch {
    Write-Host ""
    Write-Host "[HATA] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}