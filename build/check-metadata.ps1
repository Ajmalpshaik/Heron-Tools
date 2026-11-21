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
    Heron-Since, Heron-Layer), read the same way: in the file's first 41 lines, on a comment line
    (// in C#, # in PowerShell, inside the first <!-- --> comment in XAML). Each value must be filled in,
    Heron-Agent must be 'none', and Heron-Status and Heron-Layer must be one of the allowed values,
    spelled exactly as docs/metadata-standard.md lists them.
    Checks every .cs, .ps1 and .xaml file under src\, build\ and installer\ (skipping bin, obj and .vs).
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

$files = foreach ($dir in 'src', 'build', 'installer') {
    $full = Join-Path $repoRoot $dir
    if (Test-Path $full) {
        Get-ChildItem -Path $full -Recurse -File -Include *.cs, *.ps1, *.xaml |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs)[\\/]' }
    }
}

$problems = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $head = @(Get-Content -LiteralPath $file.FullName -TotalCount 41)
    $rel  = $file.FullName.Substring($repoRoot.Length + 1)
    $ext  = $file.Extension.ToLowerInvariant()

    # Only comment lines count (docs/metadata-standard.md): // in C#, # in PowerShell, and in XAML the
    # lines inside the file's first <!-- --> comment. A field in code or a string is not a header.
    $comment = New-Object System.Collections.Generic.List[string]
    if ($ext -eq '.xaml') {
        $inside = $false
        foreach ($l in $head) {
            if (-not $inside) {
                $at = $l.IndexOf('<!--')
                if ($at -lt 0) { continue }
                $inside = $true
                $l = $l.Substring($at + 4)
            }
            $end = $l.IndexOf('-->')
            if ($end -ge 0) { $comment.Add($l.Substring(0, $end)); break }
            $comment.Add($l)
        }
    }
    else {
        $marker = if ($ext -eq '.cs') { '//' } else { '#' }
        foreach ($l in $head) {
            $m = [regex]::Match($l, '^\s*' + [regex]::Escape($marker) + '(.*)$')
            if ($m.Success) { $comment.Add($m.Groups[1].Value) }
        }
    }

    foreach ($field in $fields) {
        $match = $null
        foreach ($l in $comment) {
            $m = [regex]::Match($l, '^\s*' + [regex]::Escape($field) + '\s*:(.*)$')
            if ($m.Success) { $match = $m; break }
        }
        if (-not $match) { $problems.Add("$rel - missing $field (it must be on a comment line in the first 41 lines)"); continue }

        $value = $match.Groups[1].Value.Trim()
        if ($value -eq '') { $problems.Add("$rel - $field has no value"); continue }

        if ($field -eq 'Heron-Agent' -and $value -cne 'none') {
            $problems.Add("$rel - Heron-Agent '$value' must be 'none' (Heron Tools implements no Heron AI agent)")
        }
        if ($field -eq 'Heron-Status' -and $statuses -cnotcontains $value) {
            $problems.Add("$rel - Heron-Status '$value' is not one of: $($statuses -join ', ')")
        }
        if ($field -eq 'Heron-Layer' -and $layers -cnotcontains $value) {
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
