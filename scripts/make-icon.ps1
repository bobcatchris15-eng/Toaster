# Generates the Toaster application mark.
#
# The mark is deliberately abstract: a dark slab carrying an amber slot rule with
# two blocks rising out of it. It reuses the console palette so the icon, the
# window chrome and the metric tiles read as one system. Geometry is defined in
# unit coordinates and snapped to whole pixels per size, so the 16px rendering is
# drawn rather than downsampled.
#
# Outputs assets\Toaster.ico (multi-resolution) and assets\Toaster.png (preview).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'

$Bg     = [System.Drawing.Color]::FromArgb(0x13, 0x1C, 0x25)
$Edge   = [System.Drawing.Color]::FromArgb(0x2A, 0x3A, 0x4A)
$Amber  = [System.Drawing.Color]::FromArgb(0xFF, 0x9A, 0x3C)
$Slate  = [System.Drawing.Color]::FromArgb(0x3D, 0x54, 0x69)
$Slate2 = [System.Drawing.Color]::FromArgb(0x63, 0x81, 0x9B)

# Distillation, read bottom to top: a wide raw source narrows through an
# intermediate pass into one compact amber bar of Toast. x0, y0, x1, y1 in unit
# space plus fill colour.
$Shapes = @(
    @{ R = @(0.14, 0.66, 0.86, 0.79); C = $Slate  }  # raw source
    @{ R = @(0.24, 0.44, 0.76, 0.57); C = $Slate2 }  # sectioned
    @{ R = @(0.34, 0.22, 0.66, 0.35); C = $Amber  }  # distilled Toast
)

function New-Mark([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

    $slab = New-Object System.Drawing.SolidBrush($Bg)
    $g.FillRectangle($slab, 0, 0, $Size, $Size)
    $slab.Dispose()

    # The hairline border only survives above 24px; below that it eats the mark.
    if ($Size -ge 24) {
        $pen = New-Object System.Drawing.Pen($Edge)
        $g.DrawRectangle($pen, 0, 0, $Size - 1, $Size - 1)
        $pen.Dispose()
    }

    foreach ($shape in $Shapes) {
        $x0 = [int][Math]::Round($shape.R[0] * $Size)
        $y0 = [int][Math]::Round($shape.R[1] * $Size)
        $x1 = [int][Math]::Round($shape.R[2] * $Size)
        $y1 = [int][Math]::Round($shape.R[3] * $Size)
        # Nothing may collapse to zero at 16px.
        $w = [Math]::Max(1, $x1 - $x0)
        $h = [Math]::Max(1, $y1 - $y0)
        $brush = New-Object System.Drawing.SolidBrush($shape.C)
        $g.FillRectangle($brush, $x0, $y0, $w, $h)
        $brush.Dispose()
    }

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$Bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

# A classic DIB frame: BITMAPINFOHEADER with doubled height, a bottom-up BGRA
# image, then the AND mask. PNG-compressed frames are only safe at the large
# sizes; GDI+ refuses to parse an icon whose small frames are PNG.
function Get-DibBytes([System.Drawing.Bitmap]$Bitmap) {
    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([uint32]40)          # biSize
    $writer.Write([int32]$w)           # biWidth
    $writer.Write([int32]($h * 2))     # biHeight: image plus mask
    $writer.Write([uint16]1)           # biPlanes
    $writer.Write([uint16]32)          # biBitCount
    $writer.Write([uint32]0)           # biCompression: BI_RGB
    $writer.Write([uint32]($w * $h * 4))
    $writer.Write([int32]0); $writer.Write([int32]0)
    $writer.Write([uint32]0); $writer.Write([uint32]0)

    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$p.B); $writer.Write([byte]$p.G)
            $writer.Write([byte]$p.R); $writer.Write([byte]$p.A)
        }
    }

    # Fully opaque mark, so the AND mask is all zeros — but it must still be there.
    $maskRow = [int][Math]::Floor(($w + 31) / 32) * 4
    $writer.Write((New-Object byte[] ($maskRow * $h)))

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    return ,$bytes
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) {
    $bmp = New-Mark $size
    $data = if ($size -ge 128) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $entry = [pscustomobject]@{ Size = $size; Data = $data }
    if ($size -eq 256) { $bmp.Save((Join-Path $assets 'Toaster.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $entry
}

# ICO container: ICONDIR, then one ICONDIRENTRY per frame, then the PNG payloads.
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([uint16]0)               # reserved
$writer.Write([uint16]1)               # type: icon
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)             # palette entries
    $writer.Write([byte]0)             # reserved
    $writer.Write([uint16]1)           # colour planes
    $writer.Write([uint16]32)          # bits per pixel
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }

$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'Toaster.ico'), $ico.ToArray())
$writer.Dispose()

"Wrote assets\Toaster.ico ($($frames.Count) sizes) and assets\Toaster.png"
