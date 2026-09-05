#nullable enable
using System;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
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
        var dealerRegistered = TryRegisterWithBackAlleyDealer(vehicleType.vehicleTypeName, context);
        runtime = AudiRS6RRuntime.Initialize(context, vehicleType.vehicleTypeName, dealerRegistered);
        context.Logger.Info($"AudiRS6R: registered vehicle type '{vehicleType.vehicleTypeName}'.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;

        if (vehicleType == null)
            return Task.CompletedTask;

        ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
        TryUnregisterFromBackAlleyDealer(vehicleType.vehicleTypeName, null);

        return Task.CompletedTask;
    }

    internal static bool TryRegisterWithBackAlleyDealer(string vehicleName, ModContext? context) =>
        TrySyncBackAlleyDealer("RegisterVehicle", vehicleName, context);

    private static bool TryUnregisterFromBackAlleyDealer(string vehicleName, ModContext? context) =>
        TrySyncBackAlleyDealer("UnregisterVehicle", vehicleName, context);

    private static bool TrySyncBackAlleyDealer(string methodName, string vehicleName, ModContext? context)
    {
        try
        {
            var type = FindBackAlleyDealerType();
            if (type == null)
                return false;

            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var instance = type.GetProperty("Instance", staticFlags)?.GetValue(null);
            if (instance == null)
                return false;

            var registrationMethod = type.GetMethod(methodName, instanceFlags);
            if (registrationMethod != null)
            {
                registrationMethod.Invoke(instance, new object[] { vehicleName });
                context?.Logger.Info($"AudiRS6R: BackAlleyDealer synchronized via '{registrationMethod.Name}'.");
                return true;
            }

            // Older published dealer builds discover mod vehicles through a private refresh method
            // instead of exposing RegisterVehicle/UnregisterVehicle.
            var refreshMethod = type.GetMethod("RefreshRegisteredVehicles", instanceFlags) ??
                                type.GetMethod("UpdateModdedVehicles", instanceFlags);
            if (refreshMethod == null)
                return false;

            refreshMethod.Invoke(instance, null);
            context?.Logger.Info($"AudiRS6R: BackAlleyDealer synchronized via compatibility refresh '{refreshMethod.Name}'.");
            return true;
        }
        catch (Exception ex)
        {
            context?.Logger.Warn($"AudiRS6R: BackAlleyDealer integration failed: {ex.Message}");
            return false;
        }
    }

    private static Type? FindBackAlleyDealerType()
    {
        const string fullTypeName = "BackAlleyDealer.BackAlleyDealerInit";
        var type = Type.GetType(fullTypeName + ", BackAlleyDealer");
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullTypeName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }
}
