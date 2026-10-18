# Logo assets

Main: yellow highlighted bilingual cards (`logo.png`). System tray: a single yellow tile with large bilingual glyphs (`tray-logo.png`). Generated with the built-in image generation tool; original transparency is preserved.

`app.ico` embeds the main artwork at all nine sizes for the EXE. `window.ico` uses simplified artwork at 16, 20, 24 and 32 pixels and main artwork at larger sizes. `tray.ico` uses simplified artwork at all sizes. WPF windows load `window.ico` as a BitmapFrame so the decoder preserves every frame for native window/title-bar/taskbar sizing.

Regenerate both:

```powershell
./scripts/create-icon.ps1
./scripts/create-icon.ps1 -Output 'Assets/window.ico' -SmallSource 'Assets/tray-logo.png'
./scripts/create-icon.ps1 -Source 'Assets/tray-logo.png' -Output 'Assets/tray.ico' -SmallSource 'Assets/tray-logo.png'
```

## Main design prompt

Preserve two overlapping rounded panels and large A and 文 glyphs. Rear panel: highlighter yellow with charcoal A. Front panel: pale yellow with charcoal 文. Sturdy charcoal perimeter, broad highlighted fills, no extra frames, arrows or decoration. Flat design, transparent exterior, opaque interiors.

## Tray design prompt

One compact yellow rounded tile with large charcoal A upper-left and 文 lower-right. Balanced diagonal arrangement, thick strokes and generous counters, single charcoal perimeter. Designed for small Windows system tray sizes, no decorative elements, minimal margin. Flat yellow and charcoal fills, transparent exterior, opaque interior.
