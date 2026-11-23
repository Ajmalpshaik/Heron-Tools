# Heron-Agent:  none
# Heron-Step:   1
# Heron-Status: DRAFT
# Heron-Since:  1.0.0
# Heron-Layer:  tool
# See docs/metadata-standard.md

<#
.SYNOPSIS
    Builds Heron Tools for every Revit release and installs it into each Revit installed on this PC.

.DESCRIPTION
    For each Revit year (2020 to 2027 by default):
      1. builds src/HeronTools/HeronTools.csproj with -p:RevitVersion=<year>
      2. proves the DLL was built for that Revit's .NET (2020 = .NET Framework 4.7.2,
         2021-2024 = 4.8, 2025-2026 = .NET 8, 2027 = .NET 10) - a DLL for the wrong .NET does not load
      3. if that Revit is installed and not running (found by Revit.exe in C:\Program Files\Autodesk\Revit <year>,
         by Windows' list of installed programs - so Revit on another drive counts - or by that year's
         Addins folder in %ProgramData%, or another add-in in that year's %AppData% Addins folder):
           - keeps a copy of the Heron Tools already installed there, so -Rollback can put it back
           - copies the build into   %AppData%\Autodesk\Revit\Addins\<year>\HeronTools\
             and the manifest into   %AppData%\Autodesk\Revit\Addins\<year>\HeronTools.addin
           - removes the "downloaded from the internet" mark, then checks every copied file byte for byte
           - if a copy fails: puts the kept install back, or on a first install takes out what it copied
    Years whose Revit is not installed are built but not installed. A year whose Revit is open is
    skipped for install (its DLL would be locked) - close Revit and run again. If a Revit is open whose
    year cannot be read, nothing is installed at all, because it could be any of them.

    Per-user install only. It needs no admin rights and works the same on every release, including
    2027 (Revit 2027 moved only the ALL-USERS folder, to Program Files). It never closes Revit.

    A year that already has the Autodesk App Store version of Heron Tools (HeronTools.bundle in an
    ApplicationPlugins folder) is built but not installed: two copies would give Revit's "duplicate add-in" message.

    Touches nothing but Heron Tools' own two items in each Addins folder, and its own backup folder
    %LocalAppData%\HeronTools\install-backup\<year>\.

    The install safeguards follow the Heron AI platform's installer (tools/deploy-addin.ps1); no code
    is shared.

.PARAMETER Rollback
    Builds nothing. For each year, puts back the Heron Tools install that the last run replaced.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1 -Years 2024,2026 -NoInstall
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build.ps1 -Years 2024 -Rollback
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Text, not numbers: "powershell -File build.ps1 -Years 2024,2026" hands the list over as ONE piece of
    # text ("2024,2026"), which an [int[]] parameter refuses. It is split into years just below.
    [string[]]$Years = @('2020', '2021', '2022', '2023', '2024', '2025', '2026', '2027'),

    [switch]$NoInstall,

    [switch]$Rollback
)

$ErrorActionPreference = 'Stop'

$yearList = @()
foreach ($part in @($Years | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })) {
    $number = 0
    if (-not [int]::TryParse($part, [ref]$number) -or $number -lt 2000 -or $number -gt 2100) {
        Write-Host "'$part' is not a Revit year. Use for example: -Years 2024,2026" -ForegroundColor Red
        exit 1
    }
    $yearList += $number
}
$yearList = [int[]]$yearList   # a typed list of years; $Years itself stays text

$repoRoot    = Split-Path -Parent $PSScriptRoot
$project     = Join-Path $repoRoot 'src\HeronTools\HeronTools.csproj'
$manifestDir = Join-Path $repoRoot 'manifests'
$productName = 'HeronTools'
$backupRoot  = Join-Path $env:LOCALAPPDATA "$productName\install-backup"

# Autodesk's own assemblies: Revit loads its copies. A copy of ours beside the add-in would clash.
$neverCopy = @('RevitAPI*.dll', 'RevitAPIUI*.dll', 'AdWindows.dll', 'UIFramework*.dll', 'RevitNET*.dll')

# ---------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------

# The .NET each Revit loads add-ins into. Same table as Directory.Build.props; a year not here is unknown.
function Get-ExpectedTargetFramework([int]$year) {
    if ($year -eq 2020)                     { return '.NETFramework,Version=v4.7.2' }
    if ($year -ge 2021 -and $year -le 2024) { return '.NETFramework,Version=v4.8' }
    if ($year -ge 2025 -and $year -le 2026) { return '.NETCoreApp,Version=v8.0' }
    if ($year -eq 2027)                     { return '.NETCoreApp,Version=v10.0' }
    return $null
}

