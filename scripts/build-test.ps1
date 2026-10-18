$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $projectRoot 'artifacts\test'

# Exit OllamaTextLens from its tray menu before rebuilding to avoid locked files.
& dotnet build (Join-Path $projectRoot 'OllamaTextLens.csproj') -c Release -t:Rebuild -o $testOutput
if ($LASTEXITCODE -ne 0) { throw 'Test build failed. If the app is running, choose Exit from its tray menu and try again.' }
# Notify Explorer about this rebuilt EXE without clearing unrelated shell caches.
if (-not ('OllamaTextLens.BuildShell' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace OllamaTextLens {
    public static class BuildShell {
        [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
        public static extern void SHChangeNotify(uint change, uint flags, string item, IntPtr other);
    }
}
'@
}
[OllamaTextLens.BuildShell]::SHChangeNotify(0x2000, 0x1005, (Join-Path $testOutput 'OllamaTextLens.exe'), [IntPtr]::Zero)
Write-Host "Test app: $(Join-Path $testOutput 'OllamaTextLens.exe')"
