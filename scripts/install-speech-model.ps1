$ErrorActionPreference = 'Stop'
$modelRoot = Join-Path $env:LOCALAPPDATA 'OllamaTextLens\speech'
$target = Join-Path $modelRoot 'kokoro-int8-multi-lang-v1_0'
$ready = @('model.int8.onnx','voices.bin','tokens.txt','lexicon-us-en.txt') | ForEach-Object { Test-Path -LiteralPath (Join-Path $target $_) }
if (($ready -notcontains $false) -and (Test-Path -LiteralPath (Join-Path $target 'espeak-ng-data') -PathType Container)) { Write-Output "Model already installed: $target"; exit 0 }
New-Item -ItemType Directory -Path $modelRoot -Force | Out-Null
$archive = Join-Path $modelRoot 'kokoro-int8-multi-lang-v1_0.tar.bz2'
$url = 'https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_0.tar.bz2'
& curl.exe -L --fail --retry 2 -o $archive $url
if ($LASTEXITCODE -ne 0) { throw 'Model download failed' }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '72c347230107ef8a31098ec4d468a605244beef5f1ed00db97efa13518800653') { throw 'Model checksum mismatch' }
& tar.exe -xf $archive -C $modelRoot
if ($LASTEXITCODE -ne 0) { throw 'Model extraction failed' }
Get-ChildItem -LiteralPath $target | Select-Object Name,Length
Write-Output "MODEL_READY $target"
