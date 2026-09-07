param(
    [Parameter(Mandatory=$true)][string] $SourceRoot,
    [string] $InstallRoot = (Join-Path $env:USERPROFILE 'AppData/LocalLow/Hovgaard Games/Big Ambitions/ModsLocal/AudiRS6R')
)
$ErrorActionPreference = 'Stop'
# Private audition assets stay outside the repository and Workshop package.
$names = @('EngineLow','EngineMid','EngineHigh')
foreach ($bank in @('passage_01','passage_02')) {
    foreach ($name in $names) {
        $source = Join-Path $SourceRoot ($bank+'/'+$name+'.wav')
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing supplied recording: $source" }
    }
}
if (!(Test-Path -LiteralPath (Join-Path $InstallRoot 'AudiRS6R.dll'))) { throw 'Build/install the mod first.' }
$target = Join-Path $InstallRoot 'Config/Audio/Recorded'
foreach ($bank in @('passage_01','passage_02')) {
    $directory = Join-Path $target $bank
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    foreach ($name in $names) {
        $source = Join-Path $SourceRoot ($bank+'/'+$name+'.wav')
        $destination = Join-Path $directory ($name+'.wav')
        Copy-Item -LiteralPath $source -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
            throw "Recording copy mismatch: $destination"
        }
    }
}
foreach ($note in @('README.md','extraction.json')) {
    $source = Join-Path $SourceRoot $note
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $target $note) -Force }
}
Write-Output "Installed six unmodified local audition loops and source notes at $target"
