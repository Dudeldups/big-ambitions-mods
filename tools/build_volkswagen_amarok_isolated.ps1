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

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity executable not found: $UnityExe"
}
if (-not (Test-Path -LiteralPath $amarokRoot -PathType Container)) {
    throw "Volkswagen Amarok mod folder not found: $amarokRoot"
}

# BuildPipeline.BuildAssetBundles triggers a Player-mode script compile for the whole
# Unity project. A worktree can contain many unrelated mods that are stale against
# the currently imported Big Ambitions DLLs. For this build we therefore isolate
# every sibling mod and leave only Volkswagen_Amarok under Assets/Mods.
$moved = New-Object System.Collections.Generic.List[object]

function Move-OutOfAssets {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -ieq $amarokModName) {
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
    # contains a compile DAG generated while those mods existed. Unity can otherwise
    # keep compiling paths such as Assets/Mods/BigHax even though the folders are gone.
    # Remove only generated script/build caches; do not touch the imported asset DB.
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
    Get-Content -LiteralPath $logPath -Tail 400 |
        Select-String -Pattern "error CS|error building|compiler error|exception|Volkswagen|Amarok|AssetBundle|BuildPipeline|aborting batchmode|Scripts have compiler errors" -CaseSensitive:$false |
        ForEach-Object { Write-Host $_.Line }
}

try {
    $siblings = @(
        Get-ChildItem -LiteralPath $modsRoot -Directory |
            Where-Object { $_.Name -ne $amarokModName } |
            Sort-Object Name
    )

    Write-Host "[amarok-bundle] Isolating $($siblings.Count) unrelated mod folder(s); only $amarokModName remains in Assets/Mods."
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

    Write-Host "[amarok-bundle] Starting isolated Unity batch build with a fresh script compile graph..."
    Write-Host "[amarok-bundle] Log: $logPath"

    # Unity.exe is a Windows GUI-subsystem executable. Invoking it with '&' from
    # Windows PowerShell can return control before the batch-mode editor process is
    # actually finished. That caused the finally block to restore all sibling mods
    # while Unity was still compiling, re-introducing BigHax/MCG_Doom mid-build.
    # Start-Process -Wait keeps the isolation active until Unity has truly exited.
    $unityArguments = @(
        "-batchmode",
        "-quit",
        "-projectPath `"$repoRoot`"",
        "-executeMethod VolkswagenAmarokSetup.BuildStandaloneWindowsAssetBundle",
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
