$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot 'docs\logo-preview'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$sourcePath = Join-Path $outputDir 'simplified-logo.png'
if (!(Test-Path -LiteralPath $sourcePath)) { throw 'Place the selected simplified logo in docs/logo-preview/simplified-logo.png first.' }
$source = [System.Drawing.Image]::FromFile($sourcePath)
$sheet = [System.Drawing.Bitmap]::new(720, 580)
$sheet.SetResolution(96, 96)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
$font = [System.Drawing.Font]::new('Segoe UI', 12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$titleFont = [System.Drawing.Font]::new('Segoe UI', 18, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$tiles = @()
try {
    foreach ($size in @(16, 24, 32)) {
        $tile = [System.Drawing.Bitmap]::new($size, $size)
        $tile.SetResolution(96, 96)
        $draw = [System.Drawing.Graphics]::FromImage($tile)
        try {
            $draw.Clear([System.Drawing.Color]::Transparent)
            $draw.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $draw.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $draw.DrawImage($source, 0, 0, $size, $size)
            $tile.Save((Join-Path $outputDir "tray-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $draw.Dispose() }
        $tiles += $tile
    }
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.DrawString('System tray icon: actual pixels + 4x pixel view', $titleFont, [System.Drawing.Brushes]::Black, 14, 10)
    $graphics.DrawString('View at 100% zoom. Preview only; Windows/DPI can render differently.', $font, [System.Drawing.Brushes]::DimGray, 14, 35)
    foreach ($dark in @($false, $true)) {
        $top = if ($dark) { 310 } else { 55 }
        $color = if ($dark) { '#202020' } else { '#F3F3F3' }
        $foreground = if ($dark) { [System.Drawing.Brushes]::White } else { [System.Drawing.Brushes]::Black }
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($color))
        try { $graphics.FillRectangle($brush, 0, $top, 720, 240) } finally { $brush.Dispose() }
        $graphics.DrawString("Background $color", $font, $foreground, 12, ($top + 8))
        for ($i = 0; $i -lt $tiles.Count; $i++) {
            $tile = $tiles[$i]; $size = $tile.Width; $center = 120 + 240 * $i
            $graphics.DrawString("$size x $size px (1:1)", $font, $foreground, ($center - 50), ($top + 33))
            $graphics.DrawImageUnscaled($tile, ($center - $size / 2), ($top + 60 + (32 - $size) / 2))
            $graphics.DrawString('4x nearest-neighbor', $font, $foreground, ($center - 60), ($top + 96))
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $destination = [System.Drawing.Rectangle]::new(($center - $size * 2), ($top + 112 + (128 - $size * 4) / 2), ($size * 4), ($size * 4))
            $graphics.DrawImage($tile, $destination, 0, 0, $size, $size, [System.Drawing.GraphicsUnit]::Pixel)
        }
    }
    $sheet.Save((Join-Path $outputDir 'tray-comparison.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    foreach ($tile in $tiles) { $tile.Dispose() }
    $source.Dispose(); $graphics.Dispose(); $sheet.Dispose(); $font.Dispose(); $titleFont.Dispose()
}
Write-Output "Tray previews saved to $outputDir"
