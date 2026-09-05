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

[assembly: RegisterModClass(typeof(AudiRS6RMod))]

[ModEntryOnInitializationLoad]
public class AudiRS6RMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/audirs6r.unity3d";
    private const string VehicleAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private VehicleType? vehicleType;
    private AudiRS6RRuntime? runtime;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"AudiRS6R: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn($"AudiRS6R: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        ModdingAPI.RegisterModVehicleType(vehicleType);
        runtime = AudiRS6RRuntime.Initialize(context, vehicleType.vehicleTypeName);
        context.Logger.Info($"AudiRS6R: registered vehicle type '{vehicleType.vehicleTypeName}'.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;

        if (vehicleType == null)
            return Task.CompletedTask;

        AudiRS6RLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
        ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);

        return Task.CompletedTask;
    }
}

internal static class AudiRS6RLuxuryDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "MurrayHillCarDealershipLuxury";
    private const string TargetLayoutKey =
        "ba:businesstype_cardealership|ba:buildingsize_m|1|murrayhillcardealershipluxury";

    private static readonly string[] DealerContactIds =
    {
        "The Hamptons Axis",
        "Manhattan Luxury Cars"
    };

    internal static bool EnsureVehicleAvailable(string vehicleName, ModContext? context)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;

        var vanillaStock = GetLuxuryDealerLayoutVehicles();
        if (vanillaStock.Count == 0)
            return false;

        var allDealersReady = true;
        foreach (var dealerContactId in DealerContactIds)
            allDealersReady &= EnsureDealerStock(dealerContactId, vanillaStock, vehicleName, context);

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
        string vehicleName,
        ModContext? context)
    {
        try
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
            context?.Logger.Info(
                $"AudiRS6R: added '{vehicleName}' to '{dealerContactId}' vehicle catalog ({mergedStock.Count} entries).");
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"AudiRS6R: could not update '{dealerContactId}' vehicle catalog: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
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
                if (itemDefinition == null || string.IsNullOrEmpty(itemDefinition.vehicleType))
                    continue;

                AddUnique(stock, itemDefinition.vehicleType);
            }
        }
        catch
        {
            // The layout may not be loaded while transitioning between the menu and a save.
            // The persistent runtime retries after the game finishes loading.
        }

        return stock;
    }

    private static BusinessLayoutSet? TryGetLuxuryDealerLayoutSet()
    {
        var layoutSets = BusinessLayoutSetHelper.GetAllBusinessLayoutSets();
        if (layoutSets != null && layoutSets.TryGetValue(TargetLayoutKey, out var layoutSet))
            return layoutSet;

        return BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
            TargetBusinessTypeName,
            new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
            TargetLayoutName.ToLowerInvariant(),
            false);
    }

    private static void AddUniqueRange(List<string> stock, IEnumerable<string> vehicles)
    {
        foreach (var vehicle in vehicles)
            AddUnique(stock, vehicle);
    }

    private static void AddUnique(List<string> stock, string vehicleName)
    {
        if (string.IsNullOrEmpty(vehicleName))
            return;

        foreach (var existingVehicle in stock)
        {
            if (string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                return;
        }

        stock.Add(vehicleName);
    }

    private static bool SameVehicleList(List<string> existingStock, List<string> desiredStock)
    {
        if (existingStock.Count != desiredStock.Count)
            return false;

        for (var i = 0; i < existingStock.Count; i++)
        {
            if (!string.Equals(existingStock[i], desiredStock[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
