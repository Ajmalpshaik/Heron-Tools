# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.3.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Makes dist\HeronTools-Store-<version>.zip - the one file Autodesk asks for to list Heron Tools on the
    Autodesk App Store (Design and Make Marketplace). Installs nothing.

.DESCRIPTION
    Autodesk builds the Store installer themselves from one zip that holds the add-in and a help file.
    This script makes that zip:
      1. runs the metadata check (as build.ps1 does);
      2. builds the STORE variant of Heron Tools (-p:HeronStore=true: no Model Walk) for each Store year,
         into its own folder src\HeronTools\bin\Release\Store\<year> - the normal build is never touched;
      3. proves each DLL is built for that Revit's .NET and has no Model Walk left in it;
      4. lays out an Autodesk bundle (docs/store/package.md has the picture):
           HeronTools.bundle\PackageContents.xml            one <Components> per Revit year
           HeronTools.bundle\Contents\<year>\HeronTools.addin   = manifests\HeronTools.<year>.addin, unchanged
           HeronTools.bundle\Contents\<year>\HeronTools\HeronTools.dll (+ .deps.json from 2025)
           HeronTools.bundle\Contents\Resources\            help file, Store icon, LICENSE, NOTICE, font licence
      5. checks the bundle (the XML reads back, every path in it is there, every AddInId is today's one);
      6. zips it with forward slashes and lists what is inside.

    Revit loads a bundle from the ApplicationPlugins folder (%AppData% or %ProgramData%\Autodesk\ApplicationPlugins);
    Autodesk's installer puts it there. The layout follows Autodesk's own Revit bundle for the IFC exporter
    (github.com/Autodesk/revit-ifc, Install\Program Files to Install\bundle\PackageContents.xml): one <Components>
    block per Revit year with SeriesMin = SeriesMax = R<year>.

.PARAMETER Years
    The Revit years in the Store zip. Standard: 2022 to 2027 (Autodesk asks for 2027; 2022-2026 may be listed).
    Adding 2020 and 2021 later, if Autodesk agrees, is the one line $storeYears below.

.PARAMETER SkipBuild
    Pack what is already built in src\HeronTools\bin\Release\Store\<year> instead of building again.

.PARAMETER SupportEmail
    The public support email written into PackageContents.xml (CompanyDetails). Left empty, the zip is made
    but the summary says it is not ready to submit.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build-store-package.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build-store-package.ps1 -Years 2026,2027 -SkipBuild
#>
[CmdletBinding()]
param(
    # Text, not numbers, for the same reason as build.ps1: "-Years 2026,2027" arrives as ONE piece of text.
    [string[]]$Years,

    [switch]$SkipBuild,

    [string]$SupportEmail = ''
)

$ErrorActionPreference = 'Stop'

# THE STORE YEARS - the one line to change. 2020 and 2021 are a question to Autodesk; add them here if they agree.
$storeYears = @(2022, 2023, 2024, 2025, 2026, 2027)

# Fixed for the life of the product: Autodesk's installer uses it to know a new version replaces the old one.
# Never change it, never reuse it elsewhere. (ProductCode is made from it and the version - see Get-ProductCode.)
$upgradeCode = '{57635C9D-886C-474D-8845-A71D1D68C10C}'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$project    = Join-Path $repoRoot 'src\HeronTools\HeronTools.csproj'
$binRoot    = Join-Path $repoRoot 'src\HeronTools\bin\Release\Store'
$distDir    = Join-Path $repoRoot 'dist'
$stageRoot  = Join-Path $distDir 'store-stage'
$bundle     = Join-Path $stageRoot 'HeronTools.bundle'
$contents   = Join-Path $bundle 'Contents'
$resources  = Join-Path $contents 'Resources'
$helpDir    = Join-Path $repoRoot 'docs\store\help'
$helpName   = 'HeronTools-Help.html'
$storeIcon  = Join-Path $repoRoot 'docs\store\heron-tools-store-icon-120.png'
$brandIcon  = Join-Path $repoRoot 'docs\brand\heron-app-icon-orange.png'
$maxZipMB   = 600   # Autodesk's limit for the app file

$notReady = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------------------------------------------
# Years
# ---------------------------------------------------------------------------------------------------
$yearList = @()
if ($Years) {
    foreach ($part in @($Years | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })) {
        $number = 0
        if (-not [int]::TryParse($part, [ref]$number) -or $number -lt 2000 -or $number -gt 2100) {
            Write-Host "'$part' is not a Revit year. Use for example: -Years 2026,2027" -ForegroundColor Red
            exit 1
        }
        $yearList += $number
    }
}
else { $yearList = $storeYears }
$yearList = [int[]]($yearList | Sort-Object -Unique)
if ($yearList -notcontains 2027) { $notReady.Add('Revit 2027 is not in the zip - Autodesk asks for it') }

