#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    private const string BundleKey = "AssetBundles/gunstore-businesstype.unity3d";
    private const string GunStoreBusinessTypeName = "gunstore-businesstype:businesstype_gunstore";
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
    private const string BusinessLayoutSetHelperTypeName = "BusinessLayoutSets.BusinessLayoutSetHelper";
    private const string CompetitionHelperTypeName = "Helpers.CompetitionHelper";
    private static readonly LayoutRegistration[] RivalLayouts =
    {
        new("Assets/Mods/Gun Store/Layouts/GunStoreRivalsC1.json", "GunStoreRivalsC1.json", "GunStoreRivalsC1"),
        new("Assets/Mods/Gun Store/Layouts/GunStoreRivalsA1.json", "GunStoreRivalsA1.json", "GunStoreRivalsA1"),
        new("Assets/Mods/Gun Store/Layouts/GunStoreRivalsC2.json", "GunStoreRivalsC2.json", "GunStoreRivalsC2"),
        new("Assets/Mods/Gun Store/Layouts/GunStoreRivalsD2.json", "GunStoreRivalsD2.json", "GunStoreRivalsD2"),
        new("Assets/Mods/Gun Store/Layouts/GunStoreRivalsM1.json", "GunStoreRivalsM1.json", "GunStoreRivalsM1")
    };
    private static readonly string[] GunStoreRivalBusinessNames =
    {
        "Friendly Fire Department",
        "Pew Pew Defense",
        "Guns R Us",
        "McMunition’s",
        "Respawn Disablers",
        "Pay-To-Win Supply Co.",
        "No Brain, Just Aim",
        "Boom Boom & Beyond",
        "Safety Third Firearms"
    };
    private static readonly string[] GunStoreRivalLayoutNames =
    {
        "GunStoreRivalsC1",
        "GunStoreRivalsC1",
        "GunStoreRivalsA1",
        "GunStoreRivalsA1",
        "GunStoreRivalsC2",
        "GunStoreRivalsD2",
        "GunStoreRivalsM1"
    };
    private static readonly string[] RivalTemplateLayouts =
    {
        "GiftShopRivals",
        "LiquorRivals",
        "ElectronicsRivals",
        "JewelryRivals"
    };

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private readonly Dictionary<BigAmbitions.Items.Item, string[]> patchedShowcaseShelves = new();
    private readonly List<IList> patchedRecipeLists = new();
    private readonly List<ScriptableObject> injectedAiBusinessDefaults = new();
    private ImportExportSettings? blueStoneImportSettings;
    private ImportExportSettings? maritimeImportSettings;

    public async Task OnLoadAsync(ModContext context)
    {
        RegisterBundledLayout(context);
        PatchCompetitionDefaults();
        PatchShowcaseShelves();
        AddToImporter();
        PatchConsumerGoodsWorkstation();
        await Task.Yield();
        RegisterBundledLayout(context);
        PatchCompetitionDefaults();
        PatchConsumerGoodsWorkstation();
        await Task.Yield();
        RegisterBundledLayout(context);
        PatchCompetitionDefaults();
        PatchConsumerGoodsWorkstation();
    }

    public Task OnUnloadAsync()
    {
        RestoreConsumerGoodsWorkstation();
        RestoreCompetitionDefaults();
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

    private static void RegisterBundledLayout(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(BusinessLayoutSetHelperTypeName, false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return;

        var setBusinessLayoutMethod = helperType.GetMethod(
            "SetBusinessLayoutSynchronous",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string) },
            null);
        if (setBusinessLayoutMethod == null)
            return;

        var tempDirectory = Path.Combine(Application.temporaryCachePath, "BAModLayouts", context.ModId);
        Directory.CreateDirectory(tempDirectory);

        foreach (var rivalLayout in RivalLayouts)
        {
            var layoutAsset = bundle.LoadAsset<TextAsset>(rivalLayout.AssetPath);
            if (layoutAsset == null || string.IsNullOrWhiteSpace(layoutAsset.text))
                continue;

            var tempPath = Path.Combine(tempDirectory, rivalLayout.FileName);
            File.WriteAllText(tempPath, layoutAsset.text);
            setBusinessLayoutMethod.Invoke(null, new object[] { tempPath });
        }
    }

    private void PatchCompetitionDefaults()
    {
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(CompetitionHelperTypeName, false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return;

        EnsureInjectedAiBusinessDefaults();
        if (injectedAiBusinessDefaults.Count == 0)
            return;

        var bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        PatchBusinessDefaultsCached(helperType.GetField("BusinessDefaultsCached", bindingFlags));
        PatchBusinessDefaultsByType(helperType.GetField("BusinessDefaultsByType", bindingFlags));
    }

    private void EnsureInjectedAiBusinessDefaults()
    {
        if (injectedAiBusinessDefaults.Count > 0)
            return;

        var templates = FindAiBusinessDefaultTemplates().ToArray();
        if (templates.Length == 0)
            return;

        for (var i = 0; i < GunStoreRivalBusinessNames.Length; i++)
        {
            var clone = UnityEngine.Object.Instantiate(templates[i % templates.Length]);
            clone.name = GunStoreRivalBusinessNames[i].Replace(" ", string.Empty);
            SetFieldValue(clone, "businessTypeName", GunStoreBusinessTypeName);
            SetFieldValue(clone, "businessName", GunStoreRivalBusinessNames[i]);
            SetFieldValue(clone, "buildingLayout", GunStoreRivalLayoutNames[i]);
            injectedAiBusinessDefaults.Add(clone);
        }
    }

    private static IEnumerable<ScriptableObject> FindAiBusinessDefaultTemplates()
    {
        return Resources.FindObjectsOfTypeAll<ScriptableObject>()
            .Where(IsAiBusinessDefaultObject)
            .Where(scriptableObject =>
            {
                var businessTypeName = GetStringFieldValue(scriptableObject, "businessTypeName");
                return !string.Equals(businessTypeName, GunStoreBusinessTypeName, StringComparison.Ordinal);
            })
            .OrderBy(scriptableObject =>
            {
                var layoutName = GetStringFieldValue(scriptableObject, "buildingLayout");
                var preferredIndex = Array.IndexOf(RivalTemplateLayouts, layoutName);
                return preferredIndex < 0 ? int.MaxValue : preferredIndex;
            })
            .ThenBy(scriptableObject => scriptableObject.name)
            .GroupBy(scriptableObject => GetStringFieldValue(scriptableObject, "buildingLayout"))
            .Select(group => group.First());
    }

    private static bool IsAiBusinessDefaultObject(ScriptableObject scriptableObject)
    {
        if (scriptableObject == null)
            return false;

        return HasField(scriptableObject, "businessTypeName")
               && HasField(scriptableObject, "businessName")
               && HasField(scriptableObject, "buildingLayout")
               && HasField(scriptableObject, "corporationRivalId")
               && HasField(scriptableObject, "goodsSource")
               && HasField(scriptableObject, "schedule");
    }

    private void PatchBusinessDefaultsCached(FieldInfo? field)
    {
        if (field == null)
            return;

        var cachedDefaultsValue = field.GetValue(null);
        if (cachedDefaultsValue == null)
            return;

        if (cachedDefaultsValue.GetType().IsArray)
        {
            field.SetValue(null, AppendUniqueValues(cachedDefaultsValue.GetType(), cachedDefaultsValue as IEnumerable));
            return;
        }

        if (cachedDefaultsValue is not IList cachedDefaults)
            return;

        foreach (var injectedDefault in injectedAiBusinessDefaults)
        {
            if (!cachedDefaults.Contains(injectedDefault))
                cachedDefaults.Add(injectedDefault);
        }
    }

    private void PatchBusinessDefaultsByType(FieldInfo? field)
    {
        if (field?.GetValue(null) is not IDictionary defaultsByType)
            return;

        var existingDefaults = defaultsByType[GunStoreBusinessTypeName];
        if (existingDefaults != null && existingDefaults.GetType().IsArray)
        {
            defaultsByType[GunStoreBusinessTypeName] =
                AppendUniqueValues(existingDefaults.GetType(), existingDefaults as IEnumerable);
            return;
        }

        if (existingDefaults is IList defaultsForType)
        {
            foreach (var injectedDefault in injectedAiBusinessDefaults)
            {
                if (!defaultsForType.Contains(injectedDefault))
                    defaultsForType.Add(injectedDefault);
            }

            return;
        }

        var dictionaryValueType = field.FieldType.IsGenericType
            ? field.FieldType.GetGenericArguments().LastOrDefault()
            : null;
        if (dictionaryValueType == null)
            return;

        defaultsByType[GunStoreBusinessTypeName] = CreateCollection(dictionaryValueType, injectedAiBusinessDefaults);
    }

    private void RestoreCompetitionDefaults()
    {
        var helperType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(CompetitionHelperTypeName, false))
            .FirstOrDefault(type => type != null);
        if (helperType == null)
            return;

        var bindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        if (helperType.GetField("BusinessDefaultsCached", bindingFlags)?.GetValue(null) is IList cachedDefaults)
        {
            foreach (var injectedDefault in injectedAiBusinessDefaults)
                cachedDefaults.Remove(injectedDefault);
        }

        if (helperType.GetField("BusinessDefaultsByType", bindingFlags)?.GetValue(null) is IDictionary defaultsByType)
        {
            if (defaultsByType[GunStoreBusinessTypeName] is IList defaultsForType)
            {
                foreach (var injectedDefault in injectedAiBusinessDefaults)
                    defaultsForType.Remove(injectedDefault);

                if (defaultsForType.Count == 0)
                    defaultsByType.Remove(GunStoreBusinessTypeName);
            }
            else
            {
                defaultsByType.Remove(GunStoreBusinessTypeName);
            }
        }

        foreach (var injectedDefault in injectedAiBusinessDefaults)
            UnityEngine.Object.Destroy(injectedDefault);

        injectedAiBusinessDefaults.Clear();
    }

    private static object? CreateCollection(Type collectionType, IReadOnlyList<ScriptableObject> values)
    {
        if (collectionType.IsArray)
        {
            var elementType = collectionType.GetElementType();
            if (elementType == null)
                return null;

            var array = Array.CreateInstance(elementType, values.Count);
            for (var i = 0; i < values.Count; i++)
                array.SetValue(values[i], i);

            return array;
        }

        if (Activator.CreateInstance(collectionType) is not IList list)
            return null;

        foreach (var value in values)
            list.Add(value);

        return list;
    }

    private object? AppendUniqueValues(Type collectionType, IEnumerable? existingValues)
    {
        var combined = new List<object>();

        if (existingValues != null)
        {
            foreach (var value in existingValues)
            {
                if (value != null)
                    combined.Add(value);
            }
        }

        foreach (var injectedDefault in injectedAiBusinessDefaults)
        {
            if (!combined.Contains(injectedDefault))
                combined.Add(injectedDefault);
        }

        if (collectionType.IsArray)
        {
            var elementType = collectionType.GetElementType();
            if (elementType == null)
                return null;

            var array = Array.CreateInstance(elementType, combined.Count);
            for (var i = 0; i < combined.Count; i++)
                array.SetValue(combined[i], i);

            return array;
        }

        if (Activator.CreateInstance(collectionType) is not IList list)
            return null;

        foreach (var value in combined)
            list.Add(value);

        return list;
    }

    private static bool HasField(object owner, string fieldName)
    {
        return owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) !=
               null;
    }

    private static string? GetStringFieldValue(object owner, string fieldName)
    {
        return owner.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(owner) as string;
    }

    private static void SetFieldValue(object owner, string fieldName, object? value)
    {
        owner.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(owner, value);
    }

    private readonly struct LayoutRegistration
    {
        public LayoutRegistration(string assetPath, string fileName, string layoutName)
        {
            AssetPath = assetPath;
            FileName = fileName;
            LayoutName = layoutName;
        }

        public string AssetPath { get; }
        public string FileName { get; }
        public string LayoutName { get; }
    }
}
