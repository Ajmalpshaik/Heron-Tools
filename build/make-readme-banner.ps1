# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Draws the README banner: the ink heron and the product name on a Mist card with an orange line.

.DESCRIPTION
    Same recipe as the Heron AI platform's README banner (docs/brand/heron-readme-banner.png there), so the
    two products look alike: Mist card #F7F5F2 with 24 px corners, ink #0F0F10, a 7 px Accent #FD641A line
    along the bottom, the heron mark 195 px tall, the name in Manrope SemiBold (set 6 % tighter - the closest
    match to the Heron AI wordmark, checked by redrawing that banner) with a 104 px capital, and the tagline
    in spaced Manrope Regular capitals under it, as wide as the name.

    ONE picture that reads on a light or a dark page. A <picture> that switches on prefers-color-scheme follows
    the computer's setting, not GitHub's theme, and can show a white logo on a white page - Heron AI found
    that on 2026-11-07.

    Drawn at 2x for sharp edges; the README shows it at the width that keeps the card the same height as
    Heron AI's (300 px card shown 162 px tall).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\make-readme-banner.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\make-readme-banner.ps1 -Name "Heron AI" -Out C:\temp\check.png
#>
[CmdletBinding()]
param(
    [string]$Name = 'Heron Tools',
    [string]$Tagline = 'DESIGN AUTOMATE DELIVER',
    [string]$Out,
    [int]$Scale = 2,

    [ValidateSet('Medium', 'SemiBold', 'Bold')]
    [string]$Weight = 'SemiBold',

    [double]$Tighten = 0.94
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repoRoot 'docs\brand\heron-tools-readme-banner.png' }
$fontDir  = Join-Path $repoRoot 'src\HeronTools\Resources\Fonts\'
$markSvg  = Join-Path $repoRoot 'docs\brand\heron-mark-ink.svg'

function Brush([string]$hex) { New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($hex)) }
$mist   = Brush '#FFF7F5F2'
$ink    = Brush '#FF0F0F10'
$accent = Brush '#FFFD641A'

$family = New-Object Windows.Media.FontFamily ([Uri]('file:///' + $fontDir.Replace('\', '/'))), './#Manrope'
$medium = New-Object Windows.Media.Typeface $family, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::$Weight), ([Windows.FontStretches]::Normal)
$glyph = $null
if (-not $medium.TryGetGlyphTypeface([ref]$glyph)) { throw "Manrope not found in $fontDir" }

# The tagline is lighter than the name, as on the Heron AI banner.
$regular = New-Object Windows.Media.Typeface $family, ([Windows.FontStyles]::Normal), ([Windows.FontWeights]::Regular), ([Windows.FontStretches]::Normal)

function Text([string]$s, [double]$size, $face = $medium) {
    New-Object Windows.Media.FormattedText $s, ([Globalization.CultureInfo]::InvariantCulture),
        ([Windows.FlowDirection]::LeftToRight), $face, $size, $ink, 1.0
}

# ---- layout, in banner pixels (Heron AI's banner measured: mark x 100, y 47..242; name from x 334,
#      capital 93..196; tagline capitals 213..229; line rows 292..298; card 300 tall)
$markX = 100; $markTop = 47; $markHeight = 195
$nameX = 334; $capTop = 93; $capHeight = 104
$tagTop = 213; $tagCap = 16.5
$tighten = $Tighten

# The name: Manrope Medium with Manrope's own kerning, then 4 % narrower as one piece.
$nameSize = $capHeight / $glyph.CapsHeight
$nameGeo = (Text $Name $nameSize).BuildGeometry((New-Object Windows.Point 0, 0))
$nb = $nameGeo.Bounds
$nameWidth = $nb.Width * $tighten
$width = [Math]::Ceiling($nameX + $nameWidth + 100)
$height = 300

# The tagline: spaced capitals, first letter at the name's left edge, last letter at its right edge.
$tagSize = $tagCap / $glyph.CapsHeight
$chars = $Tagline.ToCharArray()
$pieces = foreach ($c in $chars) {
    $t = Text ([string]$c) $tagSize $regular
    [pscustomobject]@{ Geo = $t.BuildGeometry((New-Object Windows.Point 0, 0)); Advance = $t.WidthIncludingTrailingWhitespace }
}
$natural = ($pieces | Measure-Object -Property Advance -Sum).Sum
$lastInk = $pieces[-1].Geo.Bounds
$naturalInk = $natural - $pieces[-1].Advance + $lastInk.Right - $pieces[0].Geo.Bounds.X
$spacing = ($nameWidth - $naturalInk) / ($chars.Count - 1)

# The heron mark from the brand SVG.
$svg = [IO.File]::ReadAllText($markSvg)
$paths = [regex]::Matches($svg, ' d="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
$mark = [Windows.Media.Geometry]::Parse(($paths -join ' '))
$mb = $mark.Bounds
$markScale = $markHeight / $mb.Height

$dv = New-Object Windows.Media.DrawingVisual
$dc = $dv.RenderOpen()
$dc.PushTransform((New-Object Windows.Media.ScaleTransform $Scale, $Scale))

# Card: rounded, Mist, with the orange line clipped into its bottom corners.
$card = New-Object Windows.Media.RectangleGeometry (New-Object Windows.Rect 0, 0, ($width - 1), ($height - 1)), 24, 24
$dc.PushClip($card)
$dc.DrawRectangle($mist, $null, (New-Object Windows.Rect 0, 0, $width, $height))
$dc.DrawRectangle($accent, $null, (New-Object Windows.Rect 0, 292, $width, 7))
$dc.Pop()

# Mark
$dc.PushTransform((New-Object Windows.Media.TranslateTransform $markX, $markTop))
$dc.PushTransform((New-Object Windows.Media.ScaleTransform $markScale, $markScale))
$dc.PushTransform((New-Object Windows.Media.TranslateTransform (-$mb.X), (-$mb.Y)))
$dc.DrawGeometry($ink, $null, $mark)
$dc.Pop(); $dc.Pop(); $dc.Pop()

# Name: capital top at $capTop (the H's top is the bounds' top).
$dc.PushTransform((New-Object Windows.Media.TranslateTransform $nameX, $capTop))
$dc.PushTransform((New-Object Windows.Media.ScaleTransform $tighten, 1))
$dc.PushTransform((New-Object Windows.Media.TranslateTransform (-$nb.X), (-$nb.Y)))
$dc.DrawGeometry($ink, $null, $nameGeo)
$dc.Pop(); $dc.Pop(); $dc.Pop()

# Tagline: capital tops at $tagTop.
$x = $nameX - $pieces[0].Geo.Bounds.X
$capY = $tagTop - ($tagSize * ($glyph.Baseline - $glyph.CapsHeight))
foreach ($p in $pieces) {
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform $x, $capY))
    $dc.DrawGeometry($ink, $null, $p.Geo)
    $dc.Pop()
    $x += $p.Advance + $spacing
}

$dc.Pop()
$dc.Close()

$bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap ($width * $Scale), ($height * $Scale), 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
$bmp.Render($dv)
$enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
$fs = [IO.File]::Create($Out); $enc.Save($fs); $fs.Close()

# README width that gives the same card height as Heron AI's (960 x 300 shown 520 wide = 162.5 tall).
$shownWidth = [Math]::Round($width * 162.5 / $height)
"Banner: $Out ($($width * $Scale) x $($height * $Scale) px). Show it in the README at width=$shownWidth."
