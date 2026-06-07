#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Buildings;
using Helpers;

[assembly: RegisterModClass(typeof(GunStoreBusinessTypeMod))]
[assembly: RegisterModClass(typeof(GunStoreBusinessTypeCityMod))]

[ModEntryOnInitializationLoad]
public class GunStoreBusinessTypeMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/gunstore-businesstype.unity3d";
    private const string BusinessTypeAssetPath = "Assets/Mods/Gun Store/GunStore.asset";
    private static readonly string[] ItemAssetPaths =
    {
        "Assets/Mods/Gun Store/Ak47.asset",
        "Assets/Mods/Gun Store/Colt1911.asset",
        "Assets/Mods/Gun Store/WebleyFosbery.asset",
        "Assets/Mods/Gun Store/BerettaM9.asset",
        "Assets/Mods/Gun Store/WinchesterRepeater.asset",
        "Assets/Mods/Gun Store/Rpg.asset"
    };

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private BusinessType? modBusinessType;
    private readonly List<Item> modItems = new();

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);

        modItems.Clear();
        foreach (var itemAssetPath in ItemAssetPaths)
        {
            var modItem = bundle.LoadAsset<Item>(itemAssetPath);
            if (modItem == null)
                continue;

            modItems.Add(modItem);
            ItemsGetter.RegisterModItem(modItem);
        }

        modBusinessType = bundle.LoadAsset<BusinessType>(BusinessTypeAssetPath);
        if (modBusinessType != null)
            ModdingAPI.RegisterModBusinessType(modBusinessType);

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (modBusinessType != null)
            ModdingAPI.UnregisterModBusinessType(modBusinessType);

        foreach (var modItem in modItems)
            ItemsGetter.UnregisterModItem(modItem.itemName);

        modItems.Clear();

        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public class GunStoreBusinessTypeCityMod : IModBigAmbitions
{
    private static readonly string[] GunStoreItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_colt1911",
        "gunstore-businesstype:itemname_webleyfosbery",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_winchesterrepeater",
        "gunstore-businesstype:itemname_rpg"
    };

    private const string RoundedShelfItemName = "ba:itemname_roundedshelf";
    private const string CheapGiftItemName = "ba:itemname_cheapgift";
    private const string ExpensiveGiftItemName = "ba:itemname_expensivegift";
    private const string ExpensiveFlowersItemName = "ba:itemname_expensiveflower";

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    private readonly Dictionary<Item, string[]> patchedShowcaseShelves = new();
    private ImportExportSettings? importSettings;

    public Task OnLoadAsync(ModContext context)
    {
        PatchShowcaseShelves();
        AddToImporter();
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        RestoreShowcaseShelves();
        RemoveFromImporter();
        return Task.CompletedTask;
    }

    private void PatchShowcaseShelves()
    {
        if (ItemsGetter.AllItems == null)
            return;

        foreach (var item in ItemsGetter.AllItems)
        {
            if (!ShouldPatchShowcaseShelf(item))
                continue;

            var missingGunStoreItems = GunStoreItemNames
                .Where(gunStoreItemName => !item.itemsThatCanShowcase.Contains(gunStoreItemName))
                .ToArray();
            if (missingGunStoreItems.Length == 0)
                continue;

            patchedShowcaseShelves[item] = item.itemsThatCanShowcase.ToArray();

            foreach (var gunStoreItemName in missingGunStoreItems)
            {
                ShelfController.RegisterItemToShow(
                    gunStoreItemName,
                    item.itemName,
                    item.itemName == RoundedShelfItemName ? ExpensiveFlowersItemName : CheapGiftItemName);
            }

            item.itemsThatCanShowcase = item.itemsThatCanShowcase.Concat(missingGunStoreItems).ToArray();
        }
    }

    private static bool ShouldPatchShowcaseShelf(Item item)
    {
        if (item == null || item.itemsThatCanShowcase == null)
            return false;

        if (item.itemName == RoundedShelfItemName)
            return true;

        return (item.type & ItemType.ShowcaseShelf) != 0
            && (item.itemsThatCanShowcase.Contains(CheapGiftItemName)
                || item.itemsThatCanShowcase.Contains(ExpensiveGiftItemName));
    }

    private void RestoreShowcaseShelves()
    {
        foreach (var patchedShelf in patchedShowcaseShelves)
            patchedShelf.Key.itemsThatCanShowcase = patchedShelf.Value;

        foreach (var gunStoreItemName in GunStoreItemNames)
            ShelfController.UnregisterItemToShow(gunStoreItemName);

        patchedShowcaseShelves.Clear();
    }

    private void AddToImporter()
    {
        importSettings ??=
            (ImportExportSettings)BuildingHelper.GetBuilding(new Address("ba:street_pier", 4)).SpecialService.settings;

        foreach (var gunStoreItemName in GunStoreItemNames)
        {
            if (!importSettings.itemsAvailable.Contains(gunStoreItemName))
                importSettings.itemsAvailable.Add(gunStoreItemName);
        }
    }

    private void RemoveFromImporter()
    {
        if (importSettings == null)
            return;

        foreach (var gunStoreItemName in GunStoreItemNames)
            importSettings.itemsAvailable.Remove(gunStoreItemName);
    }
}
