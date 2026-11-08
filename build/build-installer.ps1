# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Makes HeronToolsInstaller.exe - one file that installs Heron Tools into every Revit 2020-2027 on a PC.

.DESCRIPTION
    1. Builds Heron Tools for every Revit year with build\build.ps1 -NoInstall (which runs the metadata check
       and proves each DLL is built for its Revit's .NET).
    2. Packs each year's HeronTools.dll (+ HeronTools.deps.json for 2025 and later) and its manifest into
       installer\payload\HeronTools-payload.zip, with payload.txt listing every file and its SHA-256 - the
       installer checks each file it writes against that list.
    3. Builds installer\HeronTools.Installer with that zip inside, and copies the result to
       dist\HeronToolsInstaller-<version>.exe (and dist\HeronToolsInstaller.exe).
    Nothing is installed on this PC. installer\payload\ and dist\ are not committed.

.PARAMETER SkipBuild
    Pack what is already built in src\HeronTools\bin\Release\<year> instead of building again.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$years      = 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027
$binRoot    = Join-Path $repoRoot 'src\HeronTools\bin\Release'
$payloadDir = Join-Path $repoRoot 'installer\payload'
$stage      = Join-Path $payloadDir 'stage'
$zipPath    = Join-Path $payloadDir 'HeronTools-payload.zip'
$project    = Join-Path $repoRoot 'installer\HeronTools.Installer\HeronTools.Installer.csproj'
$distDir    = Join-Path $repoRoot 'dist'

if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1') -NoInstall
    if ($LASTEXITCODE -ne 0) { Write-Host "Installer not made: the Heron Tools build failed (see above)." -ForegroundColor Red; exit 1 }
}

# ---- stage every year's files
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$index = New-Object System.Collections.Generic.List[string]
$version = $null

foreach ($year in $years) {
    $out = Join-Path $binRoot $year
    $dll = Join-Path $out 'HeronTools.dll'
    if (-not (Test-Path $dll)) { throw "No build for Revit $year in $out - run without -SkipBuild." }
    if (-not $version) { $version = (Get-Item -LiteralPath $dll).VersionInfo.ProductVersion }

    $yearStage = Join-Path $stage "$year\HeronTools"
    New-Item -ItemType Directory -Force -Path $yearStage | Out-Null

    $files = @('HeronTools.dll')
    if ($year -ge 2025) { $files += 'HeronTools.deps.json' }
    foreach ($name in $files) {
        $source = Join-Path $out $name
        if (-not (Test-Path $source)) { throw "Revit $year build has no $name." }
        Copy-Item -LiteralPath $source -Destination $yearStage
        $index.Add("$year|HeronTools/$name|$((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash)")
    }

    $manifest = Join-Path $repoRoot "manifests\HeronTools.$year.addin"
    Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage "$year\HeronTools.addin")
    $index.Add("$year|HeronTools.addin|$((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash)")
}

$lines = @('# Heron Tools installer payload: year|path under that year''s Addins folder|SHA-256',
           "version=$version",
           "built=$((Get-Date).ToString('yyyy-MM-dd HH:mm'))") + $index
[IO.File]::WriteAllLines((Join-Path $stage 'payload.txt'), [string[]]$lines, (New-Object Text.UTF8Encoding $false))

# ---- zip it (forward slashes inside, as the installer reads them)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, 'Optimal')
    }
}
finally { $zip.Dispose() }
Remove-Item -LiteralPath $stage -Recurse -Force

# ---- build the installer with the payload inside
& dotnet build $project -c Release -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "Installer not made: it did not compile (see above)." -ForegroundColor Red; exit 1 }

$exe = Join-Path $repoRoot 'installer\HeronTools.Installer\bin\Release\net48\HeronToolsInstaller.exe'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$plain = ($version -split '\+')[0]
$named = Join-Path $distDir "HeronToolsInstaller-$plain.exe"
Copy-Item -LiteralPath $exe -Destination $named -Force
Copy-Item -LiteralPath $exe -Destination (Join-Path $distDir 'HeronToolsInstaller.exe') -Force

$size = [Math]::Round((Get-Item -LiteralPath $named).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer made: $named ($size MB) - Heron Tools $version for Revit $($years[0]) to $($years[-1])." -ForegroundColor Green
exit 0
