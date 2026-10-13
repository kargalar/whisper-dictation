param(
    [ValidateSet('Turbo', 'Large')]
    [string[]]$Model = @('Turbo', 'Large')
)

$ErrorActionPreference = 'Stop'
$modelDirectory = Join-Path $PSScriptRoot 'models'
$catalog = Get-Content -LiteralPath (Join-Path $modelDirectory 'catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json

function Test-ModelFile {
    param([string]$Path, $Definition)
    return (Test-Path -LiteralPath $Path) -and
        ((Get-Item -LiteralPath $Path).Length -eq $Definition.size) -and
        ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Definition.sha256)
}

foreach ($definition in $catalog.models | Where-Object { $_.id -in $Model }) {
    $modelPath = Join-Path $modelDirectory $definition.fileName
    if (Test-ModelFile $modelPath $definition) {
        Write-Output "$($definition.id): verified, already available."
        continue
    }
    $temporaryPath = $modelPath + '.part'
    $url = 'https://huggingface.co/' + $catalog.repository + '/resolve/' + $catalog.revision + '/' + $definition.fileName + '?download=true'
    Write-Output "$($definition.id): downloading $($definition.size) bytes."
    & curl.exe --fail --location --silent --show-error --retry 3 --continue-at - --output $temporaryPath $url
    if ($LASTEXITCODE -ne 0) { throw "Model download failed: $($definition.id). Run again to resume." }
    if (-not (Test-ModelFile $temporaryPath $definition)) { throw "Model verification failed: $($definition.id)." }
    Move-Item -LiteralPath $temporaryPath -Destination $modelPath -Force
    Write-Output "$($definition.id): downloaded and SHA256 verified."
}
