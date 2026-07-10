#nullable enable
using System;
using System.Reflection;
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
                LogSpawnedVehicleDiagnostics(vehicleInstance.id);
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Error(context, "spawn", exception);
            }
        }

        private void LogSpawnedVehicleDiagnostics(string vehicleId)
        {
            if (!TaxiDiagnostics.DebugLoggingEnabled)
                return;

            try
            {
                var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
                if (allPlayerVehicles == null)
                {
                    TaxiDiagnostics.Warn(context, "Taxi!: diagnostics: VehicleHelper.AllPlayerVehicles is null.");
                    return;
                }

                int count = 0;
                MonoBehaviour? matchedController = null;
                foreach (var vehicleController in allPlayerVehicles)
                {
                    count++;
                    if (vehicleController?.vehicleInstance?.id == vehicleId)
                        matchedController = vehicleController;
                }

                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: AllPlayerVehicles count={count}, matched={(matchedController != null)}.");
                if (matchedController == null)
                    return;

                var root = matchedController.gameObject;
                var rb = root.GetComponent<Rigidbody>();
                TaxiDiagnostics.Info(
                    context,
                    $"Taxi!: diagnostics: root='{GetPath(root.transform)}' activeSelf={root.activeSelf} activeInHierarchy={root.activeInHierarchy} " +
                    $"controllerEnabled={matchedController.enabled} controllerActive={matchedController.isActiveAndEnabled} tag='{root.tag}' layer={root.layer} " +
                    $"pos={root.transform.position} rot={root.transform.rotation.eulerAngles} rigidbody={(rb != null)} kinematic={(rb != null && rb.isKinematic)}.");

                var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                int missingBehaviours = 0;
                int loggedBehaviours = 0;
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null)
                    {
                        missingBehaviours++;
                        continue;
                    }

                    var type = behaviour.GetType();
                    if (!IsInterestingComponent(type))
                        continue;

                    loggedBehaviours++;
                    TaxiDiagnostics.Info(
                        context,
                        $"Taxi!: diagnostics: component path='{GetPath(behaviour.transform)}' type='{type.FullName}' enabled={behaviour.enabled} active={behaviour.isActiveAndEnabled}.");
                    LogInterestingMembers(type, behaviour);
                }

                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: behaviours={behaviours.Length}, missingBehaviours={missingBehaviours}, loggedInteresting={loggedBehaviours}.");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Error(context, "diagnostics", exception);
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

        private static bool IsInterestingComponent(Type type)
        {
            var name = type.FullName ?? type.Name;
            return name.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Powertrain", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Interaction", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Ignition", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogInterestingMembers(Type type, object instance)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var field in type.GetFields(Flags))
            {
                if (!IsInterestingMember(field.Name))
                    continue;

                TryLogMember(type, field.Name, () => field.GetValue(instance));
            }

            foreach (var property in type.GetProperties(Flags))
            {
                if (!IsInterestingMember(property.Name) || property.GetIndexParameters().Length > 0)
                    continue;

                var getter = property.GetGetMethod(true);
                if (getter == null)
                    continue;

                TryLogMember(type, property.Name, () => property.GetValue(instance, null));
            }
        }

        private static bool IsInterestingMember(string name)
        {
            return name.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("control", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("drive", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("engine", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ignition", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("interaction", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("power", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("throttle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryLogMember(Type type, string memberName, Func<object?> readValue)
        {
            try
            {
                var value = readValue();
                if (value is UnityEngine.Object unityObject)
                    value = unityObject != null ? unityObject.name : "null";

                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   {type.Name}.{memberName}={value ?? "null"}");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   {type.Name}.{memberName}=<read failed: {exception.GetType().Name}>");
            }
        }

        private static string GetPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
