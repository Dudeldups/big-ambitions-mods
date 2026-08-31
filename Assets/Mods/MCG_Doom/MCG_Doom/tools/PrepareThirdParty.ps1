[CmdletBinding()]
param(
    [string]$ManagedDoomRef = "master",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ModRoot = Split-Path -Parent $PSScriptRoot
$TempRoot = Join-Path $ModRoot "obj\ThirdPartyPrepare"
$ManagedDestination = Join-Path $ModRoot "Scripts\ThirdParty\ManagedDoom"
$LicenseDestination = Join-Path $ModRoot "ThirdParty\Licenses"
$RuntimeLegalDestination = Join-Path $ModRoot "Config\Doom\Legal"
$WadDestination = Join-Path $ModRoot "Config\Doom\doom1.wad"
$RecordPath = Join-Path $ModRoot "THIRD_PARTY_PREPARED.txt"

$DoomArchiveUrl = "https://deb.debian.org/debian/pool/non-free/d/doom-wad-shareware/doom-wad-shareware_1.9.fixed.orig.tar.gz"
$DoomArchiveMd5 = "B1D0B2E814366FE926EA2773CA404137"

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Download-File([string]$Uri, [string]$Destination) {
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Destination
}

function Resolve-ManagedDoomCommit([string]$Ref) {
    $headers = @{ "User-Agent" = "MCG_Doom-ThirdParty-Prep" }
    $uri = "https://api.github.com/repos/sinshu/managed-doom/commits/$Ref"
    $result = Invoke-RestMethod -Headers $headers -Uri $uri
    return [string]$result.sha
}

if ((Test-Path -LiteralPath $WadDestination) -and
    (Get-ChildItem -LiteralPath $ManagedDestination -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue) -and
    -not $Force) {
    Write-Host "Third-party payload already exists. Use -Force to refresh it."
    exit 0
}

if (Test-Path -LiteralPath $TempRoot) {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force
}
Ensure-Directory $TempRoot
Ensure-Directory $ManagedDestination
Ensure-Directory $LicenseDestination
Ensure-Directory $RuntimeLegalDestination
Ensure-Directory (Split-Path -Parent $WadDestination)

$resolvedCommit = Resolve-ManagedDoomCommit $ManagedDoomRef
Write-Host "Managed Doom ref '$ManagedDoomRef' resolved to $resolvedCommit"

$managedZip = Join-Path $TempRoot "managed-doom.zip"
$managedExtract = Join-Path $TempRoot "managed-doom"
$managedUrl = "https://github.com/sinshu/managed-doom/archive/$resolvedCommit.zip"

Download-File $managedUrl $managedZip
Expand-Archive -LiteralPath $managedZip -DestinationPath $managedExtract -Force

$managedRepositoryRoot = Get-ChildItem -LiteralPath $managedExtract -Directory | Select-Object -First 1
if ($null -eq $managedRepositoryRoot) {
    throw "Could not locate the extracted Managed Doom repository."
}

$managedSource = Join-Path $managedRepositoryRoot.FullName "ManagedDoom\src"
if (-not (Test-Path -LiteralPath $managedSource)) {
    throw "Managed Doom source folder was not found at: $managedSource"
}

# Keep the local explanatory README, but replace all previously vendored source.
Get-ChildItem -LiteralPath $ManagedDestination -Force |
    Where-Object { $_.Name -ne "README.md" } |
    Remove-Item -Recurse -Force

Copy-Item -Path (Join-Path $managedSource "*") -Destination $ManagedDestination -Recurse -Force

# The Silk folder is the desktop frontend. Unity/MCG replaces video/input/audio.
$silkPath = Join-Path $ManagedDestination "Silk"
if (Test-Path -LiteralPath $silkPath) {
    Remove-Item -LiteralPath $silkPath -Recurse -Force
}

# Fail early if desktop-only package namespaces leaked into the remaining core.
$forbiddenNamespaces = "^\s*using\s+(Silk\.NET|TrippyGL|DrippyAL|MeltySynth)"
$forbiddenMatches = Get-ChildItem -LiteralPath $ManagedDestination -Filter "*.cs" -Recurse |
    Select-String -Pattern $forbiddenNamespaces -ErrorAction SilentlyContinue
if ($forbiddenMatches) {
    Write-Host "Unexpected desktop/external dependency references remain:" -ForegroundColor Red
    $forbiddenMatches | Select-Object -First 20 | ForEach-Object { Write-Host $_.Path ":" $_.LineNumber " " $_.Line }
    throw "Managed Doom core still references a desktop/external dependency. Review the upstream source before building."
}

$managedLicenseSource = Join-Path $managedRepositoryRoot.FullName "licenses\LICENSE_ManagedDoom.txt"
if (Test-Path -LiteralPath $managedLicenseSource) {
    Copy-Item -LiteralPath $managedLicenseSource -Destination (Join-Path $LicenseDestination "LICENSE_ManagedDoom.txt") -Force
    Copy-Item -LiteralPath $managedLicenseSource -Destination (Join-Path $RuntimeLegalDestination "LICENSE_ManagedDoom.txt") -Force
}

Copy-Item -LiteralPath (Join-Path $ModRoot "LICENSE") -Destination (Join-Path $RuntimeLegalDestination "MCG_Doom_GPL-2.0.txt") -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "DOOM_SHAREWARE_NOTICE.md") -Destination $RuntimeLegalDestination -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "THIRD_PARTY_NOTICES.md") -Destination $RuntimeLegalDestination -Force