# Reads the TargetFramework attribute the compiler wrote into the DLL - proof of which .NET it is for.
function Get-AssemblyTargetFramework([string]$dllPath) {
    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($dllPath))
    $found = [regex]::Matches($text, '\.NET(?:Framework|CoreApp),Version=v\d+(?:\.\d+)+') |
        ForEach-Object { $_.Value } | Sort-Object -Unique
    return @($found)
}

# Every running Revit, with its year read from its program folder ("Revit 2024\Revit.exe").
# Year 0 = a Revit whose year cannot be read (no access to its path, or an unusual folder name).
function Get-RunningRevit {
    $list = @()
    foreach ($p in @(Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
        $year = 0
        $path = $null
        try { $path = $p.Path } catch { }
        if ($path -and $path -match 'Revit[ _]?(\d{4})') { $year = [int]$Matches[1] }
        $list += [pscustomobject]@{ Id = $p.Id; Year = $year; Path = $path }
    }
    return $list
}

# Keeps the install about to be replaced, so -Rollback can put it back. One level: the last one replaced.
function Save-PreviousInstall([int]$year, [string]$productDir, [string]$addinFile) {
    if (-not (Test-Path (Join-Path $productDir "$productName.dll"))) { return 'nothing installed before' }
    $backup = Join-Path $backupRoot $year
    if (Test-Path $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $backup $productName) | Out-Null
    Get-ChildItem -LiteralPath $productDir -File | Copy-Item -Destination (Join-Path $backup $productName) -Force
    if (Test-Path $addinFile) { Copy-Item -LiteralPath $addinFile -Destination (Join-Path $backup "$productName.addin") -Force }
    $oldDll = Join-Path $productDir "$productName.dll"
    $info = [ordered]@{
        Year    = $year
        Version = (Get-Item -LiteralPath $oldDll).VersionInfo.ProductVersion
        Sha256  = (Get-FileHash -LiteralPath $oldDll -Algorithm SHA256).Hash
        SavedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    }
    $info | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'replaced.json') -Encoding UTF8
    return "previous $($info.Version) kept in $backup"
}

# Puts the kept install back. Returns $null when it worked, or what went wrong.
function Restore-PreviousInstall([int]$year, [string]$productDir, [string]$addinFile) {
    $backup = Join-Path $backupRoot $year
    $keptDir = Join-Path $backup $productName
    if (-not (Test-Path (Join-Path $keptDir "$productName.dll"))) { return "no kept install for $year in $backup" }
    New-Item -ItemType Directory -Force -Path $productDir | Out-Null
    Get-ChildItem -LiteralPath $productDir -Force | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath $keptDir -File | Copy-Item -Destination $productDir -Force
    $keptAddin = Join-Path $backup "$productName.addin"
    if (Test-Path $keptAddin) { Copy-Item -LiteralPath $keptAddin -Destination $addinFile -Force }
    elseif (Test-Path $addinFile) { Remove-Item -LiteralPath $addinFile -Force }   # the old install had none
    return $null
}

# Is Revit of that year on this PC? Any one of these is enough:
#   - Revit.exe in Program Files\Autodesk\Revit <year> (the usual place);
#   - Windows' list of installed programs has "Revit <year>" with Revit.exe in its folder (Revit on D:\);
#   - Revit's own all-users Addins folder for that year (%ProgramData%\Autodesk\Revit\Addins\<year>);
#   - the per-user Addins folder for that year holds another add-in (Heron Tools' own leftovers do not
#     count, or a removed Revit would keep being installed into).
# Same rule as installer\HeronTools.Installer\InstallEngine.cs.
function Test-RevitInstalled([int]$year) {
    if (Test-Path (Join-Path $env:ProgramFiles "Autodesk\Revit $year\Revit.exe")) { return $true }
    $uninstall = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    foreach ($app in @(Get-ItemProperty -Path $uninstall -ErrorAction SilentlyContinue)) {
        $display  = [string]$app.DisplayName
        $location = ([string]$app.InstallLocation).Trim().Trim('"')
        if ($location -and $display.Trim() -match "^(Autodesk\s+)?Revit\s+$year(\.\d+)*$" -and
            (Test-Path -LiteralPath (Join-Path $location 'Revit.exe'))) { return $true }
    }
    if (Test-Path (Join-Path $env:ProgramData "Autodesk\Revit\Addins\$year")) { return $true }
    $mine = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    if (Test-Path $mine) {
        $others = @(Get-ChildItem -LiteralPath $mine -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne $productName -and $_.Name -ne "$productName.addin" })
        if ($others.Count -gt 0) { return $true }
    }
    return $false
}

