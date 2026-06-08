#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Buildings;
using Helpers;
using UnityEngine;

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
        "Assets/Mods/Gun Store/AmmoSmall.asset",
        "Assets/Mods/Gun Store/WinCheaterSxp.asset",
        "Assets/Mods/Gun Store/BerettaM9.asset",
        "Assets/Mods/Gun Store/AmmoLarge.asset",
        "Assets/Mods/Gun Store/Rpg.asset",
        "Assets/Mods/Gun Store/GunPartsCheap.asset",
        "Assets/Mods/Gun Store/GunPartsExpensive.asset"
    };

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public static IReadOnlyList<ScriptableObject> RecipeAssets { get; private set; } = Array.Empty<ScriptableObject>();

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

        RecipeAssets = GunStoreRecipeFactory.CreateAllRecipes()
            .Where(recipeAsset => recipeAsset != null)
            .Cast<ScriptableObject>()
            .ToArray();

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
        RecipeAssets = Array.Empty<ScriptableObject>();

        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public class GunStoreBusinessTypeCityMod : IModBigAmbitions
{
    private static readonly string[] GunStoreShelfItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_ammosmall",
        "gunstore-businesstype:itemname_wincheatersxp",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_ammolarge",
        "gunstore-businesstype:itemname_rpg"
    };

    private static readonly string[] BlueStoneImporterItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_ammosmall",
        "gunstore-businesstype:itemname_wincheatersxp",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_ammolarge"
    };

    private static readonly string[] MaritimeImporterItemNames =
    {
        "gunstore-businesstype:itemname_gunpartscheap",
        "gunstore-businesstype:itemname_gunpartsexpensive"
    };

    private const string RoundedShelfItemName = "ba:itemname_roundedshelf";
    private const string CheapGiftItemName = "ba:itemname_cheapgift";
    private const string ExpensiveGiftItemName = "ba:itemname_expensivegift";
    private const string ExpensiveFlowersItemName = "ba:itemname_expensiveflower";
    private const string ConsumerGoodsWorkstationType = "ba:factoryworkstationtype_consumergoodsworkstation";

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    private readonly Dictionary<BigAmbitions.Items.Item, string[]> patchedShowcaseShelves = new();
    private readonly List<IList> patchedRecipeLists = new();
    private ImportExportSettings? blueStoneImportSettings;
    private ImportExportSettings? maritimeImportSettings;

    public async Task OnLoadAsync(ModContext context)
    {
        PatchShowcaseShelves();
        AddToImporter();
        PatchConsumerGoodsWorkstation();
        await Task.Yield();
        PatchConsumerGoodsWorkstation();
        await Task.Yield();
        PatchConsumerGoodsWorkstation();
    }

    public Task OnUnloadAsync()
    {
        RestoreConsumerGoodsWorkstation();
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

            var missingGunStoreItems = GunStoreShelfItemNames
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

    private static bool ShouldPatchShowcaseShelf(BigAmbitions.Items.Item item)
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

        foreach (var gunStoreItemName in GunStoreShelfItemNames)
            ShelfController.UnregisterItemToShow(gunStoreItemName);

        patchedShowcaseShelves.Clear();
    }

    private void AddToImporter()
    {
        blueStoneImportSettings ??=
            (ImportExportSettings)BuildingHelper.GetBuilding(new Address("ba:street_pier", 4)).SpecialService.settings;
        maritimeImportSettings ??=
            (ImportExportSettings)BuildingHelper.GetBuilding(new Address("ba:street_pier", 7)).SpecialService.settings;

        foreach (var gunStoreItemName in BlueStoneImporterItemNames)
        {
            if (!blueStoneImportSettings.itemsAvailable.Contains(gunStoreItemName))
                blueStoneImportSettings.itemsAvailable.Add(gunStoreItemName);
        }

        foreach (var gunPartItemName in MaritimeImporterItemNames)
        {
            if (!maritimeImportSettings.itemsAvailable.Contains(gunPartItemName))
                maritimeImportSettings.itemsAvailable.Add(gunPartItemName);
        }
    }

    private void RemoveFromImporter()
    {
        if (blueStoneImportSettings != null)
        {
            foreach (var gunStoreItemName in BlueStoneImporterItemNames)
                blueStoneImportSettings.itemsAvailable.Remove(gunStoreItemName);
        }

        if (maritimeImportSettings != null)
        {
            foreach (var gunPartItemName in MaritimeImporterItemNames)
                maritimeImportSettings.itemsAvailable.Remove(gunPartItemName);
        }
    }

    private void PatchConsumerGoodsWorkstation()
    {
        if (GunStoreBusinessTypeMod.RecipeAssets.Count == 0)
            return;

        var patchedAnyWorkstation = false;

        foreach (var scriptableObject in Resources.FindObjectsOfTypeAll<ScriptableObject>())
        {
            if (TryPatchWorkstation(scriptableObject))
                patchedAnyWorkstation = true;
        }

        if (TryPatchFactoryWorkstationCaches())
            patchedAnyWorkstation = true;

        if (patchedAnyWorkstation)
            RefreshFactoryWorkstationHelper();
    }

    private bool TryPatchFactoryWorkstationCaches()
    {
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Workstations.FactoryWorkstationHelper", false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return false;

        var bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var patchedAnyWorkstation = false;

        foreach (var fieldName in new[] { "AllWorkstations", "AllWorkstationsByType" })
        {
            var field = helperType.GetField(fieldName, bindingFlags);
            if (field?.GetValue(null) == null)
                continue;

            if (TryPatchWorkstationContainer(field.GetValue(null)))
                patchedAnyWorkstation = true;
        }

        return patchedAnyWorkstation;
    }

    private bool TryPatchWorkstationContainer(object? container)
    {
        if (container == null)
            return false;

        var patchedAnyWorkstation = false;

        if (TryPatchWorkstation(container))
            patchedAnyWorkstation = true;

        if (container is IDictionary dictionary)
        {
            foreach (var value in dictionary.Values)
            {
                if (TryPatchWorkstationContainer(value))
                    patchedAnyWorkstation = true;
            }

            return patchedAnyWorkstation;
        }

        if (container is IEnumerable enumerable && container is not string)
        {
            foreach (var value in enumerable)
            {
                if (TryPatchWorkstationContainer(value))
                    patchedAnyWorkstation = true;
            }
        }

        return patchedAnyWorkstation;
    }

    private bool TryPatchWorkstation(object? workstationObject)
    {
        if (workstationObject == null)
            return false;

        var type = workstationObject.GetType();
        if (type.FullName != "BigAmbitions.Factories.Workstations.FactoryWorkstation")
            return false;

        var workstationTypeField =
            type.GetField("workstationType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var supportedRecipesField =
            type.GetField("supportedRecipes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (workstationTypeField == null || supportedRecipesField == null)
            return false;

        if (!string.Equals(workstationTypeField.GetValue(workstationObject) as string, ConsumerGoodsWorkstationType,
                StringComparison.Ordinal))
            return false;

        if (supportedRecipesField.GetValue(workstationObject) is not IList supportedRecipes)
            return false;

        var addedAnyRecipe = false;
        foreach (var recipeAsset in GunStoreBusinessTypeMod.RecipeAssets)
        {
            if (supportedRecipes.Contains(recipeAsset))
                continue;

            supportedRecipes.Add(recipeAsset);
            addedAnyRecipe = true;
        }

        if (addedAnyRecipe && !patchedRecipeLists.Contains(supportedRecipes))
            patchedRecipeLists.Add(supportedRecipes);

        return addedAnyRecipe;
    }

    private static void RefreshFactoryWorkstationHelper()
    {
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Workstations.FactoryWorkstationHelper", false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return;

        var onFactoryWorkstationsLoaded =
            helperType.GetMethod("OnFactoryWorkstationsLoaded", BindingFlags.Static | BindingFlags.Public |
                                                             BindingFlags.NonPublic);
        if (onFactoryWorkstationsLoaded == null || onFactoryWorkstationsLoaded.GetParameters().Length != 0)
            return;

        onFactoryWorkstationsLoaded.Invoke(null, null);
    }

    private void RestoreConsumerGoodsWorkstation()
    {
        if (GunStoreBusinessTypeMod.RecipeAssets.Count == 0)
            return;

        foreach (var supportedRecipes in patchedRecipeLists)
        {
            foreach (var recipeAsset in GunStoreBusinessTypeMod.RecipeAssets)
                supportedRecipes.Remove(recipeAsset);
        }

        patchedRecipeLists.Clear();
    }
}