# ---------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------

# Same table as Directory.Build.props and build.ps1.
function Get-ExpectedTargetFramework([int]$year) {
    if ($year -eq 2020)                     { return '.NETFramework,Version=v4.7.2' }
    if ($year -ge 2021 -and $year -le 2024) { return '.NETFramework,Version=v4.8' }
    if ($year -ge 2025 -and $year -le 2026) { return '.NETCoreApp,Version=v8.0' }
    if ($year -eq 2027)                     { return '.NETCoreApp,Version=v10.0' }
    return $null
}

function Get-AssemblyTargetFramework([string]$dllPath) {
    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($dllPath))
    $found = [regex]::Matches($text, '\.NET(?:Framework|CoreApp),Version=v\d+(?:\.\d+)+') |
        ForEach-Object { $_.Value } | Sort-Object -Unique
    return @($found)
}

# Any trace of Model Walk in a DLL: type and window names (ASCII, BAML names are lower case) and tooltip
# text (UTF-16, which can start on an even or an odd byte). Returns the words found.
function Find-ModelWalk([string]$dllPath) {
    $bytes = [IO.File]::ReadAllBytes($dllPath)
    $texts = @(
        [Text.Encoding]::ASCII.GetString($bytes).ToLowerInvariant(),
        [Text.Encoding]::Unicode.GetString($bytes).ToLowerInvariant(),
        [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1).ToLowerInvariant()
    )
    $hits = @()
    foreach ($word in 'modelwalk', 'walkcharacter', 'model walk', "model`nwalk") {
        foreach ($t in $texts) { if ($t.Contains($word)) { $hits += $word; break } }
    }
    return @($hits | Sort-Object -Unique)
}

# A ProductCode that is the same every time for the same version and new for every new version
# (Autodesk's package rules: a new ProductCode when AppVersion changes). Made from a SHA-1 of the
# UpgradeCode and the version, shaped as a name-based GUID - never a random New-Guid per run.
function Get-ProductCode([string]$version) {
    $sha = [Security.Cryptography.SHA1]::Create()
    try { $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("HeronTools.bundle|$upgradeCode|$version")) }
    finally { $sha.Dispose() }
    $b = $hash[0..15]
    $b[6] = ($b[6] -band 0x0F) -bor 0x50   # version 5 (name-based)
    $b[8] = ($b[8] -band 0x3F) -bor 0x80   # RFC 4122 variant
    $hex = ($b | ForEach-Object { $_.ToString('X2') }) -join ''
    return '{' + $hex.Substring(0, 8) + '-' + $hex.Substring(8, 4) + '-' + $hex.Substring(12, 4) + '-' +
           $hex.Substring(16, 4) + '-' + $hex.Substring(20, 12) + '}'
}

function Esc([string]$text) { return [Security.SecurityElement]::Escape($text) }

$results = New-Object System.Collections.Generic.List[object]
function Add-Result($year, $build, $check, $note = '') {
    $results.Add([pscustomobject]@{ Revit = $year; Build = $build; Check = $check; Note = $note })
}

# ---------------------------------------------------------------------------------------------------
# Version (one place: the csproj)
# ---------------------------------------------------------------------------------------------------
$version = ([xml](Get-Content -Raw -LiteralPath $project)).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not ($version -match '^\d+\.\d+\.\d+$')) { throw "No usable <Version> in $project ('$version')." }
$productCode = Get-ProductCode $version

# ---------------------------------------------------------------------------------------------------
# Build the Store variant
# ---------------------------------------------------------------------------------------------------
if (-not $SkipBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK (dotnet) is not on this PC's PATH. Install the .NET 10 SDK and run again."
    }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-metadata.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Stopped: fix the metadata listed above (see docs\metadata-standard.md)." -ForegroundColor Red
        exit 1
    }
}

