$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
$source = [Drawing.Bitmap]::new((Join-Path $root 'ares-icon-chroma.png'))
try {
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            $color = $source.GetPixel($x, $y)
            $greenDominance = $color.G - [Math]::Max($color.R, $color.B)
            if ($greenDominance -gt 35) {
                $alpha = [Math]::Max(0, [Math]::Min(255, 255 - (($greenDominance - 35) * 3)))
                $red = [Math]::Min(255, $color.R + [Math]::Max(0, $color.G - $color.R) * (1 - $alpha / 255.0))
                $blue = [Math]::Min(255, $color.B + [Math]::Max(0, $color.G - $color.B) * (1 - $alpha / 255.0))
                $source.SetPixel($x, $y, [Drawing.Color]::FromArgb($alpha, [int]$red, [int][Math]::Min($color.G, [Math]::Max($red, $blue)), [int]$blue))
            }
        }
    }
    $source.Save((Join-Path $root 'ares-icon.png'), [Drawing.Imaging.ImageFormat]::Png)

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024)
    $pngData = @{}
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source, 0, 0, $size, $size)
            } finally { $graphics.Dispose() }
            $path = Join-Path $root "ares-icon-$size.png"
            $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            $pngData[$size] = [IO.File]::ReadAllBytes($path)
        } finally { $bitmap.Dispose() }
    }

    $icoSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $ico = [IO.File]::Create((Join-Path $root 'ares.ico'))
    $writer = [IO.BinaryWriter]::new($ico)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$icoSizes.Count)
        $offset = 6 + 16 * $icoSizes.Count
        foreach ($size in $icoSizes) {
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$pngData[$size].Length); $writer.Write([uint32]$offset)
            $offset += $pngData[$size].Length
        }
        foreach ($size in $icoSizes) { $writer.Write($pngData[$size]) }
    } finally { $writer.Dispose(); $ico.Dispose() }
} finally { $source.Dispose() }
