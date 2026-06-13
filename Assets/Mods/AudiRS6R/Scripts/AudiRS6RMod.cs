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
        var dealerRegistered = TryCallBackAlleyDealer("RegisterVehicle", vehicleType.vehicleTypeName, context);
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
        TryCallBackAlleyDealer("UnregisterVehicle", vehicleType.vehicleTypeName, null);

        return Task.CompletedTask;
    }

    private static bool TryCallBackAlleyDealer(string methodName, string vehicleName, ModContext? context)
    {
        try
        {
            var type = Type.GetType("BackAlleyDealer.BackAlleyDealerInit, BackAlleyDealer");
            var instance = type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var method = type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (instance == null || method == null)
            {
                return false;
            }

            method.Invoke(instance, new object[] { vehicleName });
            return true;
        }
        catch (Exception ex)
        {
            context?.Logger.Warn($"AudiRS6R: BackAlleyDealer integration failed: {ex.Message}");
            return false;
        }
    }
}