$good = @()
foreach ($year in $yearList) {
    Write-Host ""
    Write-Host "=== Revit $year (Store build) ===" -ForegroundColor Cyan

    $expectedTfm = Get-ExpectedTargetFramework $year
    if (-not $expectedTfm) { Add-Result $year 'NOT RUN' 'not checked' "Revit $year is not in the .NET table"; continue }
    $manifest = Join-Path $repoRoot "manifests\HeronTools.$year.addin"
    if (-not (Test-Path $manifest)) { Add-Result $year 'NOT RUN' 'not checked' "no manifest for $year in manifests\"; continue }

    if (-not $SkipBuild) {
        & dotnet build $project -c Release "-p:RevitVersion=$year" '-p:HeronStore=true' -nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { Add-Result $year 'FAIL' 'not checked' 'see the build errors above'; continue }
    }

    $outDir = Join-Path $binRoot $year
    $dll = Join-Path $outDir 'HeronTools.dll'
    if (-not (Test-Path $dll)) { Add-Result $year 'FAIL' 'not checked' "no HeronTools.dll in $outDir"; continue }

    $actualTfm = @(Get-AssemblyTargetFramework $dll)
    if ($actualTfm.Count -ne 1 -or $actualTfm[0] -ne $expectedTfm) {
        $seen = if ($actualTfm.Count -eq 0) { 'none' } else { $actualTfm -join ' + ' }
        Add-Result $year 'PASS' 'FAIL' "DLL is built for $seen, Revit $year needs $expectedTfm"; continue
    }
    if ($year -ge 2025 -and -not (Test-Path (Join-Path $outDir 'HeronTools.deps.json'))) {
        Add-Result $year 'PASS' 'FAIL' 'no HeronTools.deps.json (needed by .NET add-ins)'; continue
    }
    $dllVersion = (Get-Item -LiteralPath $dll).VersionInfo.ProductVersion
    if (-not $dllVersion.StartsWith($version)) {
        Add-Result $year 'PASS' 'FAIL' "DLL version $dllVersion is not the csproj version $version - build again without -SkipBuild"; continue
    }
    $walk = @(Find-ModelWalk $dll)
    if ($walk.Count -gt 0) {
        Add-Result $year 'PASS' 'FAIL' ("Model Walk is still in the Store DLL (found: " + ($walk -join ', ') + ")"); continue
    }
    $good += $year
    Add-Result $year 'PASS' 'PASS' "$expectedTfm, no Model Walk"
}

if ($good.Count -ne $yearList.Count) {
    Write-Host ""
    $results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
    Write-Host "No zip made: a year above did not build or check." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------------------------------
# Lay out the bundle
# ---------------------------------------------------------------------------------------------------
if (Test-Path $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $resources | Out-Null

foreach ($year in $yearList) {
    $outDir  = Join-Path $binRoot $year
    $yearDir = Join-Path $contents $year
    $dllDir  = Join-Path $yearDir 'HeronTools'
    New-Item -ItemType Directory -Force -Path $dllDir | Out-Null
    # Today's manifest, unchanged: same AddInId for that year, Assembly path HeronTools\HeronTools.dll.
    Copy-Item -LiteralPath (Join-Path $repoRoot "manifests\HeronTools.$year.addin") -Destination (Join-Path $yearDir 'HeronTools.addin')
    $files = @('HeronTools.dll')
    if ($year -ge 2025) { $files += 'HeronTools.deps.json' }
    foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $outDir $name) -Destination $dllDir }
}

# Help file: written in docs\store\help by the Store paperwork work. Everything in that folder goes in
# (in case the page gains pictures). Without it the zip still gets a placeholder, and is not ready to submit.
$helpFile = Join-Path $helpDir $helpName
if (Test-Path -LiteralPath $helpFile) {
    Get-ChildItem -LiteralPath $helpDir -File | Copy-Item -Destination $resources
}
else {
    $placeholder = "<!DOCTYPE html>`r`n<html><head><meta charset=""utf-8""><title>Heron Tools - help</title></head>" +
        "<body><h1>Heron Tools</h1><p>TODO: the real help page (docs/store/help/$helpName) was not in the repo " +
        "when this zip was made. Do not submit this zip.</p></body></html>`r`n"
    [IO.File]::WriteAllText((Join-Path $resources $helpName), $placeholder, (New-Object Text.UTF8Encoding $false))
    $notReady.Add("help file missing: $($helpFile.Substring($repoRoot.Length + 1)) - a TODO placeholder was packed")
}

# Store icon: the 120 x 120 Store icon if it is there, else the brand icon it is made from.
$iconName = 'HeronTools.png'
if (Test-Path -LiteralPath $storeIcon) { Copy-Item -LiteralPath $storeIcon -Destination (Join-Path $resources $iconName) }
elseif (Test-Path -LiteralPath $brandIcon) {
    Copy-Item -LiteralPath $brandIcon -Destination (Join-Path $resources $iconName)
    $notReady.Add('Store icon missing: docs\store\heron-tools-store-icon-120.png - the 512 px brand icon was packed instead')
}
else { throw "No icon: neither $storeIcon nor $brandIcon is there." }

# Licences that must travel with the add-in: Apache 2.0 (LICENSE + NOTICE) and the Manrope font's OFL
# (the font is built into the DLL).
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $resources 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'NOTICE') -Destination (Join-Path $resources 'NOTICE.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\HeronTools\Resources\Fonts\OFL.txt') -Destination (Join-Path $resources 'Manrope-OFL.txt')