# A second Heron Tools in the ALL-USERS folder would load twice ("duplicate add-in" error in Revit).
function Get-AllUsersCopy([int]$year) {
    $places = @(Join-Path $env:ProgramData "Autodesk\Revit\Addins\$year\$productName.addin")
    if ($year -ge 2027) { $places += Join-Path $env:ProgramFiles "Autodesk\Revit\Addins\$year\$productName.addin" }
    return @($places | Where-Object { Test-Path $_ })
}

# The Autodesk App Store version of Heron Tools for that year (its bundle folder), or $null. It carries the same
# AddInId, so installing this one too would give Revit's "duplicate add-in" message at every start: that year is
# left out. Same rule as installer\HeronTools.Installer\InstallEngine.cs (StoreCopy); the UpgradeCode is the one
# fixed in build\build-store-package.ps1.
function Get-StoreCopy([int]$year) {
    $roots = @(
        (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'),
        (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins'),
        (Join-Path $env:ProgramFiles 'Autodesk\ApplicationPlugins'))
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($bundle in @(Get-ChildItem -LiteralPath $root -Directory -Filter '*.bundle' -ErrorAction SilentlyContinue)) {
            $package = Join-Path $bundle.FullName 'PackageContents.xml'
            $text = if (Test-Path -LiteralPath $package) { [IO.File]::ReadAllText($package) } else { '' }
            $ours = ($bundle.Name -eq "$productName.bundle") -or ($text -match '57635C9D-886C-474D-8845-A71D1D68C10C')
            if (-not $ours) { continue }
            if ($text -match ('SeriesMin="R' + $year + '"') -or (Test-Path -LiteralPath (Join-Path $bundle.FullName "Contents\$year"))) {
                return $bundle.FullName
            }
        }
    }
    return $null
}

function Test-IsLink([string]$path) {
    return (Test-Path $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)
}

$results = New-Object System.Collections.Generic.List[object]
function Add-Result($year, $build, $install, $note = '') {
    $results.Add([pscustomobject]@{ Revit = $year; Build = $build; Install = $install; Note = $note })
}

# ---------------------------------------------------------------------------------------------------
# Revit running? Read once: a Revit of unknown year blocks every install.
# ---------------------------------------------------------------------------------------------------
$runningRevit = @()
if (-not $NoInstall) {
    $runningRevit = @(Get-RunningRevit)
    $unknown = @($runningRevit | Where-Object { $_.Year -eq 0 })
    if ($unknown.Count -gt 0) {
        Write-Host ("A Revit is open whose year cannot be read (process " + (($unknown | ForEach-Object { $_.Id }) -join ', ') +
            "). Nothing will be installed or rolled back - close every Revit and run again.") -ForegroundColor Yellow
    }
}
$blockAll = @($runningRevit | Where-Object { $_.Year -eq 0 }).Count -gt 0

# ---------------------------------------------------------------------------------------------------
# -Rollback: put back the install the last run replaced. No build.
# ---------------------------------------------------------------------------------------------------
if ($Rollback) {
    foreach ($year in $yearList) {
        $addinsDir  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
        $productDir = Join-Path $addinsDir $productName
        if ($blockAll) { Add-Result $year 'not built' 'NOT RUN' 'a Revit of unknown year is open'; continue }
        if (@($runningRevit | Where-Object { $_.Year -eq $year }).Count -gt 0) { Add-Result $year 'not built' 'NOT RUN' "Revit $year is open - close it"; continue }
        if (Test-IsLink $productDir) { Add-Result $year 'not built' 'NOT RUN' "$productDir is a link - remove it by hand"; continue }
        try {
            $problem = Restore-PreviousInstall $year $productDir (Join-Path $addinsDir "$productName.addin")
            if ($problem) { Add-Result $year 'not built' 'NOT RUN' $problem }
            else { Add-Result $year 'not built' 'ROLLED BACK' (Get-Content -Raw (Join-Path (Join-Path $backupRoot $year) 'replaced.json') | ConvertFrom-Json).Version }
        }
        catch { Add-Result $year 'not built' 'FAIL' ('could not put it back: ' + $_.Exception.Message) }
    }
    Write-Host ""
    Write-Host "Rollback summary" -ForegroundColor Cyan
    $results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
    if ($results | Where-Object { $_.Install -eq 'FAIL' }) { exit 1 }
    exit 0
}

# ---------------------------------------------------------------------------------------------------
# Build (and install)
# ---------------------------------------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK (dotnet) is not on this PC's PATH. Install the .NET 10 SDK and run again."
}

