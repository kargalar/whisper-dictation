param(
    [string]$CudaRoot = $env:CUDA_PATH,
    [switch]$CpuOnly
)

$ErrorActionPreference = 'Stop'
$workspacePath = $PSScriptRoot
$artifactPath = Join-Path $workspacePath 'artifacts'
$buildPath = Join-Path $artifactPath ('build-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$publishPath = Join-Path $buildPath 'WhisperDictation'
$workerPath = Join-Path $publishPath 'worker'
$licensePath = Join-Path $publishPath 'licenses'
$zipPath = Join-Path $artifactPath 'WhisperDictation-win-x64.zip'
$temporaryZipPath = Join-Path $artifactPath ('WhisperDictation-win-x64.' + [Guid]::NewGuid().ToString('N') + '.tmp')
$modelDirectory = Join-Path $workspacePath 'models'
$modelCatalog = Get-Content -LiteralPath (Join-Path $modelDirectory 'catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json

# Models are downloaded during development, verified here, and bundled for offline use.
foreach ($definition in $modelCatalog.models) {
    $modelFile = Join-Path $modelDirectory $definition.fileName
    if (-not (Test-Path -LiteralPath $modelFile) -or (Get-Item -LiteralPath $modelFile).Length -ne $definition.size) {
        throw "Model missing or incomplete: $($definition.id). Run ./download-models.ps1 before packaging."
    }
    if ((Get-FileHash -LiteralPath $modelFile -Algorithm SHA256).Hash -ne $definition.sha256) {
        throw "Model checksum mismatch: $($definition.id)."
    }
}

function Invoke-Dotnet {
    param([string[]]$CommandArguments)
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)." }
}

function Remove-StagedDirectory {
    param([string]$DirectoryPath)
    $resolvedPath = [IO.Path]::GetFullPath($DirectoryPath)
    $stagingRoot = [IO.Path]::GetFullPath($publishPath).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($stagingRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a directory outside this fresh packaging stage.'
    }
    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

New-Item -ItemType Directory -Path $publishPath, $licensePath -Force | Out-Null
$bundledModelDirectory = Join-Path $publishPath 'models'
New-Item -ItemType Directory -Path $bundledModelDirectory -Force | Out-Null
foreach ($definition in $modelCatalog.models) {
    Copy-Item -LiteralPath (Join-Path $modelDirectory $definition.fileName) -Destination $bundledModelDirectory
}
Copy-Item -LiteralPath (Join-Path $modelDirectory 'catalog.json') -Destination $bundledModelDirectory
Copy-Item -LiteralPath (Join-Path $modelDirectory 'LICENSE-OpenAI.txt') -Destination $licensePath
Invoke-Dotnet @('restore', (Join-Path $workspacePath 'WhisperDictation.slnx'), '--locked-mode')
Invoke-Dotnet @('publish', (Join-Path $workspacePath 'src/WhisperDictation.Desktop/WhisperDictation.Desktop.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publishPath, '-p:PublishTrimmed=false')
Invoke-Dotnet @('publish', (Join-Path $workspacePath 'src/WhisperDictation.Worker/WhisperDictation.Worker.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $workerPath, '-p:PublishTrimmed=false')

# The generic Whisper CPU package carries other architectures; distribute only Windows x64.
foreach ($runtimeDirectory in @((Join-Path $workerPath 'runtimes'), (Join-Path $publishPath 'runtimes'))) {
    if (Test-Path -LiteralPath $runtimeDirectory) {
        foreach ($platformDirectory in Get-ChildItem -LiteralPath $runtimeDirectory -Directory) {
            if ($platformDirectory.Name -notin @('win-x64', 'cuda')) { Remove-StagedDirectory $platformDirectory.FullName }
        }
    }
}

# The native engine imports these redistributable MSVC/OpenMP DLLs.
$nativeRuntimeFiles = @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll')
foreach ($fileName in $nativeRuntimeFiles) {
    $runtimeSource = Join-Path ([Environment]::GetFolderPath('System')) $fileName
    if (-not (Test-Path -LiteralPath $runtimeSource)) { throw "Missing native runtime: $fileName" }
    Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $workerPath $fileName) -Force
}

if (-not $CpuOnly) {
    if ([string]::IsNullOrWhiteSpace($CudaRoot)) {
        $cudaDirectory = 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA'
        $CudaRoot = Get-ChildItem -LiteralPath $cudaDirectory -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'v13.*' } | Sort-Object Name -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ([string]::IsNullOrWhiteSpace($CudaRoot)) { throw 'CUDA 13 toolkit required for packaging. Use -CudaRoot or -CpuOnly.' }
    foreach ($fileName in @('cudart64_13.dll', 'cublas64_13.dll', 'cublasLt64_13.dll')) {
        $cudaSource = Join-Path (Join-Path $CudaRoot 'bin/x64') $fileName
        if (-not (Test-Path -LiteralPath $cudaSource)) { $cudaSource = Join-Path (Join-Path $CudaRoot 'bin') $fileName }
        if (-not (Test-Path -LiteralPath $cudaSource)) { throw "Missing CUDA library: $fileName" }
        Copy-Item -LiteralPath $cudaSource -Destination (Join-Path $workerPath $fileName) -Force
    }
    Copy-Item -LiteralPath (Join-Path $CudaRoot 'EULA.txt') -Destination (Join-Path $licensePath 'NVIDIA-CUDA-EULA.txt') -Force
    Copy-Item -LiteralPath (Join-Path $CudaRoot 'LICENSE') -Destination (Join-Path $licensePath 'NVIDIA-CUDA-LICENSE.txt') -Force
}

Copy-Item -LiteralPath (Join-Path $workspacePath 'README.md') -Destination (Join-Path $publishPath 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $workspacePath 'THIRD_PARTY_NOTICES.md') -Destination $publishPath -Force

# Record package versions and license expressions from the restored packages, without network calls.
$packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
$packageRows = @{}
foreach ($lockFile in Get-ChildItem -LiteralPath (Join-Path $workspacePath 'src') -Filter 'packages.lock.json' -Recurse) {
    $lockData = Get-Content -LiteralPath $lockFile.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $lockData.dependencies.PSObject.Properties) {
        foreach ($package in $framework.Value.PSObject.Properties) {
            if ($package.Value.type -eq 'Project') { continue }
            $version = $package.Value.resolved
            $packageDirectory = Join-Path (Join-Path $packageCache $package.Name.ToLowerInvariant()) $version
            $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' | Select-Object -First 1
            if ($nuspec) {
                [xml]$specification = Get-Content -LiteralPath $nuspec.FullName -Raw
                $metadata = $specification.package.metadata
                $packageRows[$package.Name] = "| $($package.Name) | $version | $($metadata.license.InnerText) | $($metadata.projectUrl) |"
            }
            foreach ($license in Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING)' }) {
                Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $licensePath ($package.Name + '-' + $license.Name)) -Force
            }
        }
    }
}
@('# NuGet dependencies', '', '| Package | Version | License | Project |', '|---|---|---|---|') +
    @($packageRows.GetEnumerator() | Sort-Object Name | ForEach-Object Value) |
    Set-Content -LiteralPath (Join-Path $licensePath 'NuGet-dependencies.md') -Encoding utf8

# Exclude user settings, history, screenshots and debug symbols from the distributable ZIP.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($temporaryZipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $publishPath -File -Recurse) {
        $relativePath = $file.FullName.Substring($publishPath.Length + 1)
        if ($relativePath -match '^data[\\/]' -or $file.Extension -eq '.pdb' -or $file.Name -eq 'smoke-test.png') { continue }
        # Model weights are large binary assets; store them directly to keep packaging/extraction fast.
        $compression = if ($file.Extension -eq '.bin') { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName,
            'WhisperDictation/' + $relativePath.Replace('\', '/'), $compression) | Out-Null
    }
}
finally { $archive.Dispose() }
Move-Item -LiteralPath $temporaryZipPath -Destination $zipPath -Force
@{ PortableDirectory = $publishPath; ZipPath = $zipPath } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $artifactPath 'latest-build.json') -Encoding utf8
Get-Item -LiteralPath $zipPath | Select-Object FullName, Length
