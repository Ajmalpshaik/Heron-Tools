# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Checks that every Heron Tools source file carries the five Heron metadata fields.

.DESCRIPTION
    The same five fields the Heron AI platform uses (Heron-Agent, Heron-Step, Heron-Status,
    Heron-Since, Heron-Layer), read the same way: in the file's first 41 lines, on a comment line.
    Checks every .cs, .ps1 and .xaml file under src\ and build\ (skipping bin, obj and .vs).
    Exits 1 and names each file and missing field when anything is wrong. build.ps1 runs it first,
    so a file without its header stops the build.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\check-metadata.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$fields   = 'Heron-Agent', 'Heron-Step', 'Heron-Status', 'Heron-Since', 'Heron-Layer'
$statuses = 'DISCOVERED', 'DRAFT', 'TESTING', 'VALIDATED', 'SHADOW', 'PROVEN', 'PRODUCTION', 'DEPRECATED', 'ARCHIVED'
$layers   = 'bridge', 'revit', 'brain', 'platform', 'test', 'tool'

$files = foreach ($dir in 'src', 'build') {
    $full = Join-Path $repoRoot $dir
    if (Test-Path $full) {
        Get-ChildItem -Path $full -Recurse -File -Include *.cs, *.ps1, *.xaml |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' }
    }
}

$problems = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $head = Get-Content -LiteralPath $file.FullName -TotalCount 41
    $rel  = $file.FullName.Substring($repoRoot.Length + 1)

    foreach ($field in $fields) {
        $line = $head | Where-Object { $_ -match "^\s*(//|#)?\s*$field\s*:\s*(.+)$" } | Select-Object -First 1
        if (-not $line) { $problems.Add("$rel - missing $field"); continue }

        $value = ([regex]::Match($line, "$field\s*:\s*(.+)$")).Groups[1].Value.Trim()
        if ($field -eq 'Heron-Status' -and $statuses -notcontains $value.ToUpperInvariant()) {
            $problems.Add("$rel - Heron-Status '$value' is not one of: $($statuses -join ', ')")
        }
        if ($field -eq 'Heron-Layer' -and $layers -notcontains $value.ToLowerInvariant()) {
            $problems.Add("$rel - Heron-Layer '$value' is not one of: $($layers -join ', ')")
        }
    }
}

if ($problems.Count -gt 0) {
    Write-Host "Heron metadata check: FAIL ($($problems.Count) problem(s))" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "Heron metadata check: PASS ($(@($files).Count) files)" -ForegroundColor Green
exit 0
