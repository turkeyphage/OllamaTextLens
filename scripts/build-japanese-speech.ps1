param([string]$Python = (Join-Path $env:LOCALAPPDATA 'OllamaTextLens\experiments\kokoro-japanese\venv\Scripts\python.exe'))
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $Python)) { throw 'Pass -Python with a Python 3.12 executable, or prepare the local Japanese environment first.' }
& $Python -m pip install -r (Join-Path $PSScriptRoot 'japanese-requirements.txt')
if ($LASTEXITCODE) { throw 'Japanese build dependencies failed to install' }
$output=Join-Path $projectRoot 'artifacts\test\speech-ja'
$work=Join-Path $projectRoot 'artifacts\japanese-build'
& $Python -m PyInstaller --noconfirm --clean --onedir --console --name JapaneseSpeech --distpath $output --workpath $work --specpath $work --collect-data unidic_lite --collect-data misaki --collect-data unidic --collect-submodules pyopenjtalk --collect-data pyopenjtalk --exclude-module torch --exclude-module scipy --exclude-module pandas (Join-Path $PSScriptRoot 'japanese-worker.py')
if ($LASTEXITCODE) { throw 'Japanese helper packaging failed' }
# Preserve the licenses for the bundled runtime, dictionary and Python packages.
& $Python -c 'import importlib.metadata as m, pathlib, shutil, sys; out=pathlib.Path(sys.argv[1]); out.mkdir(parents=True,exist_ok=True); [(shutil.copyfile(d.locate_file(f),out/(d.metadata["Name"]+"-"+str(f).replace("/","_").replace("\\","_")))) for d in m.distributions() for f in (d.files or []) if any(x in str(f).lower() for x in ("license","copying","copyright")) and d.locate_file(f).is_file()]' (Join-Path $output 'JapaneseSpeech\licenses')
if ($LASTEXITCODE) { throw 'License copy failed' }
