[CmdletBinding()]
param(
    [switch] $Install,
    [string] $UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$modsRoot = Join-Path $repoRoot "Assets\Mods"
$amarokModName = "Volkswagen_Amarok"
$donorModName = "AudiRS6R"
$amarokRoot = Join-Path $modsRoot $amarokModName
$windowsBundle = Join-Path $amarokRoot "AssetBundles\Windows\volkswagenamarok.unity3d"
$flatBundle = Join-Path $amarokRoot "AssetBundles\volkswagenamarok.unity3d"
$logPath = Join-Path $repoRoot "Temp\VolkswagenAmarokIsolatedBuild.log"
$holdRoot = Join-Path $repoRoot ("Temp\VolkswagenAmarokBundleIsolation\" + [Guid]::NewGuid().ToString("N"))
$feedbackPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_ingame_feedback.py"

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity executable not found: $UnityExe"
}
if (-not (Test-Path -LiteralPath $amarokRoot -PathType Container)) {
    throw "Volkswagen Amarok mod folder not found: $amarokRoot"
}
if (-not (Test-Path -LiteralPath $feedbackPatch -PathType Leaf)) {
    throw "Volkswagen Amarok in-game feedback patch not found: $feedbackPatch"
}

Write-Host "[amarok-bundle] Applying current Amarok in-game tuning patch..."
& python $feedbackPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok in-game feedback patch failed with exit code $LASTEXITCODE."
}

# BuildPipeline.BuildAssetBundles triggers a Player-mode script compile for the whole
# Unity project. A worktree can contain many unrelated mods that are stale against
# the currently imported Big Ambitions DLLs. Keep only the Amarok plus AudiRS6R,
# which the deterministic Amarok setup uses as its generic donor prefab/VehicleType.
$moved = New-Object System.Collections.Generic.List[object]

function Is-KeptMod {
    param([string] $Name)
    return $Name -ieq $amarokModName -or $Name -ieq $donorModName
}

function Move-OutOfAssets {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or (Is-KeptMod -Name $Name)) {
        return
    }

    $sourceDir = Join-Path $modsRoot $Name
    if (-not (Test-Path -LiteralPath $sourceDir -PathType Container)) {
        return
    }

    New-Item -ItemType Directory -Path $holdRoot -Force | Out-Null
    $targetDir = Join-Path $holdRoot $Name
    $sourceMeta = $sourceDir + ".meta"
    $targetMeta = $null

    Move-Item -LiteralPath $sourceDir -Destination $targetDir
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
    for ($index = $moved.Count - 1; $index -ge 0; $index--) {
        $entry = $moved[$index]

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

function Clear-StaleUnityCompileCaches {
    # Moving source folders outside Assets is not sufficient when Library/Bee still
    # contains a compile DAG generated while those mods existed. Remove only
    # generated script/build caches; do not touch the imported asset database.
    $cachePaths = @(
        (Join-Path $repoRoot "Library\Bee"),
        (Join-Path $repoRoot "Library\ScriptAssemblies"),
        (Join-Path $repoRoot "Library\PlayerScriptAssemblies"),
        (Join-Path $repoRoot "Library\BuildPlayerData")
    )

    foreach ($cachePath in $cachePaths) {
        if (Test-Path -LiteralPath $cachePath) {
            Write-Host "[amarok-bundle] Clearing stale Unity compile cache: $cachePath"
            Remove-Item -LiteralPath $cachePath -Recurse -Force
        }
    }
}

function Show-RelevantLogTail {
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        return
    }

    Write-Host "[amarok-bundle] Last relevant Unity log lines:"
    Get-Content -LiteralPath $logPath -Tail 500 |
        Select-String -Pattern "error CS|error building|compiler error|exception|Volkswagen|Amarok|AssetBundle|BuildPipeline|aborting batchmode|Scripts have compiler errors|overlay sources" -CaseSensitive:$false |
        ForEach-Object { Write-Host $_.Line }
}

try {
    $siblings = @(
        Get-ChildItem -LiteralPath $modsRoot -Directory |
            Where-Object { -not (Is-KeptMod -Name $_.Name) } |
            Sort-Object Name
    )

    Write-Host "[amarok-bundle] Isolating $($siblings.Count) unrelated mod folder(s); keeping $amarokModName + $donorModName."
    foreach ($sibling in $siblings) {
        Move-OutOfAssets -Name $sibling.Name
    }

    Clear-StaleUnityCompileCaches

    New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        Remove-Item -LiteralPath $logPath -Force
    }
    if (Test-Path -LiteralPath $windowsBundle -PathType Leaf) {
        Remove-Item -LiteralPath $windowsBundle -Force
    }
    if (Test-Path -LiteralPath ($windowsBundle + ".manifest") -PathType Leaf) {
        Remove-Item -LiteralPath ($windowsBundle + ".manifest") -Force
    }

    Write-Host "[amarok-bundle] Regenerating Amarok prefab and building Windows AssetBundle in isolated Unity..."
    Write-Host "[amarok-bundle] Log: $logPath"

    # Unity.exe is a Windows GUI-subsystem executable. Start-Process -Wait keeps
    # all unrelated mods isolated until the batch-mode editor has truly exited.
    $unityArguments = @(
        "-batchmode",
        "-quit",
        "-projectPath `"$repoRoot`"",
        "-executeMethod VolkswagenAmarokSetup.RegenerateAndBuildStandaloneWindowsAssetBundle",
        "-logFile `"$logPath`""
    ) -join " "

    $unityProcess = Start-Process `
        -FilePath $UnityExe `
        -ArgumentList $unityArguments `
        -PassThru `
        -Wait `
        -NoNewWindow

    $unityExitCode = $unityProcess.ExitCode
    Write-Host "[amarok-bundle] Unity batch process finished with exit code $unityExitCode."

    if (-not (Test-Path -LiteralPath $windowsBundle -PathType Leaf)) {
        Show-RelevantLogTail
        throw "Unity did not create the Amarok AssetBundle. See $logPath"
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
    & $externalBuild -ModName $amarokModName -Install
    if ($LASTEXITCODE -ne 0) {
        throw "External Amarok DLL/install step failed with exit code $LASTEXITCODE."
    }
}

Write-Host "[amarok-bundle] Done."
