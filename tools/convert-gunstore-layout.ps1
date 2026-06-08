$blueprintPath = "C:\Users\Arne\AppData\LocalLow\Hovgaard Games\Big Ambitions\Blueprints\Gun Store C1\Layout.json"
$outputPath = "E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Assets\Mods\Gun Store\Layouts\GunStoreRivalsC1.json"

$saleItems = @(
    "gunstore-businesstype:itemname_ak47",
    "gunstore-businesstype:itemname_berettam9",
    "gunstore-businesstype:itemname_wincheatersxp",
    "gunstore-businesstype:itemname_rpg",
    "gunstore-businesstype:itemname_ammosmall",
    "gunstore-businesstype:itemname_ammolarge"
)

$layout = Get-Content -Raw -Path $blueprintPath | ConvertFrom-Json
$fixtureIndex = 0

foreach ($item in $layout.Items) {
    $isSalesFixture = $item.itemName -eq "ba:itemname_productpanel" -or $item.itemName -eq "ba:itemname_roundedshelf"
    if (-not $isSalesFixture) {
        continue
    }

    if ($null -eq $item.playerItemPurchaserSettings) {
        $item | Add-Member -NotePropertyName playerItemPurchaserSettings -NotePropertyValue ([pscustomobject]@{
            name = ""
            enabled = $true
            itemName = ""
            itemQuantity = 1
            isQuantityItem = $false
        })
    }

    $item.playerItemPurchaserSettings.name = ""
    $item.playerItemPurchaserSettings.enabled = $true
    $item.playerItemPurchaserSettings.itemName = $saleItems[$fixtureIndex % $saleItems.Count]
    $item.playerItemPurchaserSettings.itemQuantity = 1
    $item.playerItemPurchaserSettings.isQuantityItem = $false

    $fixtureIndex++
}

$outputDirectory = Split-Path -Parent $outputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$layout | ConvertTo-Json -Depth 100 | Set-Content -Path $outputPath
Write-Output "Wrote $fixtureIndex sales fixtures to $outputPath"
