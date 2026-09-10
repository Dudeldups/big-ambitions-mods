#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

public sealed class BMWM4G82Runtime : MonoBehaviour
{
    private static BMWM4G82Runtime? activeRuntime;
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1775f;
    private const float EnginePowerKw = 375f;
    private const float EngineIdleRpm = 800f;
    private const float EngineLimitRpm = 7200f;
    private const float SpeedLimitKph = 290f;
    private const float FinalDriveRatio = 3.154f;
    private const float EngineInertia = 0.14f;
    private const float EngineStartDuration = 0.55f;
    private const float ClutchEngagementRpm = 1150f;
    private const float ClutchThrottleOffsetRpm = 450f;
    private const float ClutchEngagementRange = 500f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.96f;
    private const float AntiRollBarForce = 7200f;
    private const float FrontSuspensionTravel = 0.05f;
    private const float RearSuspensionTravel = 0.05f;
    private const float SuspensionBumpRate = 24500f;
    private const float SuspensionReboundRate = 28000f;
    private const float SuspensionExtensionSpeed = 4f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private static readonly Vector3 DriverExitPosition = new Vector3(-1.72f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition = new Vector3(1.72f, 0.20f, 0.15f);
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.18f, -0.08f);

    private static readonly float[] M4Gears =
    {
        -3.478f,
        0f,
        5.000f,
        3.200f,
        2.143f,
        1.720f,
        1.313f,
        1.000f,
        0.823f,
        0.640f,
    };

