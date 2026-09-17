[CmdletBinding()]
param(
    [switch] $Install,
    [string] $UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe",
    [int] $MaxBuildAttempts = 8
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
if ($MaxBuildAttempts -lt 1) {
    throw "MaxBuildAttempts must be at least 1."
}

# BuildPipeline.BuildAssetBundles asks Unity for Player-mode script assemblies for
# the whole project. This worktree intentionally contains unrelated mods that can
# be stale against the current imported game DLLs. Keep their files intact, move
# compile blockers outside Assets only for the bundle build, then restore them.
$initialBlockers = @(
    "BigHax",
    "MCG_Doom"
)

$moved = New-Object System.Collections.Generic.List[object]
$movedNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

function Move-OutOfAssets {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -ieq "Volkswagen_Amarok") {
        return $false
    }
    if ($movedNames.Contains($Name)) {
        return $false
    }

    $sourceDir = Join-Path $modsRoot $Name
    $sourceMeta = $sourceDir + ".meta"
    if (-not (Test-Path -LiteralPath $sourceDir -PathType Container)) {
        return $false
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
    $null = $movedNames.Add($Name)
    Write-Host "[amarok-bundle] Temporarily isolated $Name from Assets."
    return $true
}

function Restore-IsolatedMods {
    # Restore in reverse order to mirror the isolation sequence.
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

function Get-CompileErrorModNames {
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        return @()
    }

    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath $logPath) {
        # Typical Unity compiler output:
        # Assets\Mods\SomeMod\Scripts\Foo.cs(12,3): error CS1234: ...
        $match = [regex]::Match(
            $line,
            'Assets[\\/]+Mods[\\/]+([^\\/]+)[\\/]+.*?:\s*error\s+CS\d+',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $null = $names.Add($match.Groups[1].Value)
        }
    }
    return @($names | Sort-Object)
}

function Show-RelevantLogTail {
    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        return
    }

    Write-Host "[amarok-bundle] Last relevant Unity log lines:"
    Get-Content -LiteralPath $logPath -Tail 180 |
        Select-String -Pattern "error|exception|Volkswagen|Amarok|AssetBundle|BuildPipeline|compiler" -CaseSensitive:$false |
        ForEach-Object { Write-Host $_.Line }
}

try {
    foreach ($blocker in $initialBlockers) {
        $null = Move-OutOfAssets -Name $blocker
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null

    $bundleBuilt = $false
    for ($attempt = 1; $attempt -le $MaxBuildAttempts; $attempt++) {
        if (Test-Path -LiteralPath $windowsBundle -PathType Leaf) {
            Remove-Item -LiteralPath $windowsBundle -Force
        }
        if (Test-Path -LiteralPath ($windowsBundle + ".manifest") -PathType Leaf) {
            Remove-Item -LiteralPath ($windowsBundle + ".manifest") -Force
        }
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            Remove-Item -LiteralPath $logPath -Force
        }

        Write-Host "[amarok-bundle] Starting isolated Unity batch build (attempt $attempt/$MaxBuildAttempts)..."
        Write-Host "[amarok-bundle] Log: $logPath"

        & $UnityExe `
            -batchmode `
            -quit `
            -projectPath $repoRoot `
            -executeMethod VolkswagenAmarokSetup.BuildStandaloneWindowsAssetBundle `
            -logFile $logPath

        $unityExitCode = $LASTEXITCODE
        if ((Test-Path -LiteralPath $windowsBundle -PathType Leaf) -and $unityExitCode -eq 0) {
            $bundleBuilt = $true
            break
        }

        $errorMods = @(Get-CompileErrorModNames)
        $amarokErrors = @($errorMods | Where-Object { $_ -ieq "Volkswagen_Amarok" })
        if ($amarokErrors.Count -gt 0) {
            Show-RelevantLogTail
            throw "Volkswagen_Amarok itself has Player-mode compiler errors. See $logPath"
        }

        $newBlockers = @(
            $errorMods |
                Where-Object { $_ -ne "Volkswagen_Amarok" -and -not $movedNames.Contains($_) }
        )

        if ($newBlockers.Count -eq 0) {
            Write-Host "[amarok-bundle] Unity exit code: $unityExitCode"
            Show-RelevantLogTail
            throw "Isolated Unity AssetBundle build failed and no new unrelated compiler blocker could be identified. See $logPath"
        }

        Write-Host ("[amarok-bundle] Additional unrelated compile blocker(s): " + ($newBlockers -join ", "))
        foreach ($blocker in $newBlockers) {
            $null = Move-OutOfAssets -Name $blocker
        }
    }

    if (-not $bundleBuilt) {
        Show-RelevantLogTail
        throw "Amarok AssetBundle was not created after $MaxBuildAttempts isolated build attempts. See $logPath"
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
