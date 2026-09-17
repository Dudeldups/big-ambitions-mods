[CmdletBinding()]
param(
    [switch] $Install,
    [string] $UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$modsRoot = Join-Path $repoRoot "Assets\Mods"
$amarokRoot = Join-Path $modsRoot "Volkswagen_Amarok"
$windowsBundle = Join-Path $amarokRoot "AssetBundles\Windows\volkswagenamarok.unity3d"
$flatBundle = Join-Path $amarokRoot "AssetBundles\volkswagenamarok.unity3d"
$logPath = Join-Path $repoRoot "Temp\VolkswagenAmarokIsolatedBuild.log"
$holdRoot = Join-Path $repoRoot ("Temp\VolkswagenAmarokBundleIsolation\" + [Guid]::NewGuid().ToString("N"))

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity executable not found: $UnityExe"
}
if (-not (Test-Path -LiteralPath $amarokRoot -PathType Container)) {
    throw "Volkswagen Amarok mod folder not found: $amarokRoot"
}

# BuildPipeline.BuildAssetBundles asks Unity for Player-mode script assemblies.
# The current shared SDK worktree contains unrelated mods that do not compile
# against the current game DLLs. Keep their files intact, but move the known
# blockers outside Assets while the batch-mode bundle build runs.
$blockers = @(
    "BigHax",
    "MCG_Doom"
)

$moved = New-Object System.Collections.Generic.List[object]

function Move-OutOfAssets {
    param([string] $Name)

    $sourceDir = Join-Path $modsRoot $Name
    $sourceMeta = $sourceDir + ".meta"
    if (-not (Test-Path -LiteralPath $sourceDir -PathType Container)) {
        return
    }

    New-Item -ItemType Directory -Path $holdRoot -Force | Out-Null
    $targetDir = Join-Path $holdRoot $Name
    Move-Item -LiteralPath $sourceDir -Destination $targetDir
    $targetMeta = $null
    if (Test-Path -LiteralPath $sourceMeta -PathType Leaf) {
        $targetMeta = Join-Path $holdRoot ($Name + ".meta")
        Move-Item -LiteralPath $sourceMeta -Destination $targetMeta
    }

    $moved.Add([pscustomobject]@{
        Name = $Name
        SourceDir = $sourceDir
        TargetDir = $targetDir
        SourceMeta = $sourceMeta
        TargetMeta = $targetMeta
    }) | Out-Null
    Write-Host "[amarok-bundle] Temporarily isolated $Name from Assets."
}

function Restore-IsolatedMods {
    foreach ($entry in $moved) {
        if (Test-Path -LiteralPath $entry.TargetDir -PathType Container) {
            Move-Item -LiteralPath $entry.TargetDir -Destination $entry.SourceDir
        }
        if ($null -ne $entry.TargetMeta -and (Test-Path -LiteralPath $entry.TargetMeta -PathType Leaf)) {
            Move-Item -LiteralPath $entry.TargetMeta -Destination $entry.SourceMeta
        }
        Write-Host "[amarok-bundle] Restored $($entry.Name)."
    }

    if (Test-Path -LiteralPath $holdRoot -PathType Container) {
        Remove-Item -LiteralPath $holdRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    foreach ($blocker in $blockers) {
        Move-OutOfAssets -Name $blocker
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Remove-Item -LiteralPath $logPath -Force
    }

    Write-Host "[amarok-bundle] Starting isolated Unity batch build..."
    Write-Host "[amarok-bundle] Log: $logPath"

    & $UnityExe `
        -batchmode `
        -quit `
        -projectPath $repoRoot `
        -executeMethod VolkswagenAmarokSetup.BuildStandaloneWindowsAssetBundle `
        -logFile $logPath

    $unityExitCode = $LASTEXITCODE
    if ($unityExitCode -ne 0) {
        Write-Host "[amarok-bundle] Unity exited with code $unityExitCode. Last relevant log lines:"
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            Get-Content -LiteralPath $logPath -Tail 120 |
                Select-String -Pattern "error|exception|Volkswagen|Amarok|AssetBundle|BuildPipeline" -CaseSensitive:$false |
                ForEach-Object { Write-Host $_.Line }
        }
        throw "Isolated Unity AssetBundle build failed. See $logPath"
    }

    if (-not (Test-Path -LiteralPath $windowsBundle -PathType Leaf)) {
        throw "Unity completed, but the Amarok bundle was not created: $windowsBundle"
    }

    # Keep the SDK-style platform output, but also keep a flat copy because the
    # current Big Ambitions ModsLocal loader resolves the declared relative key
    # AssetBundles/volkswagenamarok.unity3d directly.
    New-Item -ItemType Directory -Path (Split-Path -Parent $flatBundle) -Force | Out-Null
    Copy-Item -LiteralPath $windowsBundle -Destination $flatBundle -Force
    if (Test-Path -LiteralPath ($windowsBundle + ".manifest") -PathType Leaf) {
        Copy-Item -LiteralPath ($windowsBundle + ".manifest") -Destination ($flatBundle + ".manifest") -Force
    }

    $bundleInfo = Get-Item -LiteralPath $windowsBundle
    Write-Host "[amarok-bundle] Built: $($bundleInfo.FullName) ($($bundleInfo.Length) bytes)"
    Write-Host "[amarok-bundle] Flat runtime copy: $flatBundle"
}
finally {
    Restore-IsolatedMods
}

if ($Install) {
    $externalBuild = Join-Path $repoRoot "tools\external-build\BuildBigAmbitionsMods.ps1"
    if (-not (Test-Path -LiteralPath $externalBuild -PathType Leaf)) {
        throw "External build script not found: $externalBuild"
    }

    Write-Host "[amarok-bundle] Building DLL and installing Volkswagen_Amarok to ModsLocal..."
    & $externalBuild -ModName "Volkswagen_Amarok" -Install
    if ($LASTEXITCODE -ne 0) {
        throw "External Amarok DLL/install step failed with exit code $LASTEXITCODE."
    }
}

Write-Host "[amarok-bundle] Done."
