#requires -Version 5
<#
.SYNOPSIS
    Validates a RimSynapse mod's compatibility metadata (Core #91 / Psychology #62).

.DESCRIPTION
    About.xml carries no machine-readable version, so About/Manifest.xml is the source of truth
    that RimSynapse Core's SynapseCompatChecker reads at load. This script checks that manifest is
    well-formed and that every place the version/workshop id is repeated agrees with it:

      1. About/Manifest.xml is well-formed XML with a valid <version> and <coreVersion>.
      2. Manifest <version>    == About.xml <modVersion>.
      3. Manifest <workshopId> == About/PublishedFileId.txt.
      4. Any Source/**/Compat/*Compat.cs "…Version = \"x.y.z\"" const == Manifest <version>.

    Drift in any of these is exactly the class of bug that ships a mod claiming the wrong version
    (or requiring the wrong Core), so a mismatch is a hard failure (exit 1). Portable: it derives
    everything from -RepoRoot (default: the script's own folder), so it can be copied verbatim into
    any sibling RimSynapse mod repo.

.PARAMETER RepoRoot
    The mod repo root (the folder containing About/). Defaults to the script's location.

.EXAMPLE
    pwsh ./verify-metadata.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Test-Check {
    param([string]$Name, [bool]$Ok, [string]$Detail)
    $script:checks++
    if ($Ok) {
        Write-Host ("  [PASS] {0}" -f $Name) -ForegroundColor Green
        if ($Detail) { Write-Host ("         {0}" -f $Detail) -ForegroundColor DarkGray }
    } else {
        Write-Host ("  [FAIL] {0}" -f $Name) -ForegroundColor Red
        if ($Detail) { Write-Host ("         {0}" -f $Detail) -ForegroundColor Yellow }
        $script:failures.Add($Name)
    }
}

$VersionPattern = '^\d+(\.\d+){1,3}$'   # e.g. 0.8 / 0.8.0 / 0.8.0.0

$aboutDir      = Join-Path $RepoRoot 'About'
$manifestPath  = Join-Path $aboutDir 'Manifest.xml'
$aboutPath     = Join-Path $aboutDir 'About.xml'
$pfidPath      = Join-Path $aboutDir 'PublishedFileId.txt'

Write-Host ("RimSynapse metadata check — {0}" -f $RepoRoot) -ForegroundColor Cyan

# ── 1. Manifest.xml well-formed with valid version + coreVersion ─────────────────────────────
$manifestVersion = $null
$manifestWorkshopId = $null
if (-not (Test-Path $manifestPath)) {
    Test-Check 'Manifest.xml present' $false "expected at $manifestPath"
} else {
    $manifestXml = $null
    try { $manifestXml = [xml](Get-Content -LiteralPath $manifestPath -Raw) }
    catch { Test-Check 'Manifest.xml well-formed XML' $false $_.Exception.Message }

    if ($manifestXml) {
        Test-Check 'Manifest.xml well-formed XML' $true
        $root = $manifestXml.Manifest
        $manifestVersion    = if ($root) { ("" + $root.version).Trim() }    else { '' }
        $coreVersion        = if ($root) { ("" + $root.coreVersion).Trim() } else { '' }
        $manifestWorkshopId = if ($root) { ("" + $root.workshopId).Trim() }  else { '' }

        Test-Check 'Manifest <version> is valid' ($manifestVersion -match $VersionPattern) "version = '$manifestVersion'"
        Test-Check 'Manifest <coreVersion> is valid' ($coreVersion -match $VersionPattern) "coreVersion = '$coreVersion'"
    }
}

# ── 2. Manifest <version> == About.xml <modVersion> ──────────────────────────────────────────
if (-not (Test-Path $aboutPath)) {
    Test-Check 'About.xml present' $false "expected at $aboutPath"
} elseif ($manifestVersion) {
    $aboutXml = [xml](Get-Content -LiteralPath $aboutPath -Raw)
    $modVersion = ("" + $aboutXml.ModMetaData.modVersion).Trim()
    Test-Check 'Manifest <version> == About.xml <modVersion>' ($modVersion -eq $manifestVersion) "About '$modVersion' vs Manifest '$manifestVersion'"
}

# ── 3. Manifest <workshopId> == About/PublishedFileId.txt ────────────────────────────────────
if (Test-Path $pfidPath) {
    $pfid = (Get-Content -LiteralPath $pfidPath -Raw).Trim()
    if ($manifestWorkshopId) {
        Test-Check 'Manifest <workshopId> == PublishedFileId.txt' ($pfid -eq $manifestWorkshopId) "PublishedFileId '$pfid' vs Manifest '$manifestWorkshopId'"
    }
} else {
    Write-Host "  [skip] PublishedFileId.txt absent (not yet published) — workshopId cross-check skipped" -ForegroundColor DarkGray
}

# ── 4. Compat source const (…Version = "x.y.z") == Manifest <version> ─────────────────────────
$sourceDir = Join-Path $RepoRoot 'Source'
if ($manifestVersion -and (Test-Path $sourceDir)) {
    $compatFiles = Get-ChildItem -LiteralPath $sourceDir -Recurse -Filter '*Compat.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' }
    $found = $false
    foreach ($f in $compatFiles) {
        foreach ($m in [regex]::Matches((Get-Content -LiteralPath $f.FullName -Raw), '(\w*Version)\s*=\s*"([0-9][0-9.]*)"')) {
            $found = $true
            $constName = $m.Groups[1].Value
            $constVal  = $m.Groups[2].Value
            Test-Check "Compat const $constName == Manifest <version>" ($constVal -eq $manifestVersion) "$($f.Name): $constName = '$constVal' vs Manifest '$manifestVersion'"
        }
    }
    if (-not $found) {
        Write-Host "  [skip] no '…Version = \"x.y.z\"' const in Source/**/Compat/*Compat.cs — source cross-check skipped" -ForegroundColor DarkGray
    }
}

# ── Summary ──────────────────────────────────────────────────────────────────────────────────
Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host ("PASS — {0} checks, metadata is coherent." -f $checks) -ForegroundColor Green
    exit 0
} else {
    Write-Host ("FAIL — {0} of {1} checks failed: {2}" -f $failures.Count, $checks, ($failures -join '; ')) -ForegroundColor Red
    exit 1
}
