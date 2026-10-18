param(
    [string]$Source = 'Assets\logo.png',
    [string]$Output = 'Assets\app.ico',
    [string]$SmallSource = 'Assets\logo.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$image = [System.Drawing.Image]::FromFile((Join-Path $projectRoot $Source))
$smallImage = [System.Drawing.Image]::FromFile((Join-Path $projectRoot $SmallSource))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $frameSource = if ($size -le 32) { $smallImage } else { $image }
            $graphics.DrawImage($frameSource, 0, 0, $size, $size)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,($stream.ToArray())
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $outputStream = [System.IO.File]::Create((Join-Path $projectRoot $Output))
    $writer = [System.IO.BinaryWriter]::new($outputStream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose() }
} finally { $image.Dispose(); $smallImage.Dispose() }
Write-Output "Created $Output with nine icon sizes."
