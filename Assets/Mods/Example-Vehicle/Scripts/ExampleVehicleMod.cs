#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BackAlleyDealer;
using BAModAPI;
using BAModAPI.Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(ExampleVehicleMod))]

[ModEntryOnInitializationLoad]
public class ExampleVehicleMod : IModBigAmbitions
{
    private const string BundleKey = "AssetBundles/example-vehicle.unity3d";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private readonly List<VehicleType> registeredVehicleTypes = new();
    private ExampleVehicleRuntime? runtime;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            Debug.LogError($"ExampleVehicleMod: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        var vehicleTypes = bundle.LoadAllAssets<VehicleType>();
        if (vehicleTypes == null || vehicleTypes.Length == 0)
        {
            Debug.LogError("ExampleVehicleMod: failed to load any vehicle types from the asset bundle.");
            return Task.CompletedTask;
        }

        foreach (var vehicleType in vehicleTypes)
        {
            if (vehicleType == null || string.IsNullOrWhiteSpace(vehicleType.vehicleTypeName))
                continue;

            ModdingAPI.RegisterModVehicleType(vehicleType);
            registeredVehicleTypes.Add(vehicleType);
        }

        if (registeredVehicleTypes.Count == 0)
        {
            Debug.LogError("ExampleVehicleMod: the asset bundle contained no valid vehicle types to register.");
            return Task.CompletedTask;
        }

        runtime = ExampleVehicleRuntime.Initialize(context, registeredVehicleTypes);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;

        foreach (var vehicleType in registeredVehicleTypes)
        {
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            BackAlleyDealerInit.Instance?.UnregisterVehicle(vehicleType.vehicleTypeName);
        }

        registeredVehicleTypes.Clear();

        return Task.CompletedTask;
    }
}
