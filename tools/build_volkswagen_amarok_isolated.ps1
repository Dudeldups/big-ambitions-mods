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
$amarokRoot = Join-Path $modsRoot $amarokModName
$windowsBundle = Join-Path $amarokRoot "AssetBundles\Windows\volkswagenamarok.unity3d"
$flatBundle = Join-Path $amarokRoot "AssetBundles\volkswagenamarok.unity3d"
$logPath = Join-Path $repoRoot "Temp\VolkswagenAmarokIsolatedBuild.log"
$holdRoot = Join-Path $repoRoot ("Temp\VolkswagenAmarokBundleIsolation\" + [Guid]::NewGuid().ToString("N"))
$feedbackPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_ingame_feedback.py"
$lightingHelperPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_lighting_source_helper.py"
$existingPrefabBuildPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_existing_prefab_feedback_build.py"
$thirdFeedbackPrepPatch = Join-Path $repoRoot "tools\prepare_volkswagen_amarok_third_feedback.py"
$thirdFeedbackPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_third_ingame_feedback.py"
$npcStancePatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_npc_stance_correction.py"
$fourthFeedbackPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_fourth_ingame_feedback.py"
$fourthSafetyPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_fourth_safety.py"
$fifthFeedbackPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_fifth_ingame_feedback.py"
$sixthFeedbackPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_sixth_ingame_feedback.py"
$seventhMaterialsPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_seventh_materials.py"
$seventhTrafficServicesPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_seventh_traffic_services.py"
$seventhPerformancePatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_seventh_performance.py"
$sideIndicatorPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_side_indicators.py"
$eighthBlackGeometryPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_eighth_black_geometry.py"
$eighthPerformancePatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_eighth_performance.py"
$originalBluePaintPatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_original_blue_paint.py"
$damagePerformancePatch = Join-Path $repoRoot "tools\patch_volkswagen_amarok_damage_performance.py"
$refreshLightOverlay = Join-Path $repoRoot "tools\refresh_volkswagen_amarok_light_overlays.py"

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity executable not found: $UnityExe"
}
if (-not (Test-Path -LiteralPath $amarokRoot -PathType Container)) {
    throw "Volkswagen Amarok mod folder not found: $amarokRoot"
}
foreach ($patch in @($feedbackPatch, $lightingHelperPatch, $existingPrefabBuildPatch, $thirdFeedbackPrepPatch, $thirdFeedbackPatch, $npcStancePatch, $fourthFeedbackPatch, $fourthSafetyPatch, $fifthFeedbackPatch, $sixthFeedbackPatch, $seventhMaterialsPatch, $seventhTrafficServicesPatch, $seventhPerformancePatch, $sideIndicatorPatch, $eighthBlackGeometryPatch, $eighthPerformancePatch, $originalBluePaintPatch, $damagePerformancePatch, $refreshLightOverlay)) {
    if (-not (Test-Path -LiteralPath $patch -PathType Leaf)) {
        throw "Volkswagen Amarok source patch not found: $patch"
    }
}

Write-Host "[amarok-bundle] Refreshing authored Amarok light overlays when the .blend changed..."
& python $refreshLightOverlay
if ($LASTEXITCODE -ne 0) {
    throw "Amarok authored-light refresh failed with exit code $LASTEXITCODE."
}

Write-Host "[amarok-bundle] Applying current Amarok in-game tuning patches..."
& python $feedbackPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok in-game feedback patch failed with exit code $LASTEXITCODE."
}
& python $lightingHelperPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok lighting helper patch failed with exit code $LASTEXITCODE."
}
& python $existingPrefabBuildPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok existing-prefab build patch failed with exit code $LASTEXITCODE."
}
& python $thirdFeedbackPrepPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok third-feedback preparation failed with exit code $LASTEXITCODE."
}
& python $thirdFeedbackPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok third in-game feedback patch failed with exit code $LASTEXITCODE."
}
& python $npcStancePatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok NPC stance correction failed with exit code $LASTEXITCODE."
}
& python $fourthFeedbackPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok fourth in-game feedback patch failed with exit code $LASTEXITCODE."
}
& python $fourthSafetyPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok fourth-feedback safety patch failed with exit code $LASTEXITCODE."
}
& python $fifthFeedbackPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok fifth in-game feedback patch failed with exit code $LASTEXITCODE."
}
& python $sixthFeedbackPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok sixth in-game feedback patch failed with exit code $LASTEXITCODE."
}
& python $seventhMaterialsPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok seventh materials patch failed with exit code $LASTEXITCODE."
}
& python $seventhTrafficServicesPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok seventh traffic/services patch failed with exit code $LASTEXITCODE."
}
& python $seventhPerformancePatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok seventh performance patch failed with exit code $LASTEXITCODE."
}
& python $sideIndicatorPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok side-indicator lighting patch failed with exit code $LASTEXITCODE."
}
& python $eighthBlackGeometryPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok eighth black-geometry patch failed with exit code $LASTEXITCODE."
}
& python $eighthPerformancePatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok eighth performance patch failed with exit code $LASTEXITCODE."
}
& python $originalBluePaintPatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok original-blue paint patch failed with exit code $LASTEXITCODE."
}
& python $damagePerformancePatch
if ($LASTEXITCODE -ne 0) {
    throw "Amarok damage-performance patch failed with exit code $LASTEXITCODE."
}

# The current feedback build no longer calls Generate() and therefore no longer
# needs AudiRS6R at all. BuildPipeline still compiles every script assembly under
# Assets, so keep ONLY Volkswagen_Amarok present while Unity is running. This is
# the same isolation model that produced the previously successful Amarok bundle.
$moved = New-Object System.Collections.Generic.List[object]

function Is-KeptMod {
    param([string] $Name)
    return $Name -ieq $amarokModName
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

    Write-Host "[amarok-bundle] Relevant Unity failure details:"
    Get-Content -LiteralPath $logPath -Tail 1200 |
        Select-String -Pattern "error CS|Script Compilation Error|executeMethod|InvalidOperationException|ArgumentException|NullReferenceException|error building|compiler error|exception|failed|missing|Could not|Volkswagen|Amarok|AssetBundle|BuildPipeline|aborting batchmode|Scripts have compiler errors|overlay sources" -CaseSensitive:$false |
        ForEach-Object { Write-Host $_.Line }
}

try {
    $siblings = @(
        Get-ChildItem -LiteralPath $modsRoot -Directory |
            Where-Object { -not (Is-KeptMod -Name $_.Name) } |
            Sort-Object Name
    )

    Write-Host "[amarok-bundle] Isolating $($siblings.Count) non-Amarok mod folder(s); only $amarokModName remains in Assets/Mods."
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

    Write-Host "[amarok-bundle] Patching existing Amarok prefab and building Windows AssetBundle in isolated Unity..."
    Write-Host "[amarok-bundle] Log: $logPath"

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

    if ($unityExitCode -ne 0) {
        Show-RelevantLogTail
        throw "Unity Amarok prefab-patch/build executeMethod failed with exit code $unityExitCode. See $logPath"
    }

    if (-not (Test-Path -LiteralPath $windowsBundle -PathType Leaf)) {
        Show-RelevantLogTail
        throw "Unity did not create the Amarok AssetBundle. See $logPath"
    }

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
