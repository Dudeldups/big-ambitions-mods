#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using Helpers;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(TaxiBang.TaxiMod))]

namespace TaxiBang
{
    [ModEntryOnInitializationLoad]
    public sealed class TaxiMod : IModBigAmbitions
    {
        private const string BundleKey = "AssetBundles/taxi.unity3d";
        private const string VehicleAssetPath = "Assets/Mods/Taxi!/Taxi.asset";

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        private VehicleType? vehicleType;
        private TaxiRuntime? runtime;

        public Task OnLoadAsync(ModContext context)
        {
            TaxiDiagnostics.Info(context, $"Taxi!: loading. fileLog={TaxiDiagnostics.LogPath}");

            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
            if (bundle == null)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: failed to load bundle '{BundleKey}'.");
                return Task.CompletedTask;
            }

            TaxiDiagnostics.Info(context, $"Taxi!: loaded bundle '{BundleKey}'.");
            vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
            if (vehicleType == null)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: failed to load vehicle type '{VehicleAssetPath}'.");
                return Task.CompletedTask;
            }

            ModdingAPI.RegisterModVehicleType(vehicleType);
            runtime = TaxiRuntime.Initialize(context, vehicleType.vehicleTypeName);
            TaxiDiagnostics.Info(context, $"Taxi!: registered vehicle type '{vehicleType.vehicleTypeName}'. Press F9 in-game to spawn a test taxi.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;

            if (vehicleType == null)
                return Task.CompletedTask;

            TaxiDiagnostics.Info(null, $"Taxi!: unloading vehicle type '{vehicleType.vehicleTypeName}'.");
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            return Task.CompletedTask;
        }
    }

    public sealed class TaxiRuntime : MonoBehaviour
    {
        private const bool DebugVehicleSpawnEnabled = true;
        private const float SpawnDistance = 6f;

        private ModContext? context;
        private string vehicleTypeName = string.Empty;
        private int spawnAttempt;

        public static TaxiRuntime Initialize(ModContext context, string vehicleTypeName)
        {
            var runtime = FindObjectOfType<TaxiRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(TaxiRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<TaxiRuntime>();
            }

            runtime.context = context;
            runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
            TaxiDiagnostics.Info(context, $"Taxi!: runtime initialized. vehicleTypeName='{runtime.vehicleTypeName}' debugSpawn={DebugVehicleSpawnEnabled}.");
            return runtime;
        }

        public void Shutdown()
        {
            Destroy(gameObject);
        }

        private void Update()
        {
            if (DebugVehicleSpawnEnabled && Input.GetKeyDown(KeyCode.F9))
            {
                TaxiDiagnostics.Info(context, "Taxi!: F9 detected.");
                TrySpawnVehicleInFrontOfPlayer();
            }
        }

        private void TrySpawnVehicleInFrontOfPlayer()
        {
            try
            {
                spawnAttempt++;
                TaxiDiagnostics.Info(context, $"Taxi!: spawn attempt #{spawnAttempt} started.");

                if (string.IsNullOrWhiteSpace(vehicleTypeName))
                {
                    TaxiDiagnostics.Warn(context, "Taxi!: no vehicle type configured for F9 spawn.");
                    return;
                }

                var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
                if (vehicleType == null)
                {
                    TaxiDiagnostics.Warn(context, $"Taxi!: vehicle type '{vehicleTypeName}' is not registered.");
                    return;
                }

                var playerController = GameManager.Instance?.playerController;
                if (playerController == null)
                {
                    TaxiDiagnostics.Warn(context, "Taxi!: player controller unavailable, cannot spawn vehicle.");
                    return;
                }

                var spawnRotation = playerController.transform.rotation;
                var spawnPosition = playerController.transform.position + playerController.transform.forward * SpawnDistance;
                spawnPosition.y += 0.5f;

                var vehicleInstance = new VehicleInstance(vehicleTypeName)
                {
                    id = CreateVehicleId(),
                    fuel = vehicleType.maxFuel * 0.98f
                };

                TaxiDiagnostics.Info(context, $"Taxi!: calling VehicleHelper.CreateAndSpawnVehicle id='{vehicleInstance.id}' pos={spawnPosition} rot={spawnRotation.eulerAngles} fuel={vehicleInstance.fuel:0.##}.");
                VehicleHelper.CreateAndSpawnVehicle(vehicleInstance, spawnPosition, spawnRotation);
                var snapped = TrySnapSpawnedVehicleToGround(vehicleInstance.id, spawnPosition, spawnRotation);
                TaxiDiagnostics.Info(context, $"Taxi!: spawned '{vehicleTypeName}' with id '{vehicleInstance.id}'. snapped={snapped}.");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Error(context, "spawn", exception);
            }
        }

        private static bool TrySnapSpawnedVehicleToGround(string vehicleId, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return false;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance == null || vehicleController.vehicleInstance.id != vehicleId)
                    continue;

                VehicleHelper.TeleportVehicleToGround(vehicleController, spawnPosition, spawnRotation);
                return true;
            }

            return false;
        }

        private static string CreateVehicleId()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
    }
}