if (-not $SupportEmail) { $notReady.Add('no -SupportEmail: CompanyDetails Email in PackageContents.xml is empty') }

# ---------------------------------------------------------------------------------------------------
# PackageContents.xml - root and CompanyDetails as in Autodesk's own Revit bundle; one <Components> per year
# ---------------------------------------------------------------------------------------------------
$repoUrl     = 'https://github.com/Ajmalpshaik/Heron-Tools'
$description = 'Everyday BIM tools for Revit on the Heron tab: view filters and colours, view crop and section box, ' +
               'grids and levels, MEP elevations, selection, a quick clash check and element IDs. Free and open source.'
$minYear = ($yearList | Measure-Object -Minimum).Minimum
$maxYear = ($yearList | Measure-Object -Maximum).Maximum

$xml = New-Object System.Text.StringBuilder
[void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$xml.AppendLine('<!-- Made by build/build-store-package.ps1 - do not edit by hand. See docs/store/package.md. -->')
# (Each line is formatted first, then appended: inside AppendLine( ) the commas of -f would become its arguments.)
$root = ('<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Revit" ProductType="Application" ' +
    'Name="Heron Tools" AppVersion="{0}" Description="{1}" Author="Ajmal PS" ' +
    'Icon="./Contents/Resources/{2}" AppNameSpace="appstore.exchange.autodesk.com" OnlineDocumentation="{3}" ' +
    'HelpFile="./Contents/Resources/{4}" ProductCode="{5}" UpgradeCode="{6}" SupportedLocales="Enu">') -f
    (Esc $version), (Esc $description), $iconName, (Esc $repoUrl), $helpName, $productCode, $upgradeCode
[void]$xml.AppendLine($root)
[void]$xml.AppendLine(('  <CompanyDetails Name="Ajmal PS" Url="{0}" Email="{1}" />' -f (Esc "$repoUrl/issues"), (Esc $SupportEmail)))
[void]$xml.AppendLine(('  <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R{0}" SeriesMax="R{1}" />' -f $minYear, $maxYear))
foreach ($year in $yearList) {
    [void]$xml.AppendLine(('  <Components Description="Revit {0}">' -f $year))
    [void]$xml.AppendLine(('    <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R{0}" SeriesMax="R{0}" />' -f $year))
    $entryLine = ('    <ComponentEntry AppName="Heron Tools" Version="{0}" ModuleName="./Contents/{1}/HeronTools.addin" ' +
        'AppDescription="Heron Tools for Revit {1}" />') -f (Esc $version), $year
    [void]$xml.AppendLine($entryLine)
    [void]$xml.AppendLine('  </Components>')
}
[void]$xml.AppendLine('</ApplicationPackage>')
$packageFile = Join-Path $bundle 'PackageContents.xml'
[IO.File]::WriteAllText($packageFile, $xml.ToString(), (New-Object Text.UTF8Encoding $false))

# ---------------------------------------------------------------------------------------------------
# Check the bundle before zipping it
# ---------------------------------------------------------------------------------------------------
$problems = New-Object System.Collections.Generic.List[string]
$package = $null
try { $package = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $packageFile) }
catch { $problems.Add("PackageContents.xml does not read back as XML: $($_.Exception.Message)") }