    private static AnimationCurve CreateM4PowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.19f),
            new Keyframe(0.38f, 0.70f),
            new Keyframe(0.66f, 0.92f),
            new Keyframe(0.87f, 1f),
            new Keyframe(1f, 0.90f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? powertrainReadinessCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerReady;
    private bool dealerReadyLogged;

    public static BMWM4G82Runtime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<BMWM4G82Runtime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(BMWM4G82Runtime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<BMWM4G82Runtime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.dealerReady = false;
        runtime.dealerReadyLogged = false;
        activeRuntime = runtime;
        runtime.SubscribeEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        return runtime;
    }

    public void Shutdown()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        initializationCoroutine = null;
        powertrainReadinessCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReady = false;
        if (ReferenceEquals(activeRuntime, this))
            activeRuntime = null;
        Destroy(gameObject);
    }

    internal static bool ConfigureSpawnedVehicle(VehicleController? vehicle)
    {
        return activeRuntime != null &&
               activeRuntime.TryConfigureVehicle(vehicle, "prefab-start");
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
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void UnsubscribeEvents()
    {
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
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
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        initializationCoroutine = null;
        powertrainReadinessCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReady = false;
        dealerReadyLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        if (IsTargetVehicle(vehicle) &&
            !configuredVehicleIds.Contains(vehicle.GetInstanceID()))
        {
            context?.Logger.Warn(
                $"BMWM4G82: vehicle entered before spawn configuration " +
                $"instance={vehicle.GetInstanceID()}; applying fallback.");
        }
        TryConfigureVehicle(vehicle, "vehicle-entered-fallback");
        vehicle?.GetComponent<BMWM4G82GlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle != null && IsTargetVehicle(vehicle))
        {
            if (powertrainReadinessCoroutine != null)
                StopCoroutine(powertrainReadinessCoroutine);
            powertrainReadinessCoroutine = StartCoroutine(EnsureDrivetrainReadyAfterEntry(vehicle));
        }
    }

    private IEnumerator EnsureDrivetrainReadyAfterEntry(VehicleController vehicle)
    {
        // Native entry starts the engine asynchronously. A running flag can be
        // true while the engine is still at zero RPM, which leaves throttle
        // input connected but produces no wheel torque until a later re-entry.
        yield return new WaitForSecondsRealtime(0.25f);
        var physicsVehicle = vehicle == null
            ? null
            : vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>() ??
              vehicle.GetComponentInChildren<NWH.VehiclePhysics2.VehicleController>(true);
        if (physicsVehicle == null)
        {
            context?.Logger.Warn(
                $"BMWM4G82: post-entry drivetrain unavailable vehicle={vehicle?.GetInstanceID()}.");
            powertrainReadinessCoroutine = null;
            yield break;
        }

        var engine = physicsVehicle.powertrain.engine;
        var transmission = physicsVehicle.powertrain.transmission;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear <= 0)
                    transmission.ShiftInto(1, true);
                context?.Logger.Info(
                    $"BMWM4G82: post-entry drivetrain ready vehicle={vehicle.GetInstanceID()} " +
                    $"attempt={attempt} running={engine.IsRunning} rpm={rpm:0} " +
                    $"gear={transmission.Gear}.");
                powertrainReadinessCoroutine = null;
                yield break;
            }

            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(0.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(0.75f);
            if (vehicle != null && vehicle.controlledByPlayer)
                transmission.ShiftInto(1, true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        var finalRpm = engine.RPMPercent * engine.revLimiterRPM;
        context?.Logger.Warn(
            $"BMWM4G82: post-entry drivetrain remained unavailable " +
            $"vehicle={vehicle?.GetInstanceID()} controlled={vehicle?.controlledByPlayer} " +
            $"running={engine.IsRunning} rpm={finalRpm:0} gear={transmission.Gear}.");
        powertrainReadinessCoroutine = null;
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!dealerReady &&
            !BusinessLayoutSetHelper.loadingLayouts &&
            BMWM4G82LuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            EnsureDealerStock("dealer-entered");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen)
        {
            ConfigureExistingVehicles(out _);
            return;
        }
        if (isOpen && !dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
            EnsureDealerStock("full-menu");
    }

    private void HandleVehicleVariablesChanged() => ConfigureExistingVehicles(out _);

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        while (!dealerReady && BusinessLayoutSetHelper.loadingLayouts)
        {
            ConfigureExistingVehicles(out _);
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        var previousMatchedCount = -1;
        var stablePasses = 0;
        var maximumMatchedCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
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
                $"BMWM4G82: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            dealerReady = BMWM4G82LuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            if (dealerReady && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                context?.Logger.Info(
                    $"BMWM4G82: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return dealerReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BMWM4G82: dealer stock update failed source='{source}': " +
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
            TryConfigureVehicle(vehicle, "existing-vehicle-scan");
        }
    }

    private bool TryConfigureVehicle(VehicleController? vehicle, string source)
    {
        if (!IsTargetVehicle(vehicle))
            return false;

        var targetVehicle = vehicle!;
        var instanceId = targetVehicle.GetInstanceID();
        if (!configuredVehicleIds.Add(instanceId))
        {
            targetVehicle.GetComponent<BMWM4G82PaintController>()
                ?.ApplyCurrentColor(source);
            return true;
        }

        try
        {
            var rigidbody = targetVehicle.GetComponent<Rigidbody>() ??
                            targetVehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1.45f;
            }

            ConfigureMassProperties(targetVehicle.gameObject);
            ConfigureWheelControllers(targetVehicle.gameObject);
            ConfigureBodyColliders(targetVehicle.gameObject);
            ConfigureExitMarkers(targetVehicle.gameObject);
            var deformableBodyMeshes = ConfigureVisualDamage(targetVehicle);
            var powertrainConfigured = ConfigurePowertrain(targetVehicle.gameObject);
            var caliperController = targetVehicle.GetComponent<BMWM4G82CaliperController>();
            if (caliperController == null)
                caliperController = targetVehicle.gameObject.AddComponent<BMWM4G82CaliperController>();
            caliperController.Initialize(targetVehicle, context);
            var materialOwner = targetVehicle.GetComponent<BMWM4G82RuntimeMaterialOwner>();
            if (materialOwner == null)
                materialOwner = targetVehicle.gameObject.AddComponent<BMWM4G82RuntimeMaterialOwner>();
            var runtimeMaterialCount = materialOwner.Initialize();
            var materialResult = BMWM4G82Materials.FixSolidMaterials(targetVehicle.gameObject);
            var caliperMaterialCount = BMWM4G82Materials.ApplyCaliperFinish(targetVehicle.gameObject);
            var paintController = targetVehicle.GetComponent<BMWM4G82PaintController>();
            if (paintController == null)
                paintController = targetVehicle.gameObject.AddComponent<BMWM4G82PaintController>();
            paintController.Initialize(targetVehicle, context);
            var glassController = targetVehicle.GetComponent<BMWM4G82GlassController>();
            if (glassController == null)
                glassController = targetVehicle.gameObject.AddComponent<BMWM4G82GlassController>();
            glassController.Initialize(context);
            var lightingController = targetVehicle.GetComponent<BMWM4G82LightingController>();
            if (lightingController == null)
                lightingController = targetVehicle.gameObject.AddComponent<BMWM4G82LightingController>();
            lightingController.Initialize(targetVehicle, context);
            var driverController = targetVehicle.GetComponent<BMWM4G82DriverController>();
            if (driverController == null)
                driverController = targetVehicle.gameObject.AddComponent<BMWM4G82DriverController>();
            driverController.Initialize(targetVehicle, context);
            var audioController = targetVehicle.GetComponent<BMWM4G82AudioController>();
            if (audioController == null)
                audioController = targetVehicle.gameObject.AddComponent<BMWM4G82AudioController>();
            audioController.Initialize(targetVehicle, context);
            var accelerationTelemetry =
                targetVehicle.GetComponent<BMWM4G82AccelerationTelemetry>();
            if (accelerationTelemetry == null)
            {
                accelerationTelemetry = targetVehicle.gameObject
                    .AddComponent<BMWM4G82AccelerationTelemetry>();
            }
            accelerationTelemetry.Initialize(targetVehicle, context);

            context?.Logger.Info(
                $"BMWM4G82: configured vehicle instance={instanceId}, source='{source}', " +
                $"mass={VehicleMass:0}kg, transmission=8-speed-M-Steptronic, awd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=S58-twin-turbo, steeringCalipers=4, " +
                "wheelGeometry=authored-positive-transform-splits, " +
                $"runtimeMaterials={runtimeMaterialCount}, caliperMaterials={caliperMaterialCount}, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialResult.CabinGlassRenderers}/" +
                $"reenabled={materialResult.CabinGlassRenderersReenabled}, " +
                $"rimSlotsNormalized={materialResult.RimSlotsNormalized}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
            return true;
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"BMWM4G82: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool IsTargetVehicle(VehicleController? vehicle)
    {
        return vehicle?.vehicleInstance != null &&
               string.Equals(
                   vehicle.vehicleInstance.vehicleTypeName,
                   vehicleTypeName,
                   StringComparison.Ordinal);
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var isFront = transform.name.StartsWith("Front", StringComparison.Ordinal);
            var isRear = transform.name.StartsWith("Rear", StringComparison.Ordinal);
            if ((!isFront && !isRear) ||
                !transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                continue;

            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(
                    spring,
                    "maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetFloat(spring, "maxForce", isFront ? 19500f : 18500f);
                SetMember(component, "spring", spring);

                var damper = GetMember(component, "damper");
                SetFloat(damper, "bumpRate", SuspensionBumpRate);
                SetFloat(damper, "reboundRate", SuspensionReboundRate);
                SetMember(component, "damper", damper);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.3376f : 0.3395f);
                SetFloat(wheel, "width", isFront ? 0.275f : 0.285f);
                SetMember(component, "wheel", wheel);
                SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
                SetFloat(component, "suspensionExtensionSpeedCoeff", SuspensionExtensionSpeed);
            }
        }
    }

    private static void ConfigureMassProperties(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || GetMember(component, "centerOfMass") is not Vector3)
                continue;
            SetBool(component, "useDefaultCenterOfMass", false);
            SetVector3(component, "centerOfMass", StableCenterOfMass);
            SetVector3(component, "combinedCenterOfMass", StableCenterOfMass);
            SetFloat(component, "baseMass", VehicleMass);
            SetFloat(component, "combinedMass", VehicleMass);
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
                colliders[0].center = new Vector3(0f, 0.33f, 0.08f);
                colliders[0].size = new Vector3(1.74f, 0.42f, 4.38f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.84f, -0.20f);
                colliders[1].size = new Vector3(1.46f, 0.64f, 2.34f);
            }
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
                transform.localPosition = DriverExitPosition;
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
                transform.localPosition = PassengerExitPosition;
        }
    }

    private int ConfigureVisualDamage(VehicleController vehicle)
    {
        foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
                continue;
            component.enabled = false;
            ClearCollection(component, "_deformationQueue");
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"BMWM4G82 damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var filters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null ||
                !IsDeformableExterior(filter))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled)
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"BMWM4G82 damage vehicle={vehicle.GetInstanceID()}: " +
                "deformable outer body mesh is missing; visual damage remains disabled.");
            return 0;
        }

        ClearCollection(damageHandler, "_collisionEvents");
        damageHandler.collisionTimeout = 0.8f;
        damageHandler.damageIntensity = DamageIntensity;
        damageHandler.decelerationThreshold = DamageDecelerationThreshold;
        damageHandler.deformationRadius = DeformationRadius;
        damageHandler.deformationRandomness = DeformationRandomness;
        damageHandler.deformationStrength = DeformationStrength;
        damageHandler.deformationVerticesPerFrame = 8000;
        // The stock deformation controller assumes every mesh shares root-local
        // coordinates and can silently miss imported panels. The model-aware
        // controller below works in world space and only touches the outer shell.
        damageHandler.meshDeform = false;

        var visualDamage = vehicle.GetComponent<BMWM4G82VisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<BMWM4G82VisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        context?.Logger.Info(
            $"BMWM4G82 damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"filters=[{string.Join(", ", filters.ConvertAll(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter.name.StartsWith("BMWDamageBody", StringComparison.Ordinal))
            return true;
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || !BMWM4G82Materials.IsBMWRenderer(renderer.transform))
            return false;
        foreach (var current in filter.GetComponentsInParent<Transform>(true))
            if (current.name.StartsWith("BMWWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
                return false;
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            var name = material.name;
            if (name.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("SeatBelt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                BMWM4G82Materials.IsCabinGlassMaterial(material))
                return false;
            if (name.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Coloured", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Carbon1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Grille", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Badge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("LightA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ManufacturerPlate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Base_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static void ClearCollection(object target, string fieldName)
    {
        var collection = FindField(target.GetType(), fieldName)?.GetValue(target);
        collection?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(collection, null);
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
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateM4PowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0.28f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.11f);
            SetFloat(transmission, "_downshiftRPM", 2400f);
            SetFloat(transmission, "_upshiftRPM", 7000f);
            SetInt(transmission, "forwardGearCount", 8);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", M4Gears);

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
            {
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);
            }

            foreach (var other in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (other != null &&
                    string.Equals(other.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                {
                    var module = GetMember(other, "module");
                    SetFloat(module, "speedLimit", SpeedLimitKph);
                }
            }

            return GetInt(transmission, "forwardGearCount") == 8;
        }

        return false;
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

    private static void SetMember(object? target, string name, object? value)
    {
        if (target == null || value == null)
            return;
        var field = FindField(target.GetType(), name);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }
        var property = FindProperty(target.GetType(), name);
        if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value, null);
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

    private static void SetVector3(object? target, string name, Vector3 value)
    {
        SetValue(target, name, typeof(Vector3), value);
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

[AddComponentMenu("")]
public sealed class BMWM4G82GlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
        new Dictionary<Material, Material>();
    private ModContext? context;
    private Coroutine? restoreCoroutine;
    private bool initialized;

    internal void Initialize(ModContext? modContext)
    {
        context = modContext;
        if (initialized)
        {
            EnsureVisible("reinitialize");
            return;
        }

        cabinGlass.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null ||
                    !BMWM4G82Materials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    BMWM4G82Materials.RestoreCabinGlassMaterial(runtimeMaterial);
                    runtimeMaterials.Add(source, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }
            if (!containsCabinGlass)
                continue;
            renderer.sharedMaterials = materials;
            cabinGlass.Add(renderer);
        }
        initialized = true;
        EnsureVisible("initialize");
    }

    internal void RestoreAfterVehicleEntered()
    {
        if (!initialized)
            return;
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(RestoreAfterEntryLifecycle());
    }

    private IEnumerator RestoreAfterEntryLifecycle()
    {
        // Vehicle entry can alter renderer state after the entry callback. Two
        // deferred event passes restore glass once setup has settled, without a
        // permanent per-frame poll.
        yield return null;
        yield return new WaitForEndOfFrame();
        EnsureVisible("vehicle-entered");
        restoreCoroutine = null;
    }

    private void EnsureVisible(string source)
    {
        var restored = 0;
        var propertyBlocksCleared = 0;
        foreach (var renderer in cabinGlass)
        {
            if (renderer == null)
                continue;
            if (!renderer.enabled || renderer.forceRenderingOff)
                restored++;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            if (renderer.HasPropertyBlock())
            {
                renderer.SetPropertyBlock(null);
                propertyBlocksCleared++;
            }
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null &&
                    BMWM4G82Materials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    BMWM4G82Materials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            context?.Logger.Info(
                $"BMWM4G82 glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            context?.Logger.Info(
                $"BMWM4G82 glass vehicle={GetInstanceID()}: repaired after " +
                $"'{source}' renderers={restored}, propertyBlocks={propertyBlocksCleared}.");
        }
    }

    private void OnDestroy()
    {
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
        foreach (var material in runtimeMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
    }
}

[AddComponentMenu("")]
public sealed class BMWM4G82VisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.36f;
    private const float FrontDepthPerExcessMps = 0.012f;
    private const float RearDentLateralRadius = 0.96f;
    private const float RearDentVerticalRadius = 0.82f;
    private const float RearDentLongitudinalRadius = 1.18f;
    private const float MaximumRearDentDepth = 0.58f;
    private const float RearDepthPerExcessMps = 0.017f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private int diagnosticLogs;
    private bool initialized;
    private bool failureReported;

    internal void Initialize(
        VehicleController controller,
        NWH.VehiclePhysics2.Damage.DamageHandler handler,
        ModContext? modContext,
        IReadOnlyList<MeshFilter> filters,
        float thresholdMps)
    {
        if (initialized && vehicle == controller)
            return;

        vehicle = controller;
        damageHandler = handler;
        context = modContext;
        body = controller.GetComponent<Rigidbody>();
        impactThresholdMps = thresholdMps;
        previousDamage = handler.Damage;
        deformableFilters.Clear();
        originalVertices.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            originalVertices[filter] = runtimeMesh.vertices;
            runtimeMeshes.Add(runtimeMesh);
        }
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || damageHandler == null)
            return;

        var currentDamage = damageHandler.Damage;
        if (previousDamage > 0.001f && currentDamage <= 0.001f)
        {
            foreach (var pair in originalVertices)
            {
                if (pair.Key == null || pair.Key.sharedMesh == null)
                    continue;
                var mesh = pair.Key.sharedMesh;
                mesh.vertices = pair.Value;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }
            context?.Logger.Info(
                $"BMWM4G82 damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
        previousDamage = currentDamage;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
            return;

        try
        {
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var contacts = collision.contacts;
            if (contacts.Length == 0)
                return;

            var excessSpeed = collision.relativeVelocity.magnitude - impactThresholdMps;
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.025f, MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.04f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.04f,
                MaximumRearDentDepth);
            var center = body != null ? body.worldCenterOfMass : transform.position;
            var primaryLocalContact = transform.InverseTransformPoint(contacts[0].point);
            var changedMeshes = 0;
            var changedVertices = 0;
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var meshChanged = false;
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var worldVertex = filter.transform.TransformPoint(vertices[vertexIndex]);
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = dentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontImpact = false;
                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var isEndContact =
                            Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                            Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        var isFrontContact = isEndContact && localContact.z >= 0f;
                        float influence;
                        Vector3 candidateDirection;
                        if (isEndContact)
                        {
                            var localDelta = transform.InverseTransformVector(worldVertex - contact.point);
                            var lateralRadius = isFrontContact
                                ? FrontDentLateralRadius
                                : RearDentLateralRadius;
                            var verticalRadius = isFrontContact
                                ? FrontDentVerticalRadius
                                : RearDentVerticalRadius;
                            var longitudinalRadius = isFrontContact
                                ? FrontDentLongitudinalRadius
                                : RearDentLongitudinalRadius;
                            var normalizedDistance = Mathf.Sqrt(
                                localDelta.x * localDelta.x /
                                (lateralRadius * lateralRadius) +
                                localDelta.y * localDelta.y /
                                (verticalRadius * verticalRadius) +
                                localDelta.z * localDelta.z /
                                (longitudinalRadius * longitudinalRadius));
                            influence = 1f - normalizedDistance;
                            candidateDirection = localContact.z >= 0f
                                ? -transform.forward
                                : transform.forward;
                        }
                        else
                        {
                            influence = 1f - Vector3.Distance(worldVertex, contact.point) / DentRadius;
                            var towardCenter = (center - contact.point).normalized;
                            var contactNormal = contact.normal.normalized;
                            candidateDirection = Vector3.Dot(contactNormal, towardCenter) >= 0f
                                ? contactNormal
                                : -contactNormal;
                        }

                        if (influence <= strongestInfluence)
                            continue;
                        strongestInfluence = influence;
                        inwardDirection = candidateDirection;
                        selectedDepth = isEndContact
                            ? isFrontContact ? frontDentDepth : rearDentDepth
                            : dentDepth;
                        selectedEndImpact = isEndContact;
                        selectedFrontImpact = isFrontContact;
                    }

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    worldVertex += inwardDirection * (selectedDepth * falloff);
                    vertices[vertexIndex] = filter.transform.InverseTransformPoint(worldVertex);
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                changedMeshes++;
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                context?.Logger.Info(
                    $"BMWM4G82 damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
                    $"contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={collision.relativeVelocity.magnitude * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} vertices={changedVertices} " +
                    $"nwhDamage={(damageHandler?.Damage ?? 0f) * 100f:0.0}% " +
                    $"vehicleDamage={(vehicle?.vehicleInstance?.damage ?? 0f) * 100f:0.0}%.");
            }
        }
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn(
                $"BMWM4G82 damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
    }
}