$doomArchive = Join-Path $TempRoot "doom-wad-shareware_1.9.fixed.orig.tar.gz"
$doomExtract = Join-Path $TempRoot "doom-shareware"
Download-File $DoomArchiveUrl $doomArchive

$archiveHash = (Get-FileHash -LiteralPath $doomArchive -Algorithm MD5).Hash.ToUpperInvariant()
if ($archiveHash -ne $DoomArchiveMd5) {
    throw "Unexpected DOOM shareware archive MD5. Expected $DoomArchiveMd5, got $archiveHash."
}

Ensure-Directory $doomExtract
& tar.exe -xzf $doomArchive -C $doomExtract
if ($LASTEXITCODE -ne 0) {
    throw "tar.exe failed to extract the DOOM shareware archive."
}

$wadSource = Get-ChildItem -LiteralPath $doomExtract -Filter "doom1.wad" -File -Recurse | Select-Object -First 1
if ($null -eq $wadSource) {
    throw "doom1.wad was not found in the extracted Debian shareware archive."
}

Copy-Item -LiteralPath $wadSource.FullName -Destination $WadDestination -Force
$wadSha256 = (Get-FileHash -LiteralPath $WadDestination -Algorithm SHA256).Hash.ToUpperInvariant()
$managedZipSha256 = (Get-FileHash -LiteralPath $managedZip -Algorithm SHA256).Hash.ToUpperInvariant()

$record = @"
MCG_Doom third-party preparation
GeneratedUtc: $([DateTime]::UtcNow.ToString("o"))
ManagedDoomRequestedRef: $ManagedDoomRef
ManagedDoomResolvedCommit: $resolvedCommit
ManagedDoomArchiveSha256: $managedZipSha256
DoomSharewareArchiveUrl: $DoomArchiveUrl
DoomSharewareArchiveMd5: $archiveHash
Doom1WadSha256: $wadSha256
"@
Set-Content -LiteralPath $RecordPath -Value $record -Encoding UTF8

Remove-Item -LiteralPath $TempRoot -Recurse -Force

Write-Host ""
Write-Host "Third-party payload prepared successfully." -ForegroundColor Green
Write-Host "Managed Doom commit: $resolvedCommit"
Write-Host "DOOM1.WAD SHA256:   $wadSha256"
Write-Host "Next: build/install MCG_Doom with the normal Big Ambitions mod build."