if ($package) {
    foreach ($attr in 'HelpFile', 'Icon') {
        $rel = $package.ApplicationPackage.GetAttribute($attr)
        if (-not (Test-Path -LiteralPath (Join-Path $bundle ($rel -replace '^\./', '')))) { $problems.Add("$attr '$rel' is not in the bundle") }
    }
    $entries = @($package.ApplicationPackage.Components)
    if ($entries.Count -ne $yearList.Count) { $problems.Add("PackageContents.xml has $($entries.Count) <Components>, expected $($yearList.Count)") }
    foreach ($component in $entries) {
        $module = $component.ComponentEntry.ModuleName
        $addin  = Join-Path $bundle ($module -replace '^\./', '')
        if (-not (Test-Path -LiteralPath $addin)) { $problems.Add("ModuleName '$module' is not in the bundle"); continue }
        $year = [int]($component.RuntimeRequirements.SeriesMin -replace '^R', '')
        $manifest = [xml](Get-Content -Raw -LiteralPath $addin)
        $assembly = Join-Path (Split-Path -Parent $addin) $manifest.RevitAddIns.AddIn.Assembly
        if (-not (Test-Path -LiteralPath $assembly)) { $problems.Add("Revit ${year}: the .addin points at '$($manifest.RevitAddIns.AddIn.Assembly)', which is not beside it") }
        $original = [xml](Get-Content -Raw -LiteralPath (Join-Path $repoRoot "manifests\HeronTools.$year.addin"))
        if ($manifest.RevitAddIns.AddIn.AddInId -ne $original.RevitAddIns.AddIn.AddInId) { $problems.Add("Revit ${year}: AddInId is not today's one") }
        if ($component.RuntimeRequirements.SeriesMax -ne "R$year") { $problems.Add("Revit ${year}: SeriesMin and SeriesMax differ") }
    }
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "BUNDLE CHECK FAIL: $_" -ForegroundColor Red }
    Write-Host "No zip made." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------------------------------
# Zip (forward slashes, HeronTools.bundle at the top)
# ---------------------------------------------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipPath = Join-Path $distDir "HeronTools-Store-$version.zip"
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($file in Get-ChildItem -LiteralPath $stageRoot -Recurse -File) {
        $entry = $file.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, 'Optimal')
    }
}
finally { $zip.Dispose() }
Remove-Item -LiteralPath $stageRoot -Recurse -Force

$read = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $listing = @($read.Entries | ForEach-Object { [pscustomobject]@{ Size = $_.Length; Entry = $_.FullName } })
}
finally { $read.Dispose() }
$bad = @($listing | Where-Object { $_.Entry -notlike 'HeronTools.bundle/*' -or $_.Entry.Contains('\') })
$sizeMB = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)

Write-Host ""
Write-Host "Store build summary" -ForegroundColor Cyan
$results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
Write-Host "Inside $zipPath ($sizeMB MB):" -ForegroundColor Cyan
$listing | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
Write-Host "Version $version, ProductCode $productCode, UpgradeCode $upgradeCode"

if ($bad.Count -gt 0) { Write-Host ("ZIP CHECK FAIL: entries outside HeronTools.bundle/: " + (($bad | ForEach-Object { $_.Entry }) -join ', ')) -ForegroundColor Red; exit 1 }
if ($sizeMB -gt $maxZipMB) { Write-Host "ZIP CHECK FAIL: $sizeMB MB is over Autodesk's $maxZipMB MB limit." -ForegroundColor Red; exit 1 }

if ($notReady.Count -gt 0) {
    Write-Host ""
    Write-Host "Zip made, but NOT READY TO SUBMIT:" -ForegroundColor Yellow
    $notReady | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}
else {
    Write-Host ""
    Write-Host "Zip made and checked. The install test in a real Revit is still to do (docs\store\package.md)." -ForegroundColor Green
}
exit 0
