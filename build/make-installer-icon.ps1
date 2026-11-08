# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Makes the installer's program icon (HeronTools.ico) from the brand's orange app icon.

.DESCRIPTION
    docs/brand/heron-app-icon-orange.png (the white heron on the brand's orange) at 16, 24, 32, 48, 64 and
    256 px, packed into one .ico - each size stored as PNG, which every Windows since Vista reads. The icon
    shows in the Downloads folder, on the taskbar and in Alt+Tab, like Heron AI's installer.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\make-installer-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'docs\brand\heron-app-icon-orange.png'
if (-not $Out) { $Out = Join-Path $repoRoot 'installer\HeronTools.Installer\Resources\HeronTools.ico' }

$original = New-Object Windows.Media.Imaging.BitmapImage
$original.BeginInit(); $original.UriSource = [Uri]$source; $original.CacheOption = 'OnLoad'; $original.EndInit()

$images = foreach ($size in 16, 24, 32, 48, 64, 256) {
    $scale = $size / $original.PixelWidth
    $small = New-Object Windows.Media.Imaging.TransformedBitmap $original, (New-Object Windows.Media.ScaleTransform $scale, $scale)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($small))
    $memory = New-Object IO.MemoryStream
    $encoder.Save($memory)
    [pscustomobject]@{ Size = $size; Bytes = $memory.ToArray() }
}

# ICO: a 6-byte header, a 16-byte entry per image, then the images.
$stream = [IO.File]::Create($Out)
$writer = New-Object IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $side = if ($image.Size -ge 256) { 0 } else { $image.Size }   # 0 means 256
    $writer.Write([byte]$side); $writer.Write([byte]$side)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $writer.Write($image.Bytes) }
$writer.Close()
"Icon: $Out ($($images.Count) sizes)"
