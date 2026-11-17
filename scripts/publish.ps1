param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Keep filenames and MSBuild arguments derived from a strictly validated version.
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:[A-Za-z0-9]+)(?:\.[A-Za-z0-9]+)*)?$') {
    throw 'Version must be 0.1.0 or 0.1.0-rc.1 (without the v prefix).'
}
$coreVersion = ($Version -split '-', 2)[0]
foreach ($part in ($coreVersion -split '\.')) {
    if ([long]$part -gt 65534) { throw 'Each numeric version component must be at most 65534 for Windows file version metadata.' }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$packageName = "OllamaTextLens-$Version-win-x64"
$publishDir = Join-Path $projectRoot "artifacts/$packageName"
$distDir = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

& dotnet publish (Join-Path $projectRoot 'OllamaTextLens.csproj') `
    -c Release -r win-x64 --self-contained true `
    --source https://api.nuget.org/v3/index.json `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$Version" "-p:AssemblyVersion=$coreVersion.0" "-p:FileVersion=$coreVersion.0" `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$exe = Join-Path $publishDir 'OllamaTextLens.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Expected EXE was not produced.' }
$japanese=Join-Path $projectRoot 'artifacts/test/speech-ja'
if (-not (Test-Path -LiteralPath (Join-Path $japanese 'JapaneseSpeech/JapaneseSpeech.exe'))) { throw 'Build the Japanese speech component before publishing.' }
if (-not (Test-Path -LiteralPath (Join-Path $japanese 'JapaneseSpeech/_internal/unidic_lite/dicdir'))) { throw 'Japanese pronunciation dictionary missing.' }
Copy-Item -LiteralPath $japanese -Destination $publishDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets') -Destination $publishDir -Recurse -Force
$exeAsset = Join-Path $distDir "$packageName.exe"
$zipAsset = Join-Path $distDir "$packageName.zip"
Copy-Item -LiteralPath $exe -Destination $exeAsset -Force
Compress-Archive -LiteralPath $exe, (Join-Path $publishDir 'speech-ja'), (Join-Path $publishDir 'README.md'), (Join-Path $publishDir 'LICENSE'), (Join-Path $publishDir 'THIRD-PARTY-NOTICES.md'), (Join-Path $publishDir 'docs'), (Join-Path $publishDir 'Assets') -DestinationPath $zipAsset -Force
$checksums = foreach ($asset in @($exeAsset, $zipAsset)) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $asset)"
}
$checksums | Set-Content -LiteralPath (Join-Path $distDir "$packageName-SHA256SUMS.txt") -Encoding ascii
Write-Output "Release assets: $distDir"
