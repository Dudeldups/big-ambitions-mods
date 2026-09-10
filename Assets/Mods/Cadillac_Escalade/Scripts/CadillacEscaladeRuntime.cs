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

public sealed class CadillacEscaladeRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 2575f;
    private const float EnginePowerKw = 301f;
    private const float EngineIdleRpm = 600f;
    private const float EngineLimitRpm = 6200f;
    private const float SpeedLimitKph = 171f;
    private const float FinalDriveRatio = 3.42f;
    private const float EngineInertia = 0.32f;
    private const float EngineStartDuration = 0.80f;
    private const float ClutchEngagementRpm = 1200f;
    private const float ClutchThrottleOffsetRpm = 500f;
    private const float ClutchEngagementRange = 500f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.90f;
    private const float AntiRollBarForce = 10500f;
    private const float FrontSuspensionTravel = 0.15f;
    private const float RearSuspensionTravel = 0.15f;
    private const float DeformationStrength = 0.13f;
    private const float DeformationRadius = 0.45f;
    private const float DeformationRandomness = 0.012f;
    private const float DamageIntensity = 0.75f;
    private const float DamageDecelerationThreshold = 300f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.22f, -0.10f);
    private static readonly Vector3 LowerColliderCenter = new Vector3(0f, 0.30f, -0.05f);
    private static readonly Vector3 LowerColliderSize = new Vector3(1.94f, 0.50f, 5.12f);
    private static readonly Vector3 UpperColliderCenter = new Vector3(0f, 0.78f, -0.22f);
    private static readonly Vector3 UpperColliderSize = new Vector3(1.72f, 0.74f, 3.42f);

    private static readonly float[] EscaladeGears =
    {
        -3.06f,
        0f,
        4.03f,
        2.36f,
        1.53f,
        1.15f,
        0.85f,
        0.67f,
    };

    private static AnimationCurve CreateEscaladePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0.20f),
            new Keyframe(0.35f, 0.55f),
            new Keyframe(0.62f, 0.86f),
            new Keyframe(0.82f, 0.97f),
            new Keyframe(0.92f, 1f),
            new Keyframe(1f, 0.88f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? powertrainReadinessCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerReady;
    private bool dealerReadyLogged;
    private int cachedPlayerVehicleCount = -1;
    private int cachedTargetVehicleCount;

    public static CadillacEscaladeRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<CadillacEscaladeRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(CadillacEscaladeRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<CadillacEscaladeRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.ResetPlayerVehicleSnapshot();
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
        ResetPlayerVehicleSnapshot();
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

    private void Update()
    {
        // Dealer purchases do not raise onEnterVehicle. Keep the hot path to an
        // allocation-free integer comparison and enumerate only after the
        // small player-vehicle collection changes.
        var playerVehicleCount = VehicleHelper.AllPlayerVehicles?.Count ?? 0;
        if (playerVehicleCount != cachedPlayerVehicleCount)
            ConfigurePlayerVehiclesIfChanged(out _);
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
        ResetPlayerVehicleSnapshot();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        ResetPlayerVehicleSnapshot();
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
        ResetPlayerVehicleSnapshot();
        dealerReady = false;
        dealerReadyLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle);
        if (!IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<CadillacEscaladePaintController>()?.RestoreAfterVehicleEntered();
        vehicle.GetComponent<CadillacEscaladeGlassController>()?.RestoreAfterVehicleEntered();
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        powertrainReadinessCoroutine = StartCoroutine(EnsurePowertrainReadyAfterEntry(vehicle));
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(
             vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName,
             StringComparison.Ordinal) ||
         string.Equals(
             vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName,
             StringComparison.Ordinal));

    private IEnumerator EnsurePowertrainReadyAfterEntry(VehicleController vehicle)
    {
        // Native entry starts the engine asynchronously. Give that lifecycle a
        // short grace period, then perform a bounded clean restart only when it
        // remained dormant (the reported failure had zero RPM in first gear).
        yield return new WaitForSecondsRealtime(0.25f);
        var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (physics == null)
        {
            context?.Logger.Warn(
                $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                "entry readiness could not find the NWH controller.");
            powertrainReadinessCoroutine = null;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (!vehicle.controlledByPlayer)
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear <= 0)
                    transmission.ShiftInto(1, true);
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                    $"entry ready attempt={attempt}, running={engine.IsRunning}, " +
                    $"rpm={rpm:0}, gear={transmission.Gear}.");
                powertrainReadinessCoroutine = null;
                yield break;
            }

            if (attempt == 1)
            {
                context?.Logger.Warn(
                    $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                    $"dormant after entry; beginning bounded restart, running={engine.IsRunning}, " +
                    $"ignition={engine.ignition}, canRun={engine.canRun}, rpm={rpm:0}, " +
                    $"gear={transmission.Gear}.");
            }
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(0.15f);
            if (!vehicle.controlledByPlayer)
                break;
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(0.90f);
            if (vehicle.controlledByPlayer)
                transmission.ShiftInto(1, true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        var finalRpm = engine.RPMPercent * engine.revLimiterRPM;
        context?.Logger.Warn(
            $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
            $"entry readiness ended without a healthy engine, controlled={vehicle.controlledByPlayer}, " +
            $"running={engine.IsRunning}, ignition={engine.ignition}, canRun={engine.canRun}, " +
            $"rpm={finalRpm:0}, gear={transmission.Gear}.");
        powertrainReadinessCoroutine = null;
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (CadillacEscaladeLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            ScheduleInitialization("dealer-entered");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen)
            ScheduleInitialization("full-menu");
    }

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        var previousMatchedCount = -1;
        var stablePasses = 0;
        var maximumMatchedCount = 0;

        while (BusinessLayoutSetHelper.loadingLayouts)
        {
            ConfigurePlayerVehiclesIfChanged(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            ConfigurePlayerVehiclesIfChanged(out var matchedCount);
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
                $"CadillacEscalade: luxury dealer stock not ready source='{source}', " +
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
            var ready = CadillacEscaladeLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"CadillacEscalade: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private void ResetPlayerVehicleSnapshot()
    {
        cachedPlayerVehicleCount = -1;
        cachedTargetVehicleCount = 0;
    }

    private void ConfigurePlayerVehiclesIfChanged(out int matchedCount)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var playerVehicleCount = vehicles?.Count ?? 0;
        if (playerVehicleCount == cachedPlayerVehicleCount)
        {
            matchedCount = cachedTargetVehicleCount;
            return;
        }

        cachedPlayerVehicleCount = playerVehicleCount;
        matchedCount = 0;
        if (vehicles == null)
        {
            cachedTargetVehicleCount = 0;
            return;
        }

        foreach (var vehicle in vehicles)
        {
            if (!IsTargetVehicle(vehicle))
                continue;

            matchedCount++;
            TryConfigureVehicle(vehicle);
        }
        cachedTargetVehicleCount = matchedCount;
    }

    private void TryConfigureVehicle(VehicleController? vehicle)
    {
        if (!IsTargetVehicle(vehicle) || vehicle == null)
            return;

        if (vehicle.vehicleInstance == null)
        {
            ConfigurePresentationOnly(vehicle);
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
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1.45f;
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            const int bakedPositiveWheelMeshes = 8;
            ConfigureBodyColliders(vehicle.gameObject);
            ConfigureExitMarkers(vehicle.gameObject);
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<CadillacEscaladeCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<CadillacEscaladeCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialResult = CadillacEscaladeMaterials.FixSolidMaterials(vehicle.gameObject);
            var lightingController = vehicle.GetComponent<CadillacEscaladeLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<CadillacEscaladeLightingController>();
            lightingController.Initialize(vehicle, context);
            var driverController = vehicle.GetComponent<CadillacEscaladeDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<CadillacEscaladeDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<CadillacEscaladePaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<CadillacEscaladePaintController>();
            paintController.Initialize(vehicle, context);
            var glassController = vehicle.GetComponent<CadillacEscaladeGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<CadillacEscaladeGlassController>();
            glassController.Initialize(context);
            var audioController = vehicle.GetComponent<CadillacEscaladeAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<CadillacEscaladeAudioController>();
            audioController.Initialize(vehicle, context);
            var accelerationTelemetry =
                vehicle.GetComponent<CadillacEscaladeAccelerationTelemetry>();
            if (accelerationTelemetry == null)
            {
                accelerationTelemetry = vehicle.gameObject
                    .AddComponent<CadillacEscaladeAccelerationTelemetry>();
            }
            accelerationTelemetry.Initialize(vehicle, context);

            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=6-speed-automatic, awd=40:60, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=2012-L92-calibration, steeringCalipers=4, " +
                $"bakedPositiveWheelMeshes={bakedPositiveWheelMeshes}, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialResult.CabinGlassRenderers}/" +
                $"reenabled={materialResult.CabinGlassRenderersReenabled}, " +
                $"rimSlotsNormalized={materialResult.RimSlotsNormalized}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"CadillacEscalade: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ConfigurePresentationOnly(VehicleController vehicle)
    {
        CadillacEscaladeMaterials.FixSolidMaterials(vehicle.gameObject);
        var paintController = vehicle.GetComponent<CadillacEscaladePaintController>();
        if (paintController == null)
        {
            paintController = vehicle.gameObject.AddComponent<CadillacEscaladePaintController>();
            paintController.Initialize(vehicle, context);
        }
        var glassController = vehicle.GetComponent<CadillacEscaladeGlassController>();
        if (glassController == null)
        {
            glassController = vehicle.gameObject.AddComponent<CadillacEscaladeGlassController>();
            glassController.Initialize(context);
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        SetLocalPosition(root, "Animate_SteeringWheel_033", new Vector3(-0.52f, 1.15f, 0.68f));
        SetLocalPosition(root, "Driverside", new Vector3(-1.75f, 0.10f, 0.30f));
        SetLocalPosition(root, "Passengerside", new Vector3(1.75f, 0.10f, 0.30f));
    }

    private static void SetLocalPosition(GameObject root, string childName, Vector3 position)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, childName, StringComparison.Ordinal))
                continue;
            transform.localPosition = position;
            return;
        }
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
                SetFloat(spring, "maxForce", 26000f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", 0.408f);
                SetFloat(wheel, "width", 0.285f);
                SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
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
                colliders[0].center = LowerColliderCenter;
                colliders[0].size = LowerColliderSize;
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = UpperColliderCenter;
                colliders[1].size = UpperColliderSize;
            }
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
                $"CadillacEscalade damage vehicle={vehicle.GetInstanceID()}: " +
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
                $"CadillacEscalade damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<CadillacEscaladeVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<CadillacEscaladeVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"filters=[{string.Join(", ", filters.ConvertAll(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (name.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("glass_windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("_int_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.StartsWith("CadillacWheel", StringComparison.Ordinal) ||
            name.StartsWith("CadillacBrake", StringComparison.Ordinal) ||
            name.StartsWith("CadillacFixedCaliper", StringComparison.Ordinal) ||
            name.StartsWith("polySurface", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.StartsWith("CadillacDamageBody", StringComparison.Ordinal) ||
               ContainsAny(name,
                   "combined_mesh", "blackout", "black_smooth", "smooth_plastics",
                   "painted_black", "panited_gloss", "misc_primer", "bright_chrome",
                   "galvano", "stainless_steel", "steel_cast", "clear_plastics",
                   "LED_Light_Pipe", "rubber",
                   "tail_lamp", "rear_etchings", "rear_turn_signals", "chml",
                   "reflectorGlass", "running_headlight", "running_facia_lamps",
                   "high_beams", "headlights_etched", "headlight_metals",
                   "etches_light", "front_emblem", "rear_emblem", "chrome_badges",
                   "Grille", "Kit2_Coloured", "Light_Geo", "ManufacturerPlate");
    }

    private static bool ContainsAny(string value, params string[] markers)
    {
        foreach (var marker in markers)
            if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
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
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateEscaladePowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", false);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.35f);
            SetFloat(transmission, "_downshiftRPM", 1800f);
            SetFloat(transmission, "_upshiftRPM", 5900f);
            SetInt(transmission, "forwardGearCount", 6);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", EscaladeGears);

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

            return GetInt(transmission, "forwardGearCount") == 6;
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
public sealed class CadillacEscaladeGlassController : MonoBehaviour
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
                    !CadillacEscaladeMaterials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    CadillacEscaladeMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    CadillacEscaladeMaterials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    CadillacEscaladeMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, tint=(0.82,0.88,0.94,0.28), deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class CadillacEscaladeVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.82f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.016f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.36f;
    private const float FrontDepthPerExcessMps = 0.012f;
    private const float RearDentLateralRadius = 0.90f;
    private const float RearDentVerticalRadius = 0.72f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.40f;
    private const float RearDepthPerExcessMps = 0.012f;
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
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
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
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
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
                $"CadillacEscalade damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
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