# Same rule as the Heron AI platform: no source file without its five Heron metadata fields.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-metadata.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build stopped: fix the metadata listed above (see docs\metadata-standard.md)." -ForegroundColor Red
    exit 1
}

foreach ($year in $yearList) {
    Write-Host ""
    Write-Host "=== Revit $year ===" -ForegroundColor Cyan

    $expectedTfm = Get-ExpectedTargetFramework $year
    if (-not $expectedTfm) {
        Add-Result $year 'NOT RUN' 'not attempted' "Revit $year is not in the .NET table - check Autodesk's SDK, then add it to Directory.Build.props and this script"
        continue
    }

    $manifest = Join-Path $manifestDir "$productName.$year.addin"
    if (-not (Test-Path $manifest)) {
        Add-Result $year 'NOT RUN' 'not attempted' "no manifest for $year in manifests\"
        continue
    }
    # <ManifestSettings> is a Revit 2026+ element; Revit 2025 and older crash on it.
    if ($year -le 2025 -and (Select-String -LiteralPath $manifest -Pattern '<ManifestSettings' -Quiet)) {
        Add-Result $year 'FAIL' 'not attempted' "$productName.$year.addin has <ManifestSettings> - Revit $year cannot read it"
        continue
    }

    & dotnet build $project -c $Configuration "-p:RevitVersion=$year" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        Add-Result $year 'FAIL' 'not attempted' 'see the build errors above'
        continue
    }

    $outDir = Join-Path $repoRoot "src\HeronTools\bin\$Configuration\$year"
    $dll    = Join-Path $outDir "$productName.dll"
    if (-not (Test-Path $dll)) {
        Add-Result $year 'FAIL' 'not attempted' "no $productName.dll in $outDir"
        continue
    }

    # Proof the DLL is for this Revit's .NET. 2021-2024 share net48, so a wrong-year DLL can only be
    # caught by the build folder; across families this check catches it outright.
    # @( ) keeps a one-item answer a list: PowerShell would otherwise hand back a plain string, and [0]
    # of a string is its first letter.
    $actualTfm = @(Get-AssemblyTargetFramework $dll)
    if ($actualTfm.Count -ne 1 -or $actualTfm[0] -ne $expectedTfm) {
        $seen = if ($actualTfm.Count -eq 0) { 'none' } else { $actualTfm -join ' + ' }
        Add-Result $year 'FAIL' 'not attempted' "DLL is built for $seen, Revit $year needs $expectedTfm"
        continue
    }
    # .NET 8/10 add-ins carry a deps.json that tells Revit how to load them.
    if ($year -ge 2025 -and -not (Test-Path (Join-Path $outDir "$productName.deps.json"))) {
        Add-Result $year 'FAIL' 'not attempted' "no $productName.deps.json in $outDir (needed by .NET add-ins)"
        continue
    }

    if ($NoInstall) {
        Add-Result $year 'PASS' 'skipped (-NoInstall)' $expectedTfm
        continue
    }

    if (-not (Test-RevitInstalled $year)) {
        Add-Result $year 'PASS' 'skipped' 'Revit not installed on this PC'
        continue
    }
    if ($blockAll) {
        Add-Result $year 'PASS' 'NOT RUN' 'a Revit of unknown year is open - close every Revit'
        continue
    }
    if (@($runningRevit | Where-Object { $_.Year -eq $year }).Count -gt 0) {
        Add-Result $year 'PASS' 'NOT RUN' "Revit $year is open - close it and run again"
        continue
    }

    $storeCopy = Get-StoreCopy $year
    if ($storeCopy) {
        Add-Result $year 'PASS' 'NOT RUN' "the Autodesk App Store version is installed for Revit $year ($storeCopy) - two copies would give Revit's duplicate add-in message; remove it first, or keep using it"
        continue
    }

    $addinsDir  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $productDir = Join-Path $addinsDir $productName
    $addinFile  = Join-Path $addinsDir "$productName.addin"

    # Never write through a link: if HeronTools is a junction/symlink (a dev link), emptying it could
    # empty the link's target.
    if (Test-IsLink $productDir) {
        Add-Result $year 'PASS' 'NOT RUN' "$productDir is a link - remove it by hand"
        continue
    }

    # Only our own files; never an Autodesk assembly that slipped into the output.
    $toCopy = @(Get-ChildItem -LiteralPath $outDir -File | Where-Object {
        $name = $_.Name
        -not ($neverCopy | Where-Object { $name -like $_ })
    })

    $kept = ''
    try {
        $kept = Save-PreviousInstall $year $productDir $addinFile
    }
    catch {
        Add-Result $year 'PASS' 'FAIL' ('could not keep the previous install, so it was left alone: ' + $_.Exception.Message)
        continue
    }

    $copyError = $null
    $dirWasThere = Test-Path $productDir
    $addinTouched = $false
    try {
        New-Item -ItemType Directory -Force -Path $productDir | Out-Null
        # Replace only Heron Tools' own folder contents, so no stale file from an older build stays behind.
        Get-ChildItem -LiteralPath $productDir -Force | Remove-Item -Recurse -Force
        $toCopy | Copy-Item -Destination $productDir -Force
        $addinTouched = $true   # before copying: a copy that fails half way still leaves a file
        Copy-Item -LiteralPath $manifest -Destination $addinFile -Force
        # Files copied from a downloaded zip carry Windows' "from the internet" mark; .NET Framework
        # Revit refuses to load a marked DLL.
        Get-ChildItem -LiteralPath $productDir -File | Unblock-File -ErrorAction SilentlyContinue
        Unblock-File -LiteralPath $addinFile -ErrorAction SilentlyContinue

        # Prove the copy: every installed file must be byte-for-byte the one just built.
        foreach ($file in $toCopy) {
            $installed = Join-Path $productDir $file.Name
            if (-not (Test-Path $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
                throw "$($file.Name) did not copy correctly"
            }
        }
        if ((Get-FileHash -LiteralPath $addinFile).Hash -ne (Get-FileHash -LiteralPath $manifest).Hash) {
            throw "$productName.addin did not copy correctly"
        }
    }
    catch {
        $copyError = $_.Exception.Message
    }

    if ($copyError) {
        # Half a new install is worse than the old one: put the old one back.
        $restore = 'nothing to put back'
        if ($kept -like 'previous*') {
            try {
                $problem = Restore-PreviousInstall $year $productDir $addinFile
                $restore = if ($problem) { "put-back failed: $problem" } else { 'previous install put back' }
            }
            catch { $restore = 'put-back failed: ' + $_.Exception.Message }
        }
        else {
            # A first install: take out only what this run copied, so Revit does not load half of it.
            try {
                foreach ($file in $toCopy) {
                    $copied = Join-Path $productDir $file.Name
                    if (Test-Path -LiteralPath $copied) { Remove-Item -LiteralPath $copied -Force }
                }
                if ($addinTouched -and (Test-Path -LiteralPath $addinFile)) { Remove-Item -LiteralPath $addinFile -Force }
                if (-not $dirWasThere -and (Test-Path $productDir) -and
                    @(Get-ChildItem -LiteralPath $productDir -Force).Count -eq 0) { Remove-Item -LiteralPath $productDir -Force }
                $restore = 'nothing installed before; what this run copied was taken out'
            }
            catch { $restore = 'taking out what this run copied failed: ' + $_.Exception.Message + ' - run build\uninstall.ps1' }
        }
        Add-Result $year 'PASS' 'FAIL' "$copyError ($restore)"
        continue
    }

    $note = $kept
    $allUsers = @(Get-AllUsersCopy $year)
    if ($allUsers.Count -gt 0) {
        $note += "; WARNING: an all-users copy also exists ($($allUsers -join ', ')) - Revit will report a duplicate add-in; remove one"
    }
    Add-Result $year 'PASS' "PASS -> $addinsDir" $note
}

Write-Host ""
Write-Host "Summary ($Configuration)" -ForegroundColor Cyan
$results | Format-Table -AutoSize -Wrap | Out-String -Width 240 | Write-Host

# A year that could not be built, or an install that failed, is a failure. A Revit that is open or not
# installed is not: that year was built and simply not installed.
if ($results | Where-Object { $_.Build -eq 'FAIL' -or $_.Build -eq 'NOT RUN' -or $_.Install -eq 'FAIL' }) { exit 1 }
exit 0
