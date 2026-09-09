#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

public sealed class BugattiChironRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1995f;
    private const float EnginePowerKw = 1103f;
    private const float EngineIdleRpm = 800f;
    private const float EngineLimitRpm = 6700f;
    private const float SpeedLimitKph = 420f;
    private const float FinalDriveRatio = 3.2f;
    private const float EngineInertia = 0.12f;
    private const float EngineStartDuration = 0.5f;
    private const float ClutchEngagementRpm = 1200f;
    private const float ClutchThrottleOffsetRpm = 500f;
    private const float ClutchEngagementRange = 500f;
    private const float ClutchCreepTorque = 0f;
    private const float VehicleLinearDrag = 0.027f;
    private const float ForcedInductionPowerMultiplier = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private const float DamageIntensity = 0.45f;
    private const float DeformationRadius = 0.32f;
    private const float DeformationStrength = 0.22f;

    private static readonly float[] ChironGears =
    {
        -2.96f,
        0f,
        4.10f,
        2.60f,
        1.80f,
        1.35f,
        1.00f,
        0.78f,
        0.62f,
    };

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerReadyLogged;

    public static BugattiChironRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<BugattiChironRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(BugattiChironRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<BugattiChironRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.SubscribeEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        return runtime;
    }

    public void Shutdown()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        Destroy(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeEvents();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeEvents();
    }

    private void SubscribeEvents()
    {
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void UnsubscribeEvents()
    {
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReadyLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle) => TryConfigureVehicle(vehicle);

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (BugattiChironLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            EnsureDealerStock("dealer-entered");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen)
            EnsureDealerStock("full-menu");
    }

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        var dealerReady = false;
        var previousMatchedCount = -1;
        var stablePasses = 0;
        var maximumMatchedCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            dealerReady |= EnsureDealerStock(source);
            ConfigureExistingVehicles(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            if (dealerReady && matchedCount == previousMatchedCount)
                stablePasses++;
            else
                stablePasses = 0;
            previousMatchedCount = matchedCount;

            if (dealerReady && stablePasses >= RequiredStablePasses)
                break;
            if (attempt < InitializationRetryCount)
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"BugattiChiron: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
    }

    private bool EnsureDealerStock(string source)
    {
        try
        {
            var ready = BugattiChironLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                context?.Logger.Info(
                    $"BugattiChiron: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BugattiChiron: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private void ConfigureExistingVehicles(out int matchedCount)
    {
        matchedCount = 0;
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
        {
            if (vehicle?.vehicleInstance == null ||
                !string.Equals(
                    vehicle.vehicleInstance.vehicleTypeName,
                    vehicleTypeName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            matchedCount++;
            TryConfigureVehicle(vehicle);
        }
    }

    private void TryConfigureVehicle(VehicleController? vehicle)
    {
        if (vehicle?.vehicleInstance == null ||
            !string.Equals(
                vehicle.vehicleInstance.vehicleTypeName,
                vehicleTypeName,
                StringComparison.Ordinal))
        {
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (!configuredVehicleIds.Add(instanceId))
            return;

        try
        {
            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = new Vector3(0f, 0.26f, 0f);
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = 1.35f;
            }

            ConfigureWheelControllers(vehicle.gameObject);
            ConfigureBodyColliders(vehicle.gameObject);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var materialResult = BugattiChironMaterials.FixSolidMaterials(vehicle.gameObject);
            var deformableMeshCount = ConfigureVisualDamage(vehicle);
            var lightingController = vehicle.GetComponent<BugattiChironLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<BugattiChironLightingController>();
            lightingController.Initialize(vehicle, context);
            var driverController = vehicle.GetComponent<BugattiChironDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<BugattiChironDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<BugattiChironPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<BugattiChironPaintController>();
            paintController.Initialize(vehicle, context);
            var hornController = vehicle.GetComponent<BugattiChironHornController>();
            if (hornController == null)
                hornController = vehicle.gameObject.AddComponent<BugattiChironHornController>();
            hornController.Initialize(vehicle, context);
            var launchDiagnostics = vehicle.GetComponent<BugattiChironLaunchDiagnostics>();
            if (launchDiagnostics == null)
                launchDiagnostics = vehicle.gameObject.AddComponent<BugattiChironLaunchDiagnostics>();
            launchDiagnostics.Initialize(vehicle, context);
            var damageDiagnostics = vehicle.GetComponent<BugattiChironDamageDiagnostics>();
            if (damageDiagnostics == null)
                damageDiagnostics = vehicle.gameObject.AddComponent<BugattiChironDamageDiagnostics>();
            damageDiagnostics.Initialize(
                vehicle,
                context,
                DamageDecelerationThreshold / 100f,
                deformableMeshCount);

            context?.Logger.Info(
                $"BugattiChiron: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=7-speed-DSG, awd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"linearDrag={VehicleLinearDrag:0.000}, boostMultiplier=" +
                $"{ForcedInductionPowerMultiplier:0.00}, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"deformableBodyMeshes={deformableMeshCount}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"BugattiChiron: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        var positions = new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.7945f, 0.51f, 1.3155f) },
            { "FrontRight_WheelController", new Vector3(0.7945f, 0.51f, 1.3155f) },
            { "RearLeft_WheelController", new Vector3(-0.7505f, 0.51f, -1.3955f) },
            { "RearRight_WheelController", new Vector3(0.7505f, 0.51f, -1.3955f) },
        };

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!positions.TryGetValue(transform.name, out var position))
                continue;

            transform.localPosition = position;
            var isFront = transform.name.StartsWith("Front", StringComparison.Ordinal);
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(spring, "maxLength", 0.18f);
                SetFloat(spring, "maxForce", 22000f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.34f : 0.355f);
                SetFloat(wheel, "width", isFront ? 0.285f : 0.355f);
            }
        }
    }

    private static void ConfigureBodyColliders(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            var colliders = transform.GetComponents<BoxCollider>();
            if (colliders.Length > 0)
            {
                colliders[0].center = new Vector3(0f, 0.43f, 0f);
                colliders[0].size = new Vector3(1.98f, 0.48f, 4.45f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.86f, -0.08f);
                colliders[1].size = new Vector3(1.70f, 0.72f, 2.75f);
            }
        }
    }

    private static bool ConfigurePowertrain(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var powertrain = GetMember(component, "powertrain");
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", ForcedInductionPowerMultiplier);
            SetFloat(forcedInduction, "spoolUpTime", 0.08f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.08f);
            SetFloat(transmission, "_downshiftRPM", 2800f);
            SetFloat(transmission, "_upshiftRPM", 6500f);
            SetInt(transmission, "forwardGearCount", 7);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", ChironGears);

            foreach (var other in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (other != null &&
                    string.Equals(other.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                {
                    var module = GetMember(other, "module");
                    SetFloat(module, "speedLimit", SpeedLimitKph);
                }
            }

            return GetInt(transmission, "forwardGearCount") == 7;
        }

        return false;
    }

    private int ConfigureVisualDamage(VehicleController vehicle)
    {
        var legacyDeformation = vehicle.GetComponentInChildren<VehicleDeformationController>(true);
        if (legacyDeformation != null)
        {
            legacyDeformation.enabled = false;
            ClearCollection(legacyDeformation, "_deformationQueue");
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var deformableFilters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null ||
                !BugattiChironMaterials.IsBugattiRenderer(filter.transform))
            {
                continue;
            }

            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || !HasDeformablePaint(renderer))
                continue;
            deformableFilters.Add(filter);
        }

        var filtersField = FindField(damageHandler.GetType(), "_deformableMeshFilters");
        var originalsField = FindField(damageHandler.GetType(), "_originalMeshes");
        if (!(filtersField?.GetValue(damageHandler) is IList runtimeFilters) ||
            !(originalsField?.GetValue(damageHandler) is IList originalMeshes) ||
            deformableFilters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: " +
                $"could not bind visible body meshes count={deformableFilters.Count}; " +
                "visual damage remains disabled.");
            return 0;
        }

        runtimeFilters.Clear();
        originalMeshes.Clear();
        foreach (var filter in deformableFilters)
        {
            runtimeFilters.Add(filter);
            originalMeshes.Add(filter.sharedMesh);
        }
        ClearCollection(damageHandler, "_collisionEvents");

        damageHandler.collisionTimeout = 0.8f;
        damageHandler.damageIntensity = DamageIntensity;
        damageHandler.decelerationThreshold = DamageDecelerationThreshold;
        damageHandler.deformationRadius = DeformationRadius;
        damageHandler.deformationRandomness = 0.01f;
        damageHandler.deformationStrength = DeformationStrength;
        damageHandler.deformationVerticesPerFrame = 8000;
        damageHandler.meshDeform = true;

        context?.Logger.Info(
            $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: enabled " +
            $"bodyMeshes={deformableFilters.Count} threshold=" +
            $"{DamageDecelerationThreshold / 100f:0.0}mps radius={DeformationRadius:0.00} " +
            $"strength={DeformationStrength:0.00}; legacy unfiltered deformation disabled.");
        return deformableFilters.Count;
    }

    private static bool HasDeformablePaint(Renderer renderer)
    {
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            var name = material.name;
            if (name.IndexOf("BugattiOpaque_04_Body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("BugattiOpaque_06_Darker_Parts", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static void ClearCollection(object target, string fieldName)
    {
        var collection = FindField(target.GetType(), fieldName)?.GetValue(target);
        collection?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(collection, null);
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;

        var field = FindField(target.GetType(), name);
        if (field != null)
            return field.GetValue(target);

        var property = FindProperty(target.GetType(), name);
        return property?.GetValue(target, null);
    }

    private static void SetFloat(object? target, string name, float value)
    {
        SetValue(target, name, typeof(float), value);
    }

    private static void SetInt(object? target, string name, int value)
    {
        if (!SetValue(target, name, typeof(int), value))
        {
            var field = target == null ? null : FindField(target.GetType(), name);
            if (field?.FieldType.IsEnum == true)
                field.SetValue(target, Enum.ToObject(field.FieldType, value));
        }
    }

    private static void SetBool(object? target, string name, bool value)
    {
        SetValue(target, name, typeof(bool), value);
    }

    private static int GetInt(object? target, string name)
    {
        var value = GetMember(target, name);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private static bool SetValue(object? target, string name, Type expectedType, object value)
    {
        if (target == null)
            return false;

        var field = FindField(target.GetType(), name);
        if (field != null && field.FieldType == expectedType)
        {
            field.SetValue(target, value);
            return true;
        }

        var property = FindProperty(target.GetType(), name);
        if (property != null && property.CanWrite && property.PropertyType == expectedType)
        {
            property.SetValue(target, value, null);
            return true;
        }

        return false;
    }

    private static void SetFloatArray(object? target, string name, float[] values)
    {
        if (target == null)
            return;

        var field = FindField(target.GetType(), name);
        if (field == null)
            return;

        if (field.FieldType == typeof(float[]))
        {
            field.SetValue(target, (float[])values.Clone());
            return;
        }

        if (!(field.GetValue(target) is IList list))
            return;
        list.Clear();
        foreach (var value in values)
            list.Add(value);
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null)
                return property;
        }

        return null;
    }
}

[DefaultExecutionOrder(100)]
internal sealed class BugattiChironDamageDiagnostics : MonoBehaviour
{
    private const int MaximumAcceptedLogs = 6;
    private const int MaximumRoadSuppressionLogs = 3;
    private VehicleController? vehicle;
    private ModContext? context;
    private float impactThreshold;
    private float nextLogTime;
    private int deformableMeshCount;
    private int acceptedLogs;
    private int roadSuppressionLogs;

    internal void Initialize(
        VehicleController controller,
        ModContext? modContext,
        float threshold,
        int bodyMeshCount)
    {
        vehicle = controller;
        context = modContext;
        impactThreshold = threshold;
        deformableMeshCount = bodyMeshCount;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || collision == null || collision.relativeVelocity.magnitude < impactThreshold ||
            Time.unscaledTime < nextLogTime)
        {
            return;
        }

        nextLogTime = Time.unscaledTime + 0.8f;
        var other = collision.collider;
        var otherName = other != null ? other.name : "unknown";
        var layerName = other != null ? LayerMask.LayerToName(other.gameObject.layer) : "unknown";
        var speedKph = collision.relativeVelocity.magnitude * 3.6f;
        if (!NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
        {
            if (roadSuppressionLogs++ < MaximumRoadSuppressionLogs)
            {
                context?.Logger.Info(
                    $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: suppressed road/ground " +
                    $"contact='{otherName}' layer='{layerName}' relativeSpeed={speedKph:0.0}kph.");
            }
            return;
        }

        if (acceptedLogs++ < MaximumAcceptedLogs)
        {
            context?.Logger.Info(
                $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: accepted impact " +
                $"contact='{otherName}' layer='{layerName}' relativeSpeed={speedKph:0.0}kph " +
                $"bodyMeshes={deformableMeshCount}.");
        }
    }
}
