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
    private GunStoreHelpDebugRuntime? helpDebugRuntime;

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

        helpDebugRuntime = GunStoreHelpDebugRuntime.Initialize(context);

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        helpDebugRuntime?.Shutdown();
        helpDebugRuntime = null;

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
    private const string RpgItemName = "gunstore-businesstype:itemname_rpg";
    private static readonly Address BlueStoneImporterAddress = new("ba:street_pier", 4);
    private static readonly Address MaritimeImporterAddress = new("ba:street_pier", 7);
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
        "gunstore-businesstype:itemname_ammolarge",
        "gunstore-businesstype:itemname_rpg"
    };

    private static readonly string[] MaritimeImporterItemNames =
    {
        "gunstore-businesstype:itemname_gunpartscheap",
        "gunstore-businesstype:itemname_gunpartsexpensive"
    };
    private static readonly string[] RetiredAiRivalBusinessNames =
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

    private const string RoundedShelfItemName = "ba:itemname_roundedshelf";
    private const string CheapGiftItemName = "ba:itemname_cheapgift";
    private const string ExpensiveGiftItemName = "ba:itemname_expensivegift";
    private const string ExpensiveFlowersItemName = "ba:itemname_expensiveflower";
    private const string ConsumerGoodsWorkstationType = "ba:factoryworkstationtype_consumergoodsworkstation";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private readonly Dictionary<BigAmbitions.Items.Item, string[]> patchedShowcaseShelves = new();
    private readonly List<IList> patchedRecipeLists = new();
    private ImportExportSettings? blueStoneImportSettings;
    private ImportExportSettings? maritimeImportSettings;

    internal static void RepairEmptyProductCachesAfterGameLoaded(ModContext? context)
    {
        var registrations = SaveGameManager.Current?.BuildingRegistrations;
        if (registrations == null)
            return;

        var changed = false;
        foreach (var registration in registrations)
        {
            if (registration == null || !registration.RentedByPlayer ||
                !string.Equals(registration.businessTypeName, GunStoreBusinessTypeName, StringComparison.Ordinal) ||
                registration.cachedAvailableProducts?.Count > 0 ||
                !HasLoadedGunStoreStock(registration))
            {
                continue;
            }

            registration.cachedAvailableProducts ??= new List<string>();

            try
            {
                BusinessHelper.UpdateCachedAvailableProducts(registration);
                changed |= registration.cachedAvailableProducts.Count > 0;
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        if (changed)
            SaveGameManager.MarkChange();
    }

    internal static void RetireLegacyAiRivalsAfterGameLoaded(ModContext? context)
    {
        var registrations = SaveGameManager.Current?.BuildingRegistrations;
        if (registrations == null)
            return;

        var retiredCount = 0;
        foreach (var registration in registrations)
        {
            if (registration == null ||
                registration.RentedByPlayer ||
                !RetiredAiRivalBusinessNames.Contains(registration.BusinessName, StringComparer.Ordinal))
            {
                continue;
            }

            var wasAlreadyClosed = registration.temporarilyClosed;
            if (!wasAlreadyClosed)
            {
                registration.temporarilyClosed = true;
                retiredCount++;
            }

            context?.Logger.Warn(
                $"Gun Store: retired legacy AI rival: name='{registration.BusinessName}', " +
                $"address={registration.Address}, businessType='{registration.businessTypeName}', " +
                $"businessOwnerRivalId='{registration.businessOwnerRivalId ?? "<none>"}', " +
                $"wasAlreadyClosed={wasAlreadyClosed}. " +
                "It is not player-owned and has been closed to prevent obsolete Gun Store layouts " +
                "from participating in customer and shelf simulation.");
        }

        if (retiredCount <= 0)
            return;

        SaveGameManager.MarkChange();
        context?.Logger.Info(
            $"Gun Store: closed {retiredCount} legacy AI rival business(es) in this save. " +
            "Player-owned businesses were not changed.");
    }

    private static bool HasLoadedGunStoreStock(BuildingRegistration registration)
    {
        if (registration.itemInstances == null)
            return false;

        foreach (var itemInstance in registration.itemInstances.Values)
        {
            if (itemInstance?.ItemCached == null)
                continue;

            var stock = ItemHelper.GetStockInstance(itemInstance);
            if (stock != null && GunStoreShelfItemNames.Contains(stock.itemName))
                return true;
        }

        return false;
    }

    public async Task OnLoadAsync(ModContext context)
    {
        context.Logger.Info(
            "Gun Store city integration loaded with AI-rival and layout-cache patches disabled; " +
            "product visual mappings are limited to base-game showcase fixtures.");

        for (var i = 0; i < 6; i++)
        {
            if (i == 0)
                PatchShowcaseShelves(context);

            AddToImporter();
            PatchImportPartnerships();
            PatchConsumerGoodsWorkstation();

            if (i < 5)
                await Task.Yield();
        }

    }

    public Task OnUnloadAsync()
    {
        RestoreConsumerGoodsWorkstation();
        RestoreShowcaseShelves();
        RemoveFromImporter();
        return Task.CompletedTask;
    }

    private void PatchShowcaseShelves(ModContext context)
    {
        // ShelfController stores visual mappings globally. Clear mappings left by a hot reload
        // before registering only the base-game fixtures this mod supports.
        foreach (var gunStoreItemName in GunStoreShelfItemNames)
            ShelfController.UnregisterItemToShow(gunStoreItemName);

        if (ItemsGetter.AllItems == null)
            return;

        var patchedShelfCount = 0;
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
            patchedShelfCount++;
        }

        context.Logger.Info(
            $"Gun Store showcase integration registered {GunStoreShelfItemNames.Length} products on " +
            $"{patchedShelfCount} base-game fixture catalog(s). Custom-mod fixtures were not modified.");
    }

    private static bool ShouldPatchShowcaseShelf(BigAmbitions.Items.Item item)
    {
        if (item == null || item.itemsThatCanShowcase == null ||
            !item.itemName.StartsWith("ba:", StringComparison.Ordinal))
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
            (ImportExportSettings)BuildingHelper.GetBuilding(BlueStoneImporterAddress).SpecialService.settings;
        maritimeImportSettings ??=
            (ImportExportSettings)BuildingHelper.GetBuilding(MaritimeImporterAddress).SpecialService.settings;

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

    private void PatchImportPartnerships()
    {
        var importPartnerships = SaveGameManager.Current?.importPartnerships;
        if (importPartnerships == null)
            return;

        foreach (var importPartnership in importPartnerships)
        {
            if (importPartnership == null)
                continue;

            if (IsBlueStoneImportPartnership(importPartnership))
                RemoveImportProduct(importPartnership, RpgItemName);

            if (IsMaritimeImportPartnership(importPartnership))
                importPartnership.AddMissingImportProducts();
        }
    }

    private static bool IsBlueStoneImportPartnership(object importPartnership)
    {
        var addressValue = GetMemberValue(importPartnership, "importAddress") ?? GetMemberValue(importPartnership, "ImportAddress");
        return addressValue is Address address && address.Equals(BlueStoneImporterAddress);
    }

    private static bool IsMaritimeImportPartnership(object importPartnership)
    {
        var addressValue = GetMemberValue(importPartnership, "importAddress") ?? GetMemberValue(importPartnership, "ImportAddress");
        return addressValue is Address address && address.Equals(MaritimeImporterAddress);
    }

    private static void RemoveImportProduct(object importPartnership, string itemName)
    {
        if (GetMemberValue(importPartnership, "products") is IList products)
        {
            for (var i = products.Count - 1; i >= 0; i--)
            {
                var product = products[i];
                if (product == null)
                    continue;

                var productItemName = GetMemberValue(product, "itemName") as string
                    ?? GetMemberValue(product, "ItemName") as string;
                if (string.Equals(productItemName, itemName, StringComparison.Ordinal))
                    products.RemoveAt(i);
            }
        }

        RemoveDictionaryEntry(importPartnership, "ImporterAmountPerItem", itemName);
        RemoveDictionaryEntry(importPartnership, "ContractAmountPerItem", itemName);

        RemoveCollectionEntry(importPartnership, "AmountExceededItems", itemName);
    }

    private static void RemoveDictionaryEntry(object owner, string memberName, string key)
    {
        if (GetMemberValue(owner, memberName) is not IDictionary dictionary)
            return;

        if (dictionary.Contains(key))
            dictionary.Remove(key);
    }

    private static void RemoveCollectionEntry(object owner, string memberName, object value)
    {
        var collection = GetMemberValue(owner, memberName);
        if (collection == null)
            return;

        if (collection is IList list)
        {
            while (list.Contains(value))
                list.Remove(value);

            return;
        }

        var removeMethod = collection.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public, null, new[] { value.GetType() }, null);
        removeMethod?.Invoke(collection, new[] { value });
    }

    private static object? GetMemberValue(object owner, string memberName)
    {
        const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var ownerType = owner.GetType();

        var field = ownerType.GetField(memberName, bindingFlags);
        if (field != null)
            return field.GetValue(owner);

        var property = ownerType.GetProperty(memberName, bindingFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
            return null;

        try
        {
            return property.GetValue(owner);
        }
        catch
        {
            return null;
        }
    }

}
