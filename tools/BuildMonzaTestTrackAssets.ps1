param(
    [string] $SourceGlb,
    [string] $UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ModsRoot = Join-Path $RepoRoot "Assets\Mods"
$ModelTarget = Join-Path $RepoRoot "Assets\Mods\Monza_Test_Track\Models\monza_circuit_1998_layout.glb"
$BundleTarget = Join-Path $RepoRoot "Assets\Mods\Monza_Test_Track\AssetBundles\Windows\monzatesttrack.unity3d"
$LogPath = Join-Path $RepoRoot "Logs\MonzaTestTrackAssetBuild.log"
$IsolationRoot = Join-Path $RepoRoot "Library\MonzaTestTrackAssetBuildIsolation"
$IsolationModsRoot = Join-Path $IsolationRoot "Mods"
$MonzaFolderName = "Monza_Test_Track"

function Show-MonzaBuildDiagnostics {
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        Write-Host "[monza-assets] Unity log was not created: $LogPath"
        return
    }

    Write-Host ""
    Write-Host "===== MONZA UNITY BUILD DIAGNOSTICS ====="

    $patterns = @(
        "error CS",
        "Exception:",
        "Exception ",
        "InvalidOperationException",
        "NullReferenceException",
        "ArgumentException",
        "FileNotFoundException",
        "MonzaTestTrack",
        "MONZA_",
        "did not import",
        "Could not",
        "AssetBundle build failed",
        "Scripts have compiler errors",
        "executeMethod"
    )

    $matches = Select-String -LiteralPath $LogPath -Pattern $patterns -SimpleMatch -Context 3,8

    if ($matches) {
        $matches |
            ForEach-Object { $_.ToString() } |
            Select-Object -Unique |
            ForEach-Object { Write-Host $_ }
    }
    else {
        Write-Host "[monza-assets] No targeted diagnostic lines found; showing the last 80 log lines."
        Get-Content -LiteralPath $LogPath -Tail 80
    }
}

function Restore-IsolatedMods {
    if (-not (Test-Path -LiteralPath $IsolationModsRoot -PathType Container)) {
        return
    }

    Write-Host "[monza-assets] Restoring temporarily isolated mod folders..."

    foreach ($item in Get-ChildItem -LiteralPath $IsolationModsRoot -Force) {
        $target = Join-Path $ModsRoot $item.Name
        if (Test-Path -LiteralPath $target) {
            throw "Cannot restore isolated item because the target already exists: $target"
        }

        Move-Item -LiteralPath $item.FullName -Destination $target
    }

    if (Test-Path -LiteralPath $IsolationRoot) {
        Remove-Item -LiteralPath $IsolationRoot -Recurse -Force
    }
}

function Enter-MonzaIsolation {
    # Recover automatically from a previously interrupted asset build.
    Restore-IsolatedMods

    New-Item -ItemType Directory -Path $IsolationModsRoot -Force | Out-Null

    $movedFolders = 0
    foreach ($directory in Get-ChildItem -LiteralPath $ModsRoot -Directory) {
        if ($directory.Name -eq $MonzaFolderName) {
            continue
        }

        $metaPath = $directory.FullName + ".meta"

        Move-Item -LiteralPath $directory.FullName -Destination (Join-Path $IsolationModsRoot $directory.Name)

        if (Test-Path -LiteralPath $metaPath -PathType Leaf) {
            Move-Item -LiteralPath $metaPath -Destination (Join-Path $IsolationModsRoot ($directory.Name + ".meta"))
        }

        $movedFolders++
    }

    Write-Host "[monza-assets] Isolated $movedFolders unrelated mod folder(s) from Unity compilation."
}

# If a previous PowerShell/Unity run was interrupted after isolation, restore first.
Restore-IsolatedMods

if (-not $SourceGlb) {
    $downloadCandidates = @(
        "E:\Downloads",
        (Join-Path $env:USERPROFILE "Downloads")
    ) | Select-Object -Unique

    foreach ($downloads in $downloadCandidates) {
        if (-not (Test-Path -LiteralPath $downloads -PathType Container)) {
            continue
        }

        $candidate = Get-ChildItem -LiteralPath $downloads -File -Filter "monza_circuit_1998_layout*.glb" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if ($candidate) {
            $SourceGlb = $candidate.FullName
            break
        }
    }
}

if (-not $SourceGlb -or -not (Test-Path -LiteralPath $SourceGlb -PathType Leaf)) {
    throw @"
Monza source GLB was not found.
Put the downloaded file in E:\Downloads with a name like
  monza_circuit_1998_layout.glb
or call this script with:
  -SourceGlb "C:\path\to\monza_circuit_1998_layout.glb"
"@
}

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
    throw "Unity executable not found: $UnityExe"
}

$targetDir = Split-Path -Parent $ModelTarget
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force | Out-Null

Write-Host "[monza-assets] Source GLB: $SourceGlb"
Write-Host "[monza-assets] Project: $RepoRoot"
Write-Host "[monza-assets] Copying source model to: $ModelTarget"
Copy-Item -LiteralPath $SourceGlb -Destination $ModelTarget -Force

# Never accept an old bundle after a failed Unity run.
if (Test-Path -LiteralPath $BundleTarget -PathType Leaf) {
    Remove-Item -LiteralPath $BundleTarget -Force
}
if (Test-Path -LiteralPath ($BundleTarget + ".manifest") -PathType Leaf) {
    Remove-Item -LiteralPath ($BundleTarget + ".manifest") -Force
}

$unityExitCode = $null

try {
    Enter-MonzaIsolation

    Write-Host "[monza-assets] Starting isolated Unity batch AssetBundle build..."
    Write-Host "[monza-assets] Only Monza_Test_Track remains under Assets\Mods during this Unity run."
    Write-Host "[monza-assets] Log: $LogPath"

    $unityArgs = @(
        "-batchmode",
        "-quit",
        "-projectPath", ('"' + $RepoRoot + '"'),
        "-executeMethod", "MonzaTestTrack.Editor.MonzaTestTrackAssetBuilder.BuildBatch",
        "-logFile", ('"' + $LogPath + '"')
    )

    # Unity.exe is a GUI executable. Start-Process -Wait is required so that
    # PowerShell does not inspect the bundle before Unity has actually finished.
    $unityProcess = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -Wait -PassThru
    $unityExitCode = $unityProcess.ExitCode
}
finally {
    # Always restore every other mod, even when Unity compilation/import/build fails.
    Restore-IsolatedMods
}

if ($unityExitCode -ne 0) {
    Show-MonzaBuildDiagnostics
    throw "Unity Monza AssetBundle build failed with exit code $unityExitCode."
}

if (-not (Test-Path -LiteralPath $BundleTarget -PathType Leaf)) {
    Show-MonzaBuildDiagnostics
    throw "Unity exited with code 0 but the expected bundle was not created: $BundleTarget"
}

$bundle = Get-Item -LiteralPath $BundleTarget
Write-Host "[monza-assets] AssetBundle ready: $($bundle.FullName)"
Write-Host "[monza-assets] Size: $([Math]::Round($bundle.Length / 1MB, 2)) MB"
Write-Host "[monza-assets] Other mod folders restored."
Write-Host "[monza-assets] Next run the normal external build with -Install."
