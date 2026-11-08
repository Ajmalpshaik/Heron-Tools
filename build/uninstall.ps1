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
    skipped (its files are locked) - close Revit and run again. If a Revit is open whose year cannot be
    read, nothing is removed. It never closes Revit.

    The copies build.ps1 kept for -Rollback (%LocalAppData%\HeronTools\install-backup\) stay, unless
    -RemoveBackups is given. An all-users copy (ProgramData, or Program Files from 2027) is reported but
    not removed: that needs admin rights, and this script never asks for them.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\uninstall.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\uninstall.ps1 -DryRun
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\uninstall.ps1 -RemoveBackups
#>
[CmdletBinding()]
param(
    [switch]$DryRun,

    [switch]$RemoveBackups
)

$ErrorActionPreference = 'Stop'
$productName = 'HeronTools'
$addinsRoot  = Join-Path $env:APPDATA 'Autodesk\Revit\Addins'
$backupRoot  = Join-Path $env:LOCALAPPDATA "$productName\install-backup"

# Every running Revit's year, read from its program folder ("Revit 2024\Revit.exe"); 0 = cannot be read.
$runningYears = @()
foreach ($p in @(Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
    $path = $null
    try { $path = $p.Path } catch { }
    if ($path -and $path -match 'Revit[ _]?(\d{4})') { $runningYears += [int]$Matches[1] } else { $runningYears += 0 }
}
if ($runningYears -contains 0) {
    Write-Host "A Revit is open whose year cannot be read - nothing was removed. Close every Revit and run again." -ForegroundColor Yellow
    exit 1
}

# All-users copies: reported only (removing them needs admin rights).
$allUsers = @(Get-ChildItem -Path (Join-Path $env:ProgramData 'Autodesk\Revit\Addins\*\HeronTools.addin') -ErrorAction SilentlyContinue) +
            @(Get-ChildItem -Path (Join-Path $env:ProgramFiles 'Autodesk\Revit\Addins\*\HeronTools.addin') -ErrorAction SilentlyContinue)
foreach ($copy in $allUsers) {
    Write-Host "An all-users copy is at $($copy.FullName) - remove it by hand as an administrator if you want it gone." -ForegroundColor Yellow
}

if (-not (Test-Path $addinsRoot)) {
    Write-Host "No Revit add-ins folder at $addinsRoot - nothing to remove."
    exit 0
}

$results = New-Object System.Collections.Generic.List[object]
$failed = $false

foreach ($yearDir in Get-ChildItem -Path $addinsRoot -Directory | Where-Object { $_.Name -match '^\d{4}$' }) {
    $year     = $yearDir.Name
    $manifest = Join-Path $yearDir.FullName "$productName.addin"
    $folder   = Join-Path $yearDir.FullName $productName

    $present = @($manifest, $folder) | Where-Object { Test-Path $_ }
    if (-not $present) { continue }

    if ($runningYears -contains [int]$year) {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'skipped (Revit is open - close it and run again)' })
        continue
    }

    # A HeronTools folder that is a link (junction/symlink) is never deleted through: Windows PowerShell 5.1
    # can empty the link's TARGET. Remove such a link by hand. (Checked before -DryRun, so a dry run tells the truth.)
    $linked = $present | Where-Object { (Get-Item -LiteralPath $_ -Force).Attributes -band [IO.FileAttributes]::ReparsePoint }
    if ($linked) {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'skipped (is a link, remove by hand): ' + ($linked -join ', ') })
        continue
    }

    if ($DryRun) {
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'would remove: ' + ($present -join ', ') })
        continue
    }

    try {
        foreach ($item in $present) { Remove-Item -LiteralPath $item -Recurse -Force }
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'removed: ' + ($present -join ', ') })
    }
    catch {
        $failed = $true
        $results.Add([pscustomobject]@{ Revit = $year; Result = 'FAILED (a file is in use? close Revit): ' + $_.Exception.Message })
    }
}

if ($RemoveBackups -and (Test-Path $backupRoot)) {
    if ((Get-Item -LiteralPath $backupRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Host "$backupRoot is a link - not removed; remove it by hand." -ForegroundColor Yellow
    } elseif ($DryRun) {
        Write-Host "would remove the kept installs in $backupRoot"
    } else {
        try { Remove-Item -LiteralPath $backupRoot -Recurse -Force; Write-Host "Removed the kept installs in $backupRoot" }
        catch { $failed = $true; Write-Host "Could not remove $backupRoot : $($_.Exception.Message)" -ForegroundColor Red }
    }
}

if ($results.Count -eq 0) {
    Write-Host "Heron Tools is not installed in any Revit year - nothing to remove."
} else {
    $results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
}
if ($failed) { exit 1 }
exit 0
