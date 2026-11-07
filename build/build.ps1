<#
.SYNOPSIS
    Builds Heron Tools for every Revit release and installs it into each Revit installed on this PC.

.DESCRIPTION
    For each Revit year (2020 to 2027 by default):
      1. builds src/HeronTools/HeronTools.csproj with -p:RevitVersion=<year>
      2. if that Revit is installed (C:\Program Files\Autodesk\Revit <year>\Revit.exe) and not running,
         copies the build into   %AppData%\Autodesk\Revit\Addins\<year>\HeronTools\
         and the manifest into   %AppData%\Autodesk\Revit\Addins\<year>\HeronTools.addin
    Years whose Revit is not installed are built but not installed. A year whose Revit is open is
    skipped for install (the DLL would be locked) - close Revit and run again.

    Per-user install only. It needs no admin rights and works the same on every release, including
    2027 (Revit 2027 moved only the ALL-USERS folder, to Program Files).

    Touches nothing but Heron Tools' own two items in each Addins folder.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1 -Years 2024,2026 -NoInstall
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [int[]]$Years = @(2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027),

    [switch]$NoInstall
)

$ErrorActionPreference = 'Stop'

$repoRoot    = Split-Path -Parent $PSScriptRoot
$project     = Join-Path $repoRoot 'src\HeronTools\HeronTools.csproj'
$manifestDir = Join-Path $repoRoot 'manifests'
$productName = 'HeronTools'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK (dotnet) is not on this PC's PATH. Install the .NET 10 SDK and run again."
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($year in $Years) {
    Write-Host ""
    Write-Host "=== Revit $year ===" -ForegroundColor Cyan

    $manifest = Join-Path $manifestDir "$productName.$year.addin"
    if (-not (Test-Path $manifest)) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'NOT RUN'; Install = "no manifest for $year in manifests\" })
        continue
    }

    & dotnet build $project -c $Configuration "-p:RevitVersion=$year" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'FAIL'; Install = 'not attempted' })
        continue
    }

    $outDir = Join-Path $repoRoot "src\HeronTools\bin\$Configuration\$year"
    $dll    = Join-Path $outDir "$productName.dll"
    if (-not (Test-Path $dll)) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'FAIL'; Install = "no $productName.dll in $outDir" })
        continue
    }

    if ($NoInstall) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = 'skipped (-NoInstall)' })
        continue
    }

    $revitExe = Join-Path $env:ProgramFiles "Autodesk\Revit $year\Revit.exe"
    if (-not (Test-Path $revitExe)) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = 'skipped (Revit not installed)' })
        continue
    }

    $running = Get-Process -Name Revit -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -like "*\Revit $year\*" }
    if ($running) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = 'skipped (Revit is open - close it and run again)' })
        continue
    }

    $addinsDir  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $productDir = Join-Path $addinsDir $productName

    # Never write through a link: if HeronTools is a junction/symlink (a dev link), emptying it could
    # empty the link's target.
    if ((Test-Path $productDir) -and ((Get-Item -LiteralPath $productDir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = "skipped ($productDir is a link - remove it by hand)" })
        continue
    }

    try {
        New-Item -ItemType Directory -Force -Path $productDir | Out-Null

        # Replace only Heron Tools' own folder contents, so no stale file from an older build stays behind.
        Get-ChildItem -LiteralPath $productDir -Force | Remove-Item -Recurse -Force
        Get-ChildItem -LiteralPath $outDir -File | Copy-Item -Destination $productDir -Force
        Copy-Item -LiteralPath $manifest -Destination (Join-Path $addinsDir "$productName.addin") -Force
    }
    catch {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = 'FAIL (a file is in use? close Revit): ' + $_.Exception.Message })
        continue
    }

    # Prove the copy: the installed DLL must be byte-for-byte the one just built.
    $built     = (Get-FileHash $dll).Hash
    $installed = (Get-FileHash (Join-Path $productDir "$productName.dll")).Hash
    if ($built -ne $installed) {
        $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = 'FAIL (installed DLL differs from the build)' })
        continue
    }

    $results.Add([pscustomobject]@{ Revit = $year; Build = 'PASS'; Install = "PASS -> $addinsDir" })
}

Write-Host ""
Write-Host "Summary ($Configuration)" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host

if ($results | Where-Object { $_.Build -eq 'FAIL' -or $_.Install -like 'FAIL*' }) { exit 1 }
exit 0
