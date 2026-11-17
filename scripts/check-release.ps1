param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$dist=Join-Path $root 'dist'
$name="OllamaTextLens-$Version-win-x64"
foreach($line in Get-Content -LiteralPath (Join-Path $dist "$name-SHA256SUMS.txt")) {
    $expected,$file=$line -split '  ',2
    if((Get-FileHash -LiteralPath (Join-Path $dist $file) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Checksum mismatch: $file" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead((Join-Path $dist "$name.zip"))
try {
    $names=@($zip.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    foreach($required in @('OllamaTextLens.exe','LICENSE','THIRD-PARTY-NOTICES.md','speech-ja/JapaneseSpeech/JapaneseSpeech.exe','speech-ja/JapaneseSpeech/_internal/python312.dll','speech-ja/JapaneseSpeech/_internal/unidic_lite/dicdir/sys.dic')) {
        if($required -notin $names) { throw "Release ZIP missing $required" }
    }
    if(-not ($names -match '^speech-ja/JapaneseSpeech/licenses/')) { throw 'Japanese dependency licenses missing' }
    if($names -match '(^|/)(model\.int8\.onnx|voices\.bin|settings\.json)$|(^|/)(qwen3|kokoro-japanese|kokoro-spanish)/') { throw 'Local model, settings or test output leaked into release ZIP' }
    Write-Output "Verified $($names.Count) ZIP entries, Japanese runtime/dictionary/licenses, and all asset checksums."
} finally { $zip.Dispose() }
