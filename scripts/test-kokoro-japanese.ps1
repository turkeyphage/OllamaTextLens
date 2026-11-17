$ErrorActionPreference = 'Stop'
$experimentRoot = Join-Path $env:LOCALAPPDATA 'OllamaTextLens\experiments\kokoro-japanese'
$python = Join-Path $experimentRoot 'venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    $basePython=Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'
    if (-not (Test-Path -LiteralPath $basePython)) { $basePython=(Get-Command python -ErrorAction Stop).Source }
    & $basePython -m venv (Join-Path $experimentRoot 'venv')
    if ($LASTEXITCODE) { throw 'venv failed' }
}
# Windows wheel of the Open JTalk fork avoids requiring a local C++ compiler.
& $python -m pip install misaki==0.9.4 pyopenjtalk-plus==0.4.1.post10 fugashi jaconv mojimoji unidic unidic-lite onnxruntime==1.23.2 soundfile
if ($LASTEXITCODE) { throw 'Japanese speech dependencies failed' }
& $python -u (Join-Path $PSScriptRoot 'test-kokoro-japanese.py')
if ($LASTEXITCODE) { throw 'Japanese speech trial failed' }
