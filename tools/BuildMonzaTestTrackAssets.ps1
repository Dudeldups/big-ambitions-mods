param(
    [string] $SourceGlb,
    [string] $UnityExe = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ModelTarget = Join-Path $RepoRoot "Assets\Mods\Monza_Test_Track\Models\monza_circuit_1998_layout.glb"
$BundleTarget = Join-Path $RepoRoot "Assets\Mods\Monza_Test_Track\AssetBundles\Windows\monzatesttrack.unity3d"
$LogPath = Join-Path $RepoRoot "Logs\MonzaTestTrackAssetBuild.log"

if (-not $SourceGlb) {
    $downloadCandidates = @(
        "E:\\Downloads",
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
Put the downloaded file in your Downloads folder with a name like
  monza_circuit_1998_layout(1).glb
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

Write-Host "[monza-assets] Starting Unity batch AssetBundle build..."
Write-Host "[monza-assets] Log: $LogPath"

$unityArgs = @(
    "-batchmode",
    "-quit",
    "-projectPath", ('"' + $RepoRoot + '"'),
    "-executeMethod", "MonzaTestTrack.Editor.MonzaTestTrackAssetBuilder.BuildBatch",
    "-logFile", ('"' + $LogPath + '"')
)

# Unity.exe is a Windows GUI executable. A direct invocation can return control to
# PowerShell before the Unity process has actually finished. Start-Process -Wait
# guarantees that bundle existence is checked only after Unity has exited.
$unityProcess = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -Wait -PassThru

$exitCode = $unityProcess.ExitCode
if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "===== MONZA UNITY BUILD LOG (tail) ====="
    if (Test-Path -LiteralPath $LogPath) {
        Get-Content -LiteralPath $LogPath -Tail 200
    }
    throw "Unity Monza AssetBundle build failed with exit code $exitCode."
}

if (-not (Test-Path -LiteralPath $BundleTarget -PathType Leaf)) {
    Write-Host ""
    Write-Host "===== MONZA UNITY BUILD LOG (tail) ====="
    if (Test-Path -LiteralPath $LogPath) {
        Get-Content -LiteralPath $LogPath -Tail 200
    }
    throw "Unity exited with code 0 but the expected bundle was not created: $BundleTarget"
}

$bundle = Get-Item -LiteralPath $BundleTarget
Write-Host "[monza-assets] AssetBundle ready: $($bundle.FullName)"
Write-Host "[monza-assets] Size: $([Math]::Round($bundle.Length / 1MB, 2)) MB"
Write-Host "[monza-assets] Next run the normal external build with -Install."
