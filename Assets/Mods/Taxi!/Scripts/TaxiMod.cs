#nullable enable
using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.SaveSystem.Legacy;
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
        private string lastSpawnedVehicleId = string.Empty;

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

            if (DebugVehicleSpawnEnabled && Input.GetKeyDown(KeyCode.F10))
            {
                TaxiDiagnostics.Info(context, "Taxi!: F10 detected.");
                ForceActivateLastSpawnedTaxi();
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
                lastSpawnedVehicleId = vehicleInstance.id;
                var snapped = TrySnapSpawnedVehicleToGround(vehicleInstance.id, spawnPosition, spawnRotation);
                TaxiDiagnostics.Info(context, $"Taxi!: spawned '{vehicleTypeName}' with id '{vehicleInstance.id}'. snapped={snapped}.");
                ApplySpawnedVehicleFixes(vehicleInstance.id);
                LogSpawnedVehicleDiagnostics(vehicleInstance.id);
                StartCoroutine(LogSpawnedVehicleDiagnosticsDelayed(vehicleInstance.id));
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Error(context, "spawn", exception);
            }
        }

        private void ForceActivateLastSpawnedTaxi()
        {
            if (string.IsNullOrWhiteSpace(lastSpawnedVehicleId))
            {
                TaxiDiagnostics.Warn(context, "Taxi!: force activate failed; no spawned taxi id is known.");
                return;
            }

            ForceActivateVehicle(lastSpawnedVehicleId, "F10");
            LogSpawnedVehicleDiagnostics(lastSpawnedVehicleId);
            StartCoroutine(LogFocusedDrivabilityDiagnosticsDelayed(lastSpawnedVehicleId, "F10+delay"));
        }

        private void ForceActivateVehicle(string vehicleId, string source)
        {
            var root = FindSpawnedVehicleRoot(vehicleId, out var matchedController, logCollectionState: true);
            if (root == null || matchedController == null)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: force activate from {source} failed; vehicle id '{vehicleId}' was not found.");
                return;
            }

            TaxiDiagnostics.Info(context, $"Taxi!: force activating '{vehicleId}' from {source}.");
            TrySetActiveVehicleId(vehicleId);
            TrySetMember(matchedController, "controlledByPlayer", true);
            TrySetMember(matchedController, "isCheckedForParkingZone", true);
            TrySetMember(matchedController, "isParked", false);
            TrySetMember(matchedController, "parkingState", 0);
            TryInvokeNoArg(matchedController, "EnableVehicle");
            TryInvokeNoArg(matchedController, "EnablePhysics");
            TryInvokeNoArg(matchedController, "StartVehicle");
            TryInvokeNoArg(matchedController, "EnterVehicle");

            var rb = root.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.WakeUp();
                TaxiDiagnostics.Info(context, "Taxi!: force activate set Rigidbody.isKinematic=false and WakeUp().");
            }

            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                if (typeName.IndexOf("NWH.VehiclePhysics2.VehicleController", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("NWH.WheelController3D.WheelController", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    behaviour.enabled = true;
                    TaxiDiagnostics.Info(context, $"Taxi!: force activate enabled '{typeName}' at '{GetPath(behaviour.transform)}'.");
                }
            }

            LogFocusedDrivabilityDiagnostics(root, matchedController, source);
        }

        private IEnumerator LogSpawnedVehicleDiagnosticsDelayed(string vehicleId)
        {
            yield return null;
            yield return new WaitForFixedUpdate();
            TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: delayed post-spawn check for id '{vehicleId}'.");
            ApplySpawnedVehicleFixes(vehicleId);
            LogSpawnedVehicleDiagnostics(vehicleId);
        }

        private IEnumerator LogFocusedDrivabilityDiagnosticsDelayed(string vehicleId, string phase)
        {
            yield return new WaitForSeconds(1f);

            var root = FindSpawnedVehicleRoot(vehicleId, out var matchedController, logCollectionState: true);
            if (root == null || matchedController == null)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: drivability diagnostics '{phase}' failed; vehicle id '{vehicleId}' was not found.");
                yield break;
            }

            LogFocusedDrivabilityDiagnostics(root, matchedController, phase);
        }

        private void ApplySpawnedVehicleFixes(string vehicleId)
        {
            var root = FindSpawnedVehicleRoot(vehicleId, out var matchedController);
            if (root == null)
                return;

            FixParkingHelper(root, matchedController);
            FixVisuals(root);
        }

        private void LogSpawnedVehicleDiagnostics(string vehicleId)
        {
            if (!TaxiDiagnostics.DebugLoggingEnabled)
                return;

            try
            {
                var root = FindSpawnedVehicleRoot(vehicleId, out var matchedController, logCollectionState: true);
                if (root == null || matchedController == null)
                    return;

                var rb = root.GetComponent<Rigidbody>();
                TaxiDiagnostics.Info(
                    context,
                    $"Taxi!: diagnostics: root='{GetPath(root.transform)}' activeSelf={root.activeSelf} activeInHierarchy={root.activeInHierarchy} " +
                    $"baControllerType='{matchedController.GetType().FullName}' controllerEnabled={matchedController.enabled} controllerActive={matchedController.isActiveAndEnabled} tag='{root.tag}' layer={root.layer} " +
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
                LogRendererDiagnostics(root);
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

        private GameObject? FindSpawnedVehicleRoot(string vehicleId, out MonoBehaviour? matchedController, bool logCollectionState = false)
        {
            matchedController = null;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
            {
                if (logCollectionState)
                    TaxiDiagnostics.Warn(context, "Taxi!: diagnostics: VehicleHelper.AllPlayerVehicles is null.");

                return null;
            }

            int count = 0;
            foreach (var vehicleController in allPlayerVehicles)
            {
                count++;
                if (vehicleController?.vehicleInstance?.id == vehicleId)
                    matchedController = vehicleController;
            }

            if (logCollectionState)
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: AllPlayerVehicles count={count}, matched={(matchedController != null)}.");

            return matchedController != null ? matchedController.gameObject : null;
        }

        private void FixParkingHelper(GameObject root, MonoBehaviour? matchedController)
        {
            if (matchedController == null)
                return;

            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "VehicleParkingHelper")
                    continue;

                var field = behaviour.GetType().GetField("_carController", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.GetValue(behaviour) != null)
                    continue;

                try
                {
                    object? controllerValue = matchedController;
                    if (!field.FieldType.IsInstanceOfType(controllerValue))
                        controllerValue = root.GetComponent(field.FieldType);

                    if (controllerValue == null)
                    {
                        TaxiDiagnostics.Warn(context, $"Taxi!: could not fix VehicleParkingHelper._carController; expected type '{field.FieldType.FullName}'.");
                        continue;
                    }

                    field.SetValue(behaviour, controllerValue);
                    TaxiDiagnostics.Info(context, $"Taxi!: fixed VehicleParkingHelper._carController on '{GetPath(behaviour.transform)}' using '{controllerValue.GetType().FullName}'.");
                }
                catch (Exception exception)
                {
                    TaxiDiagnostics.Warn(context, $"Taxi!: failed to fix VehicleParkingHelper._carController: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        private void FixVisuals(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.IndexOf("ShadowCaster", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (transform.gameObject.activeSelf)
                {
                    transform.gameObject.SetActive(false);
                    TaxiDiagnostics.Info(context, $"Taxi!: disabled visual helper '{GetPath(transform)}'.");
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.materials)
                {
                    if (material == null || material.name.IndexOf("Taxi", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", Color.white);

                    if (material.HasProperty("Color_3d0f0cdbe6b74be28a1a5be5bab71dea"))
                        material.SetColor("Color_3d0f0cdbe6b74be28a1a5be5bab71dea", Color.white);

                    if (material.HasProperty("_IgnoreMask"))
                        material.SetFloat("_IgnoreMask", 0f);
                }
            }
        }

        private void LogRendererDiagnostics(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var rendererPath = GetPath(renderer.transform);
                if (rendererPath.IndexOf("/Player/", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var meshFilter = renderer.GetComponent<MeshFilter>();
                var meshName = meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.name : "n/a";
                var materialNames = string.Empty;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (materialNames.Length > 0)
                        materialNames += ",";

                    materialNames += material != null ? $"{material.name}/{(material.shader != null ? material.shader.name : "no-shader")}" : "null";
                }

                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: renderer path='{rendererPath}' enabled={renderer.enabled} active={renderer.gameObject.activeInHierarchy} mesh='{meshName}' materials='{materialNames}'.");
                LogMaterialDiagnostics(renderer);
            }
        }

        private void LogMaterialDiagnostics(Renderer renderer)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.name.IndexOf("Taxi", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: material '{material.name}' shader='{(material.shader != null ? material.shader.name : "no-shader")}'.");
                LogMaterialColor(material, "_BaseColor");
                LogMaterialColor(material, "_Color");
                LogMaterialColor(material, "Color_3d0f0cdbe6b74be28a1a5be5bab71dea");
                LogMaterialFloat(material, "_IgnoreMask");
                LogMaterialTexture(material, "_BaseColorMap");
                LogMaterialTexture(material, "_MainTex");
                LogMaterialTexture(material, "Texture2D_3d0f0cdbe6b74be28a1a5be5bab71dea");
            }
        }

        private void LogMaterialColor(Material material, string propertyName)
        {
            if (!material.HasProperty(propertyName))
                return;

            TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   material.{propertyName}={material.GetColor(propertyName)}");
        }

        private void LogMaterialFloat(Material material, string propertyName)
        {
            if (!material.HasProperty(propertyName))
                return;

            TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   material.{propertyName}={material.GetFloat(propertyName):0.###}");
        }

        private void LogMaterialTexture(Material material, string propertyName)
        {
            if (!material.HasProperty(propertyName))
                return;

            var texture = material.GetTexture(propertyName);
            TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   material.{propertyName}={(texture != null ? texture.name : "null")}");
        }

        private void LogFocusedDrivabilityDiagnostics(GameObject root, MonoBehaviour matchedController, string phase)
        {
            if (!TaxiDiagnostics.DebugLoggingEnabled)
                return;

            try
            {
                TaxiDiagnostics.Info(context, $"Taxi!: drivability diagnostics '{phase}' root='{GetPath(root.transform)}'.");
                LogObjectSnapshot("CarController", matchedController, IsDrivabilityMember);
                LogObjectSnapshot("VehicleInstance", GetMemberValue(matchedController, "vehicleInstance"), IsDrivabilityMember);
                LogObjectSnapshot("VehicleType", GetMemberValue(matchedController, "vehicleType"), IsDrivabilityMember);
                LogObjectSnapshot("FuelModule", GetMemberValue(matchedController, "fuelModule"), IsDrivabilityMember);

                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                        continue;

                    var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                    if (typeName.IndexOf("NWH.VehiclePhysics2.VehicleController", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LogObjectSnapshot("NWH.VehicleController", behaviour, IsDrivabilityMember);
                        LogObjectSnapshot("NWH.VehicleController.input", GetMemberValue(behaviour, "input"), IsDrivabilityMember);
                        var powertrain = GetMemberValue(behaviour, "powertrain");
                        LogObjectSnapshot("NWH.VehicleController.powertrain", powertrain, IsDrivabilityMember);
                        LogObjectSnapshot("NWH.VehicleController.powertrain.engine", GetMemberValue(powertrain, "engine"), IsDrivabilityMember);
                        LogObjectSnapshot("NWH.VehicleController.powertrain.transmission", GetMemberValue(powertrain, "transmission"), IsDrivabilityMember);
                    }

                    if (typeName.IndexOf("FuelModuleWrapper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("SpeedLimiter", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LogObjectSnapshot(typeName, behaviour, IsDrivabilityMember);
                    }
                }
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Error(context, "drivability diagnostics", exception);
            }
        }

        private void LogObjectSnapshot(string label, object? instance, Func<string, bool> memberFilter)
        {
            if (instance == null)
            {
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: {label}=null");
                return;
            }

            var type = instance.GetType();
            TaxiDiagnostics.Info(context, $"Taxi!: diagnostics: {label} type='{type.FullName}'.");
            LogSelectedMembers(label, type, instance, memberFilter);
        }

        private void LogSelectedMembers(string label, Type type, object instance, Func<string, bool> memberFilter)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var field in type.GetFields(Flags))
            {
                if (!memberFilter(field.Name))
                    continue;

                TryLogLabeledMember(label, field.Name, () => field.GetValue(instance));
            }

            foreach (var property in type.GetProperties(Flags))
            {
                if (!memberFilter(property.Name) || property.GetIndexParameters().Length > 0)
                    continue;

                var getter = property.GetGetMethod(true);
                if (getter == null)
                    continue;

                TryLogLabeledMember(label, property.Name, () => property.GetValue(instance, null));
            }
        }

        private void TryLogLabeledMember(string label, string memberName, Func<object?> readValue)
        {
            try
            {
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   {label}.{memberName}={FormatDiagnosticValue(readValue())}");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   {label}.{memberName}=<read failed: {exception.GetType().Name}>");
            }
        }

        private object? GetMemberValue(object? instance, string memberName)
        {
            if (instance == null)
                return null;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();
            var field = type.GetField(memberName, Flags);
            if (field != null)
                return field.GetValue(instance);

            var property = type.GetProperty(memberName, Flags);
            if (property?.CanRead == true && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            return null;
        }

        private void TrySetActiveVehicleId(string vehicleId)
        {
            try
            {
                var saveGameManager = SaveGameManager.Current;
                if (saveGameManager == null)
                {
                    TaxiDiagnostics.Warn(context, "Taxi!: force activate could not set ActiveVehicleId; SaveGameManager.Current is null.");
                    return;
                }

                var type = saveGameManager.GetType();
                if (TrySetMember(saveGameManager, "ActiveVehicleId", vehicleId))
                    return;

                TaxiDiagnostics.Warn(context, $"Taxi!: force activate could not set ActiveVehicleId on '{type.FullName}'.");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: force activate ActiveVehicleId failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private bool TrySetMember(object target, string memberName, object? value)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var type = target.GetType();
                var field = type.GetField(memberName, Flags);
                if (field != null)
                {
                    field.SetValue(target, CoerceValue(value, field.FieldType));
                    TaxiDiagnostics.Info(context, $"Taxi!: force activate set field {type.Name}.{memberName}={value ?? "null"}.");
                    return true;
                }

                var property = type.GetProperty(memberName, Flags);
                if (property?.CanWrite == true && property.GetIndexParameters().Length == 0)
                {
                    property.SetValue(target, CoerceValue(value, property.PropertyType), null);
                    TaxiDiagnostics.Info(context, $"Taxi!: force activate set property {type.Name}.{memberName}={value ?? "null"}.");
                    return true;
                }
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: force activate failed to set {target.GetType().Name}.{memberName}: {exception.GetType().Name}: {exception.Message}");
            }

            return false;
        }

        private void TryInvokeNoArg(object target, string methodName)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var method = target.GetType().GetMethod(methodName, Flags, null, Type.EmptyTypes, null);
                if (method == null)
                    return;

                method.Invoke(target, null);
                TaxiDiagnostics.Info(context, $"Taxi!: force activate invoked {target.GetType().Name}.{methodName}().");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Warn(context, $"Taxi!: force activate failed to invoke {target.GetType().Name}.{methodName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static object? CoerceValue(object? value, Type targetType)
        {
            if (value == null)
                return null;

            var nullableType = Nullable.GetUnderlyingType(targetType);
            var effectiveType = nullableType ?? targetType;
            if (effectiveType.IsInstanceOfType(value))
                return value;

            if (effectiveType.IsEnum)
                return Enum.ToObject(effectiveType, value);

            return Convert.ChangeType(value, effectiveType);
        }

        private static bool IsInterestingComponent(Type type)
        {
            var name = type.FullName ?? type.Name;
            return name.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("CarController", StringComparison.OrdinalIgnoreCase) >= 0
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
                || name.IndexOf("brake", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("broken", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("can", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("control", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("drive", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("engine", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ignition", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("interaction", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("power", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("park", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rpm", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("state", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("throttle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDrivabilityMember(string name)
        {
            return IsInterestingMember(name)
                || name.IndexOf("clutch", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("gear", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("handbrake", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("kinematic", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("limiter", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("motor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("torque", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryLogMember(Type type, string memberName, Func<object?> readValue)
        {
            try
            {
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   {type.Name}.{memberName}={FormatDiagnosticValue(readValue())}");
            }
            catch (Exception exception)
            {
                TaxiDiagnostics.Info(context, $"Taxi!: diagnostics:   {type.Name}.{memberName}=<read failed: {exception.GetType().Name}>");
            }
        }

        private static string FormatDiagnosticValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is UnityEngine.Object unityObject)
                return unityObject != null ? unityObject.name : "null";

            return value.ToString() ?? "null";
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
