[CmdletBinding()]
param(
    [string]$McgDll,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ModRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $ModRoot "..\..\.."))
$ExternalBuild = Join-Path $RepoRoot "tools\external-build\BuildBigAmbitionsMods.ps1"
$GameDlls = Join-Path $RepoRoot "Assets\_BaDependencies\GameDlls"
$CompileReference = Join-Path $GameDlls "LIB_BaComputerGames.dll"

function Get-DefaultModsLocalRoot {
    $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $localLow = $local -replace "\\Local$", "\LocalLow"
    return Join-Path $localLow "Hovgaard Games\Big Ambitions\ModsLocal"
}

function Resolve-McgDll([string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $candidates = @(
        (Join-Path $RepoRoot "Library\ScriptAssemblies\LIB_BaComputerGames.dll"),
        (Join-Path (Get-DefaultModsLocalRoot) "LIB_BA_MoreComputerGames\LIB_BaComputerGames.dll")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $modsLocal = Get-DefaultModsLocalRoot
    if (Test-Path -LiteralPath $modsLocal -PathType Container) {
        $found = Get-ChildItem -LiteralPath $modsLocal -Recurse -File -Filter "LIB_BaComputerGames.dll" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $found) {
            return $found.FullName
        }
    }

    throw @"
LIB_BaComputerGames.dll was not found.
Install LIB BA More Computer Games first, or pass its DLL explicitly:
  .\tools\BuildAndInstall.ps1 -McgDll "C:\path\to\LIB_BaComputerGames.dll"
"@
}

if (-not (Test-Path -LiteralPath $ExternalBuild -PathType Leaf)) {
    throw "External build script not found: $ExternalBuild"
}
if (-not (Test-Path -LiteralPath $GameDlls -PathType Container)) {
    throw "Game DLL reference directory not found: $GameDlls"
}

$wad = Join-Path $ModRoot "Config\Doom\doom1.wad"
if (-not (Test-Path -LiteralPath $wad -PathType Leaf)) {
    throw "doom1.wad is missing. Run .\tools\PrepareThirdParty.ps1 first."
}

$resolvedMcg = Resolve-McgDll $McgDll
$mcgAssemblyName = [Reflection.AssemblyName]::GetAssemblyName($resolvedMcg).Name
if ($mcgAssemblyName -ne "LIB_BaComputerGames") {
    throw "Expected assembly LIB_BaComputerGames, got '$mcgAssemblyName' from $resolvedMcg"
}

$copiedReference = $false
$backupReference = $null
try {
    if (Test-Path -LiteralPath $CompileReference -PathType Leaf) {
        $existingHash = (Get-FileHash -LiteralPath $CompileReference -Algorithm SHA256).Hash
        $mcgHash = (Get-FileHash -LiteralPath $resolvedMcg -Algorithm SHA256).Hash
        if ($existingHash -ne $mcgHash) {
            $backupReference = "$CompileReference.mcgdoom-backup"
            Copy-Item -LiteralPath $CompileReference -Destination $backupReference -Force
            Copy-Item -LiteralPath $resolvedMcg -Destination $CompileReference -Force
            $copiedReference = $true
        }
    }
    else {
        Copy-Item -LiteralPath $resolvedMcg -Destination $CompileReference -Force
        $copiedReference = $true
    }

    Write-Host "[MCG_Doom] Compile-only MCG reference: $resolvedMcg"
    Write-Host "[MCG_Doom] Building through the normal SDK external builder..."

    $arguments = @(
        "-ModName", "MCG_Doom"
    )
    if (-not $NoInstall) {
        $arguments += "-Install"
    }

    & $ExternalBuild @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "MCG_Doom external build failed."
    }
}
finally {
    if ($copiedReference) {
        Remove-Item -LiteralPath $CompileReference -Force -ErrorAction SilentlyContinue
        if ($null -ne $backupReference -and (Test-Path -LiteralPath $backupReference -PathType Leaf)) {
            Move-Item -LiteralPath $backupReference -Destination $CompileReference -Force
        }
        Write-Host "[MCG_Doom] Removed temporary compile-only MCG reference."
    }
}
