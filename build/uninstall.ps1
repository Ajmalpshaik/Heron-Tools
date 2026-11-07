# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Removes Heron Tools from every Revit year on this PC - and nothing else.

.DESCRIPTION
    In each %AppData%\Autodesk\Revit\Addins\<year>\ folder it removes exactly two items, if present:
        HeronTools.addin
        HeronTools\        (the folder with HeronTools.dll)
    Heron AI, AJ-Tools, pyRevit and every other add-in are not touched. A year whose Revit is open is
    skipped (its files are locked) - close Revit and run again.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\uninstall.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\uninstall.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$productName = 'HeronTools'
$addinsRoot  = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'

if (-not (Test-Path $addinsRoot)) {
    Write-Host "No Revit add-ins folder at $addinsRoot - nothing to remove."
    exit 0
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($yearDir in Get-ChildItem -Path $addinsRoot -Directory | Where-Object { $_.Name -match '^\d{4}$' }) {
    $year     = $yearDir.Name
    $manifest = Join-Path $yearDir.FullName "$productName.addin"
    $folder   = Join-Path $yearDir.FullName $productName

    $present = @($manifest, $folder) | Where-Object { Test-Path $_ }
    if (-not $present) { continue }

    $running = Get-Process -Name Revit -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -like "*\Revit $year\*" }
    if ($running) {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'skipped (Revit is open - close it and run again)' })
        continue
    }

    if ($DryRun) {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'would remove: ' + ($present -join ', ') })
        continue
    }

    # A HeronTools folder that is a link (junction/symlink) is never deleted through: Windows PowerShell 5.1
    # can empty the link's TARGET. Remove such a link by hand.
    $linked = $present | Where-Object { (Get-Item -LiteralPath $_ -Force).Attributes -band [IO.FileAttributes]::ReparsePoint }
    if ($linked) {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'skipped (is a link, remove by hand): ' + ($linked -join ', ') })
        continue
    }

    try {
        foreach ($item in $present) { Remove-Item -LiteralPath $item -Recurse -Force }
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'removed: ' + ($present -join ', ') })
    }
    catch {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'FAILED (a file is in use? close Revit): ' + $_.Exception.Message })
    }
}

if ($results.Count -eq 0) {
    Write-Host "Heron Tools is not installed in any Revit year - nothing to remove."
} else {
    $results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
}
exit 0
