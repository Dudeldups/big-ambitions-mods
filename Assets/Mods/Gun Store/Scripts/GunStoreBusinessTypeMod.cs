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
        "Assets/Mods/Gun Store/Colt1911.asset",
        "Assets/Mods/Gun Store/WebleyFosbery.asset",
        "Assets/Mods/Gun Store/BerettaM9.asset",
        "Assets/Mods/Gun Store/WinchesterRepeater.asset",
        "Assets/Mods/Gun Store/Rpg.asset",
        "Assets/Mods/Gun Store/GunPartsCheap.asset",
        "Assets/Mods/Gun Store/GunPartsExpensive.asset"
    };

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public static ScriptableObject? Ak47RecipeAsset { get; private set; }

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

        Ak47RecipeAsset = CreateAk47RecipeAsset();

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
        Ak47RecipeAsset = null;

        return Task.CompletedTask;
    }

    private static ScriptableObject? CreateAk47RecipeAsset()
    {
        var recipeType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Recipes.Recipe", false))
            .FirstOrDefault(type => type != null);
        if (recipeType == null || !typeof(ScriptableObject).IsAssignableFrom(recipeType))
            return null;

        var recipeAsset = ScriptableObject.CreateInstance(recipeType);
        recipeAsset.name = "Ak47Recipe";

        SetFieldValue(recipeType, recipeAsset, "id", "sSoU0AdCKUWnH+qY0k+K+A==");

        var recipeItemType = recipeType.Assembly.GetType("BigAmbitions.Factories.Recipes.RecipeItem");
        if (recipeItemType == null)
            return recipeAsset;

        SetCollectionField(recipeType, recipeAsset, "ingredients", recipeItemType, new[]
        {
            CreateRecipeItem(recipeItemType, "ba:itemname_plastic", 20),
            CreateRecipeItem(recipeItemType, "gunstore-businesstype:itemname_gunpartscheap", 40),
            CreateRecipeItem(recipeItemType, "gunstore-businesstype:itemname_gunpartsexpensive", 20)
        });

        SetFieldValue(recipeType, recipeAsset, "output",
            CreateRecipeItem(recipeItemType, "gunstore-businesstype:itemname_ak47", 20));

        var machineVisualsField = recipeType.GetField("machineVisuals",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var machineVisualType = GetElementType(machineVisualsField?.FieldType);
        if (machineVisualsField != null && machineVisualType != null)
        {
            SetCollectionField(recipeType, recipeAsset, "machineVisuals", machineVisualType, new[]
            {
                CreateMachineVisual(machineVisualType, "ba:itemname_lasercuttingmachine",
                    "gunstore-businesstype:itemname_gunpartscheap",
                    "gunstore-businesstype:itemname_gunpartscheap"),
                CreateMachineVisual(machineVisualType, "ba:itemname_consumergoodsassemblymachine",
                    string.Empty,
                    "gunstore-businesstype:itemname_ak47")
            });
        }

        return recipeAsset;
    }

    private static object CreateRecipeItem(Type recipeItemType, string itemName, int amount)
    {
        var recipeItem = Activator.CreateInstance(recipeItemType);
        if (recipeItem == null)
            throw new InvalidOperationException($"Could not create {recipeItemType.FullName}.");

        SetFieldValue(recipeItemType, recipeItem, "item", itemName);
        SetFieldValue(recipeItemType, recipeItem, "amount", amount);

        return recipeItem;
    }

    private static object CreateMachineVisual(Type machineVisualType, string machineName, string inputItemName,
        string outputItemName)
    {
        var machineVisual = Activator.CreateInstance(machineVisualType);
        if (machineVisual == null)
            throw new InvalidOperationException($"Could not create {machineVisualType.FullName}.");

        SetFieldValue(machineVisualType, machineVisual, "machineName", machineName);
        SetFieldValue(machineVisualType, machineVisual, "inputItemName", inputItemName);
        SetFieldValue(machineVisualType, machineVisual, "outputItemName", outputItemName);
        SetFieldValue(machineVisualType, machineVisual, "shaderColorA", Color.clear);
        SetFieldValue(machineVisualType, machineVisual, "shaderColorB", Color.clear);

        return machineVisual;
    }

    private static void SetCollectionField(Type ownerType, object owner, string fieldName, Type elementType,
        object[] values)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return;

        if (field.FieldType.IsArray)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var i = 0; i < values.Length; i++)
                array.SetValue(values[i], i);

            field.SetValue(owner, array);
            return;
        }

        var list = Activator.CreateInstance(field.FieldType) as IList;
        if (list == null)
            return;

        foreach (var value in values)
            list.Add(value);

        field.SetValue(owner, list);
    }

    private static Type? GetElementType(Type? collectionType)
    {
        if (collectionType == null)
            return null;

        if (collectionType.IsArray)
            return collectionType.GetElementType();

        return collectionType.IsGenericType ? collectionType.GetGenericArguments().FirstOrDefault() : null;
    }

    private static void SetFieldValue(Type ownerType, object owner, string fieldName, object? value)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            field.SetValue(owner, value);
    }
}

[ModEntryOnCityLoad]
public class GunStoreBusinessTypeCityMod : IModBigAmbitions
{
    private static readonly string[] GunStoreShelfItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_colt1911",
        "gunstore-businesstype:itemname_webleyfosbery",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_winchesterrepeater",
        "gunstore-businesstype:itemname_rpg"
    };

    private static readonly string[] BlueStoneImporterItemNames =
    {
        "gunstore-businesstype:itemname_ak47",
        "gunstore-businesstype:itemname_colt1911",
        "gunstore-businesstype:itemname_webleyfosbery",
        "gunstore-businesstype:itemname_berettam9",
        "gunstore-businesstype:itemname_winchesterrepeater",
        "gunstore-businesstype:itemname_rpg"
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

    private readonly Dictionary<Item, string[]> patchedShowcaseShelves = new();
    private readonly List<IList> patchedRecipeLists = new();
    private ImportExportSettings? blueStoneImportSettings;
    private ImportExportSettings? maritimeImportSettings;

    public Task OnLoadAsync(ModContext context)
    {
        PatchShowcaseShelves();
        AddToImporter();
        PatchConsumerGoodsWorkstation();
        return Task.CompletedTask;
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
        var recipeAsset = GunStoreBusinessTypeMod.Ak47RecipeAsset;
        if (recipeAsset == null)
            return;

        foreach (var scriptableObject in Resources.FindObjectsOfTypeAll<ScriptableObject>())
        {
            var type = scriptableObject.GetType();
            if (type.FullName != "BigAmbitions.Factories.Workstations.FactoryWorkstation")
                continue;

            var workstationTypeField =
                type.GetField("workstationType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var supportedRecipesField =
                type.GetField("supportedRecipes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (workstationTypeField == null || supportedRecipesField == null)
                continue;

            if (!string.Equals(workstationTypeField.GetValue(scriptableObject) as string, ConsumerGoodsWorkstationType,
                    StringComparison.Ordinal))
                continue;

            if (supportedRecipesField.GetValue(scriptableObject) is not IList supportedRecipes
                || supportedRecipes.Contains(recipeAsset))
                continue;

            supportedRecipes.Add(recipeAsset);
            patchedRecipeLists.Add(supportedRecipes);
        }
    }

    private void RestoreConsumerGoodsWorkstation()
    {
        var recipeAsset = GunStoreBusinessTypeMod.Ak47RecipeAsset;
        if (recipeAsset == null)
            return;

        foreach (var supportedRecipes in patchedRecipeLists)
            supportedRecipes.Remove(recipeAsset);

        patchedRecipeLists.Clear();
    }
}
