#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(BugattiChironMod))]
[assembly: RegisterModClass(typeof(BugattiChironCityRegistration))]

[ModEntryOnInitializationLoad]
public sealed class BugattiChironMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "bugattichiron-vehicle:vehicletype_bugattichiron";

    internal const string BundleKey = "AssetBundles/bugattichiron.unity3d";
    internal const string VehicleAssetPath =
        "Assets/Mods/BugattiChiron/BugattiChiron.asset";
    private const string VehiclePrefabPath =
        "Assets/Mods/BugattiChiron/BugattiChiron.prefab";

    private VehicleType? vehicleType;
    private BugattiChironRuntime? runtime;

    public BugattiChironMod()
    {
        BugattiChironAchievementLoadGuard.Install();
    }

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"BugattiChiron: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        vehicleType.autoParkSupported = true;
        BugattiChironAchievementLoadGuard.Configure(context, vehicleType);
        if (!BugattiChironVehicleTypeRegistration.EnsureRegistered(vehicleType))
        {
            context.Logger.Warn(
                $"BugattiChiron: failed to register vehicle type '{vehicleType.vehicleTypeName}'.");
        }

        BugattiChironAchievementLoadGuard.ResumeIfReady();
        runtime = BugattiChironRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        BugattiChironAchievementLoadGuard.Shutdown();
        runtime?.Shutdown();
        runtime = null;

        if (vehicleType != null)
        {
            BugattiChironLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class BugattiChironCityRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BugattiChironMod.BundleKey);
        var vehicleType = bundle?.LoadAsset<VehicleType>(BugattiChironMod.VehicleAssetPath);
        if (vehicleType != null)
        {
            vehicleType.autoParkSupported = true;
            BugattiChironAchievementLoadGuard.Configure(context, vehicleType);
            BugattiChironVehicleTypeRegistration.EnsureRegistered(vehicleType);
            BugattiChironAchievementLoadGuard.ResumeIfReady();
        }

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class BugattiChironVehicleTypeRegistration
{
    internal static bool EnsureRegistered(VehicleType vehicleType)
    {
        var current = VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName);
        if (current != null)
            return true;

        ModdingAPI.RegisterModVehicleType(vehicleType);
        return VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName) != null;
    }
}

internal static class BugattiChironLuxuryDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "MurrayHillCarDealershipLuxury";

    private static readonly string[] DealerContactIds =
    {
        "The Hamptons Axis",
        "Manhattan Luxury Cars",
    };

    internal static bool IsTargetDealer(string? contactId)
    {
        if (string.IsNullOrEmpty(contactId))
            return false;

        foreach (var dealerContactId in DealerContactIds)
        {
            if (string.Equals(dealerContactId, contactId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static bool EnsureVehicleAvailable(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;

        if (AllDealersContainVehicle(vehicleName))
            return true;

        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        var vanillaStock = GetLuxuryDealerLayoutVehicles();
        if (vanillaStock.Count == 0)
            return false;

        var allDealersReady = true;
        foreach (var dealerContactId in DealerContactIds)
            allDealersReady &= EnsureDealerStock(dealerContactId, vanillaStock, vehicleName);
        return allDealersReady;
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return;

        foreach (var dealerContactId in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                    dealerContactId,
                    out List<string> existingStock) ||
                existingStock == null)
            {
                continue;
            }

            var remainingStock = new List<string>();
            foreach (var existingVehicle in existingStock)
            {
                if (!string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                    AddUnique(remainingStock, existingVehicle);
            }

            if (remainingStock.Count == existingStock.Count)
                continue;
            if (remainingStock.Count == 0)
                ContractItemsForSaleService.RemoveContact(dealerContactId);
            else
                ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, remainingStock);
        }
    }

    private static bool EnsureDealerStock(
        string dealerContactId,
        List<string> vanillaStock,
        string vehicleName)
    {
        var mergedStock = new List<string>();
        var hadExplicitStock = ContractItemsForSaleService.TryGetVehiclesForContact(
            dealerContactId,
            out List<string> existingStock);

        if (hadExplicitStock && existingStock != null)
            AddUniqueRange(mergedStock, existingStock);
        AddUniqueRange(mergedStock, vanillaStock);
        AddUnique(mergedStock, vehicleName);

        if (hadExplicitStock && existingStock != null && SameVehicleList(existingStock, mergedStock))
            return true;

        ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, mergedStock);
        return true;
    }

    private static List<string> GetLuxuryDealerLayoutVehicles()
    {
        var stock = new List<string>();
        try
        {
            var layoutSet = TryGetLuxuryDealerLayoutSet();
            if (layoutSet?.Items == null)
                return stock;

            foreach (var item in layoutSet.Items)
            {
                var purchaserSettings = item?.playerItemPurchaserSettings;
                if (purchaserSettings == null ||
                    !purchaserSettings.enabled ||
                    string.IsNullOrEmpty(purchaserSettings.itemName))
                {
                    continue;
                }

                var itemDefinition = ItemsGetter.GetByName(purchaserSettings.itemName);
                if (itemDefinition != null && !string.IsNullOrEmpty(itemDefinition.vehicleType))
                    AddUnique(stock, itemDefinition.vehicleType);
            }
        }
        catch
        {
            // Layout data is transient while a save is loading. The runtime retries.
        }

        return stock;
    }

    private static BusinessLayoutSet? TryGetLuxuryDealerLayoutSet()
    {
        if (BusinessLayoutSetHelper.loadingLayouts)
            return null;

        return BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
            TargetBusinessTypeName,
            new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
            TargetLayoutName.ToLowerInvariant(),
            false);
    }

    private static bool AllDealersContainVehicle(string vehicleName)
    {
        foreach (var dealerContactId in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                    dealerContactId,
                    out List<string> existingStock) ||
                existingStock == null ||
                !ContainsVehicle(existingStock, vehicleName))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
    {
        foreach (var existingVehicle in stock)
        {
            if (string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
            AddUnique(target, value);
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        foreach (var existing in target)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal))
                return;
        }

        target.Add(value);
    }

    private static bool SameVehicleList(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
