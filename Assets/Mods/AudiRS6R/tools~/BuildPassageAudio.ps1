[CmdletBinding()]
param([string]$ScratchRoot = (Join-Path $env:TEMP ('AudiRS6R-audio-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$modRoot = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force "$ScratchRoot/Assets/Editor", "$ScratchRoot/Assets/Mods/AudiRS6R/Audio/Passage01", "$ScratchRoot/Packages", "$ScratchRoot/ProjectSettings" | Out-Null
Copy-Item "$modRoot/Audio/Passage01/*.wav*" "$ScratchRoot/Assets/Mods/AudiRS6R/Audio/Passage01"
Copy-Item "$PSScriptRoot/Editor/PassageAudioBuild.cs" "$ScratchRoot/Assets/Editor"
Copy-Item "$modRoot/Scripts/AudiRS6REngineTone.cs" "$ScratchRoot/Assets"
"m_EditorVersion: 2022.3.62f2`nm_EditorVersionWithRevision: 2022.3.62f2 (7670c08855a9)" | Set-Content "$ScratchRoot/ProjectSettings/ProjectVersion.txt"
'{"dependencies":{"com.unity.modules.audio":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}' | Set-Content "$ScratchRoot/Packages/manifest.json"
unity --format ndjson run $ScratchRoot --editor-version 2022.3.62f2 --timeout 600 -- -executeMethod PassageAudioBuild.Build -logFile "$ScratchRoot/unity.log"
if ($LASTEXITCODE -ne 0 -or !(Select-String -Path "$ScratchRoot/unity.log" -SimpleMatch '[PassageAudioBuild] PASS:')) {
    throw "Audio build failed; inspect $ScratchRoot/unity.log"
}
foreach ($platform in @('Windows', 'Mac')) {
    Copy-Item "$ScratchRoot/AudioBuild/$platform/audirs6r-engine.unity3d" "$modRoot/AssetBundles/$platform"
    Copy-Item "$ScratchRoot/AudioBuild/$platform/audirs6r-engine.unity3d.manifest" "$modRoot/AssetBundles/$platform"
}
Write-Host "Validated passage_01 bundles. Build evidence: $ScratchRoot/unity.log"
