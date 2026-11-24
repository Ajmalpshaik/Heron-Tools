# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.2.2
# Heron-Layer:  tool
# See docs/metadata-standard.md

# Heron Tools icon studio - export. Opens icon-studio.html in headless Microsoft Edge, takes the PNGs the page
# drew (the hidden #export block) and writes them to the export folder, then checks every file: size from its
# name (16, 32, @2x), 32-bit with transparency, clear corners, nothing cut at the border. Optional -Shot saves a
# screenshot of the page at 150 % (the owner's screen). Copy a checked file into src\HeronTools\Resources\Icons
# only after the owner's typed "yes".
#   powershell -ExecutionPolicy Bypass -File tools\icon-studio\export-icons.ps1 [-Shot page.png] [-Sheet sheet.png]
param([string]$Html = (Join-Path $PSScriptRoot 'icon-studio.html'), [string]$OutDir = (Join-Path $PSScriptRoot 'export'), [string]$Shot,
      [string]$Sheet, [int]$SheetHeight = 1700)
$e = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$u = "file:///" + ($Html -replace '\\', '/')
$prof = "$env:TEMP\heron-edge-shot"
$dom = & $e --headless=new --disable-gpu --allow-file-access-from-files --virtual-time-budget=5000 --user-data-dir=$prof --dump-dom $u 2>$null | Out-String
$m = [regex]::Match($dom, '<pre id="export">(.*?)</pre>', 'Singleline')
if (-not $m.Success -or $m.Groups[1].Value.Trim() -eq '') { throw "No export found in the page." }
$json = [System.Net.WebUtility]::HtmlDecode($m.Groups[1].Value) | ConvertFrom-Json
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($p in $json.PSObject.Properties) {
  $b64 = $p.Value -replace '^data:image/png;base64,', ''
  [IO.File]::WriteAllBytes((Join-Path $OutDir $p.Name), [Convert]::FromBase64String($b64))
}
# Check every file: size from its name, 32-bit with alpha, corners fully transparent.
Add-Type -AssemblyName System.Drawing
Get-ChildItem $OutDir -Filter *.png | ForEach-Object {
  $m2 = [regex]::Match($_.Name, '(16|32)(@2x)?\.png$'); $want = [int]$m2.Groups[1].Value * $(if ($m2.Groups[2].Success) { 2 } else { 1 })
  $b = New-Object System.Drawing.Bitmap $_.FullName
  $corners = @($b.GetPixel(0, 0).A, $b.GetPixel($b.Width - 1, 0).A, $b.GetPixel(0, $b.Height - 1).A, $b.GetPixel($b.Width - 1, $b.Height - 1).A)
  # nothing cut off at the border: the outer ring of pixels stays almost clear
  $edge = 0; for ($i = 0; $i -lt $b.Width; $i++) { foreach ($px in @($b.GetPixel($i, 0), $b.GetPixel($i, $b.Height - 1), $b.GetPixel(0, $i), $b.GetPixel($b.Width - 1, $i))) { if ($px.A -gt $edge) { $edge = $px.A } } }
  $ok = ($b.Width -eq $want -and $b.Height -eq $want -and ($corners | Measure-Object -Maximum).Maximum -eq 0 -and $edge -le 24 -and $b.PixelFormat -eq 'Format32bppArgb')
  "{0,-28} {1}x{2} corners-alpha={3} edge-alpha-max={4} {5}" -f $_.Name, $b.Width, $b.Height, ($corners -join ','), $edge, $(if ($ok) { 'PASS' } else { 'FAIL' })
  $b.Dispose()
}
if ($Shot) {
  & $e --headless=new --disable-gpu --hide-scrollbars --allow-file-access-from-files --virtual-time-budget=5000 --window-size=1280,2400 --force-device-scale-factor=1.5 --user-data-dir=$prof --screenshot=$Shot $u 2>$null | Out-Null
}
# -Sheet: a screenshot of the review sheet only (every icon still being chosen, one row each)
if ($Sheet) {
  & $e --headless=new --disable-gpu --hide-scrollbars --allow-file-access-from-files --virtual-time-budget=8000 --window-size=1280,$SheetHeight --force-device-scale-factor=1.5 --user-data-dir=$prof --screenshot=$Sheet "$u#sheet" 2>$null | Out-Null
}








