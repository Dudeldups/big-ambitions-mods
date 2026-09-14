#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

public sealed class KoenigseggJeskoRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1420f;
    private const float EnginePowerKw = 760f;
    private const float EngineIdleRpm = 900f;
    private const float EngineLimitRpm = 8500f;
    private const float SpeedLimitKph = 480f;
    private const float FinalDriveRatio = 3.25f;
    private const float EngineInertia = 0.09f;
    private const float EngineStartDuration = 0.42f;
    private const float ClutchEngagementRpm = 1400f;
    private const float ClutchThrottleOffsetRpm = 700f;
    private const float ClutchEngagementRange = 650f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.92f;
    private const float AntiRollBarForce = 7800f;
    private const float FrontSuspensionTravel = 0.08f;
    private const float RearSuspensionTravel = 0.06f;
    private const float DeformationStrength = 0.20f;
    private const float DeformationRadius = 0.22f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.10f, -0.08f);
    private static readonly Dictionary<string, Vector3> WheelPlacementOverrides =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.8057338f, 0.347f, 1.3343513f) },
            { "FrontRight_WheelController", new Vector3(0.8059794f, 0.347f, 1.3343511f) },
            { "RearLeft_WheelController", new Vector3(-0.76666033f, 0.331f, -1.3089924f) },
            { "RearRight_WheelController", new Vector3(0.7669054f, 0.331f, -1.3089926f) },
            { "KoenigseggWheelFrontLeft", new Vector3(-0.8057338f, 0.347f, 1.3343513f) },
            { "KoenigseggWheelFrontRight", new Vector3(0.8059794f, 0.347f, 1.3343511f) },
            { "KoenigseggWheelRearLeft", new Vector3(-0.76666033f, 0.331f, -1.3089924f) },
            { "KoenigseggWheelRearRight", new Vector3(0.7669054f, 0.331f, -1.3089926f) },
            { "KoenigseggFixedCaliperFrontLeft", new Vector3(-0.8057338f, 0.347f, 1.3343513f) },
            { "KoenigseggFixedCaliperFrontRight", new Vector3(0.8059794f, 0.347f, 1.3343511f) },
            { "KoenigseggFixedCaliperRearLeft", new Vector3(-0.76666033f, 0.331f, -1.3089924f) },
            { "KoenigseggFixedCaliperRearRight", new Vector3(0.7669054f, 0.331f, -1.3089926f) },
        };

    private static readonly float[] JeskoGears =
    {
        -3.00f,
        0f,
        3.20f,
        2.15f,
        1.55f,
        1.18f,
        0.94f,
        0.78f,
        0.66f,
        0.57f,
        0.49f,
    };

    private static AnimationCurve CreateJeskoPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.20f, 0.14f),
            new Keyframe(0.48f, 0.34f),
            new Keyframe(0.76f, 0.72f),
            new Keyframe(0.90f, 1f),
            new Keyframe(1f, 0.94f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private GameObject? playerVehiclePrefab;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerReadyLogged;
    private bool dealerReady;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;

    public static KoenigseggJeskoRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<KoenigseggJeskoRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(KoenigseggJeskoRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<KoenigseggJeskoRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        KoenigseggJeskoPrivateDriverSupport.SetContext(context);
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
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        KoenigseggJeskoPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
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
        // Dealer purchases do not raise onEnterVehicle. Keep the hot path to
        // one count comparison and enumerate only after the collection changes.
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var vehicleCount = vehicles?.Count ?? 0;
        if (vehicleCount == cachedPlayerVehicleCount)
            return;
        cachedPlayerVehicleCount = vehicleCount;
        ConfigureExistingVehicles(out _);
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
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        cachedPlayerVehicleCount = -1;
        dealerReadyLogged = false;
        dealerReady = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle);
        vehicle?.GetComponent<KoenigseggJeskoGlassController>()
            ?.RestoreAfterVehicleEntered();
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!dealerReady &&
            !BusinessLayoutSetHelper.loadingLayouts &&
            KoenigseggJeskoLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            EnsureDealerStock("dealer-entered");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen && !dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
            EnsureDealerStock("full-menu");
        if (isOpen && privateDriverRegistrationAllowed &&
            (!privateDriverReady || !privateDriverPoolReady))
            EnsurePrivateDriverSupport("full-menu");
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

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!privateDriverPoolReady && playerVehiclePrefab != null)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);
            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            if (privateDriverRegistrationAllowed && !privateDriverReady)
                EnsurePrivateDriverSupport(source);
            ConfigureExistingVehicles(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            var servicesReady = dealerReady && privateDriverPoolReady &&
                                (!privateDriverRegistrationAllowed || privateDriverReady);
            if (servicesReady && matchedCount == previousMatchedCount)
                stablePasses++;
            else
                stablePasses = 0;
            previousMatchedCount = matchedCount;

            if (servicesReady && stablePasses >= RequiredStablePasses)
                break;
            if (attempt < InitializationRetryCount)
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"KoenigseggJesko: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
            context?.Logger.Warn(
                $"KoenigseggJesko: private-driver contracts not ready source='{source}'.");
        if (!privateDriverPoolReady)
            context?.Logger.Warn(
                $"KoenigseggJesko: private-driver traffic pool not ready source='{source}'.");
    }

    private bool EnsurePrivateDriverSupport(string source)
    {
        if (privateDriverReady && privateDriverPoolReady)
            return true;
        if (!privateDriverRegistrationAllowed || playerVehiclePrefab == null)
            return false;

        try
        {
            if (!privateDriverPoolReady)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);
            if (!privateDriverReady)
            {
                privateDriverReady =
                    KoenigseggJeskoPrivateDriverSupport.EnsureVehicleAvailable(
                        vehicleTypeName);
            }
            if (privateDriverReady)
                context?.Logger.Info(
                    $"KoenigseggJesko: private-driver support registered source='{source}'.");
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"KoenigseggJesko: private-driver registration failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool TryPreparePrivateDriverPool(string source)
    {
        if (playerVehiclePrefab == null)
            return false;
        try
        {
            return KoenigseggJeskoPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                privateDriverPreparationExceptionLogged = true;
                context?.Logger.Warn(
                    $"KoenigseggJesko: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            return false;
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
            var ready = KoenigseggJeskoLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                context?.Logger.Info(
                    $"KoenigseggJesko: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"KoenigseggJesko: dealer stock update failed source='{source}': " +
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
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1.45f;
            }

            ConfigureMassProperties(vehicle.gameObject);
            var wheelPlacements = ConfigureWheelPlacements(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            ConfigureBodyColliders(vehicle.gameObject);
            var disabledFallbackChassis = DisableFallbackChassis(vehicle.gameObject);
            var disabledBonnetCamera = DisableBonnetCameraGeometry(vehicle.gameObject);
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var repairedBodyShell = RepairDamageBodyFromMainShell(vehicle.gameObject);
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<KoenigseggJeskoCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<KoenigseggJeskoCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialResult = KoenigseggJeskoMaterials.FixSolidMaterials(vehicle.gameObject);
            var glassController = vehicle.GetComponent<KoenigseggJeskoGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<KoenigseggJeskoGlassController>();
            glassController.Initialize(context);
            var lightingController = vehicle.GetComponent<KoenigseggJeskoLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<KoenigseggJeskoLightingController>();
            lightingController.Initialize(vehicle, context);
            var driverController = vehicle.GetComponent<KoenigseggJeskoDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<KoenigseggJeskoDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<KoenigseggJeskoPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<KoenigseggJeskoPaintController>();
            paintController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<KoenigseggJeskoAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<KoenigseggJeskoAudioController>();
            audioController.Initialize(vehicle, context);
            var accelerationTelemetry =
                vehicle.GetComponent<KoenigseggJeskoAccelerationTelemetry>();
            if (accelerationTelemetry == null)
            {
                accelerationTelemetry = vehicle.gameObject
                    .AddComponent<KoenigseggJeskoAccelerationTelemetry>();
            }
            accelerationTelemetry.Initialize(vehicle, context);

            context?.Logger.Info(
                $"KoenigseggJesko: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=9-speed-LST, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"wheelPlacements={wheelPlacements}/12, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstacles={normalizedNavMeshObstacles}, " +
                $"disabledFallbackChassis={disabledFallbackChassis}, " +
                $"disabledBonnetCamera={disabledBonnetCamera}, " +
                $"mainBodyShellRepaired={repairedBodyShell}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=telemetry-calibration-2, steeringCalipers=4, " +
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
                $"KoenigseggJesko: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
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
                SetFloat(spring, "maxForce", 20500f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.348f : 0.370f);
                SetFloat(wheel, "width", isFront ? 0.265f : 0.345f);
                SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
            }
        }
    }

    private static int ConfigureWheelPlacements(GameObject root)
    {
        var configured = 0;
        foreach (var pair in WheelPlacementOverrides)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(transform.name, pair.Key, StringComparison.Ordinal))
                    continue;

                transform.localPosition = pair.Value;
                configured++;
                break;
            }
        }

        return configured;
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
                colliders[0].center = new Vector3(0f, 0.38f, -0.02f);
                colliders[0].size = new Vector3(1.96f, 0.46f, 4.82f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.78f, -0.18f);
                colliders[1].size = new Vector3(1.72f, 0.62f, 2.62f);
            }
        }
    }

    private static int DisableFallbackChassis(GameObject root)
    {
        var disabled = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("LOD_B_CHASSIS", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            disabled++;
        }
        return disabled;
    }

    private static int DisableBonnetCameraGeometry(GameObject root)
    {
        var disabled = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            disabled++;
        }

        return disabled;
    }

    private bool RepairDamageBodyFromMainShell(GameObject root)
    {
        MeshFilter? damageFilter = null;
        MeshRenderer? damageRenderer = null;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!string.Equals(filter.name, "KoenigseggDamageBody", StringComparison.Ordinal))
                continue;
            damageFilter = filter;
            damageRenderer = filter.GetComponent<MeshRenderer>();
            break;
        }

        MeshRenderer? sourceRenderer = null;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name.IndexOf("BODY_mm_ext", StringComparison.OrdinalIgnoreCase) < 0 ||
                renderer.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (damageFilter != null && renderer.transform.IsChildOf(damageFilter.transform)))
                continue;
            sourceRenderer = renderer;
            break;
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (damageFilter == null || damageRenderer == null ||
            sourceRenderer == null || sourceFilter?.sharedMesh == null)
        {
            context?.Logger.Warn(
                $"KoenigseggJesko body vehicle={root.GetInstanceID()}: " +
                "normal BODY_mm_ext shell was not found; retained existing damage body.");
            return false;
        }

        try
        {
            var owner = root.GetComponent<KoenigseggJeskoDamageBodyMeshController>() ??
                        root.AddComponent<KoenigseggJeskoDamageBodyMeshController>();
            owner.Initialize(root, damageFilter, damageRenderer, sourceFilter, sourceRenderer, context);
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"KoenigseggJesko body vehicle={root.GetInstanceID()}: " +
                $"normal shell repair failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        const float driverSide = -1.45f;
        const float passengerSide = 1.45f;
        var configured = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
            {
                transform.localPosition = new Vector3(driverSide, 0.1f, 0f);
                configured++;
            }
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
            {
                transform.localPosition = new Vector3(passengerSide, 0.1f, 0f);
                configured++;
            }
        }

        if (configured != 2)
            Debug.LogWarning($"KoenigseggJesko: expected two exit markers, configured={configured}.");
    }

    private static int ConfigureNavMeshObstacles(GameObject root)
    {
        if (!TryGetBodyColliderBounds(root.transform, out var bodyBounds))
            return 0;

        var normalized = 0;
        foreach (var obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null || obstacle.shape != NavMeshObstacleShape.Box)
                continue;

            var obstacleTransform = obstacle.transform;
            var scale = obstacleTransform.lossyScale;
            if (Mathf.Abs(scale.x) < 0.0001f ||
                Mathf.Abs(scale.y) < 0.0001f ||
                Mathf.Abs(scale.z) < 0.0001f)
            {
                continue;
            }

            var rootTransform = root.transform;
            obstacle.center = obstacleTransform.InverseTransformPoint(
                rootTransform.TransformPoint(bodyBounds.center));
            obstacle.size = new Vector3(
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.right) /
                Mathf.Abs(scale.x),
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.up) /
                Mathf.Abs(scale.y),
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.forward) /
                Mathf.Abs(scale.z));
            normalized++;
        }

        return normalized;
    }

    private static bool TryGetBodyColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            foreach (var collider in child.GetComponents<BoxCollider>())
            {
                if (collider == null || collider.isTrigger)
                    continue;

                var halfSize = collider.size * 0.5f;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = collider.center + Vector3.Scale(
                        halfSize,
                        new Vector3(x, y, z));
                    var rootCorner = root.InverseTransformPoint(
                        collider.transform.TransformPoint(corner));
                    if (!found)
                    {
                        bounds = new Bounds(rootCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rootCorner);
                    }
                }
            }
        }

        return found;
    }

    private static float ProjectBodySizeOntoAxis(
        Vector3 bodySize,
        Transform root,
        Vector3 worldAxis)
    {
        worldAxis.Normalize();
        return Mathf.Abs(Vector3.Dot(worldAxis, root.right)) * bodySize.x +
               Mathf.Abs(Vector3.Dot(worldAxis, root.up)) * bodySize.y +
               Mathf.Abs(Vector3.Dot(worldAxis, root.forward)) * bodySize.z;
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
                $"KoenigseggJesko damage vehicle={vehicle.GetInstanceID()}: " +
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
                $"KoenigseggJesko damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<KoenigseggJeskoVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<KoenigseggJeskoVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        context?.Logger.Info(
            $"KoenigseggJesko damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"filters=[{string.Join(", ", filters.ConvertAll(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (name.IndexOf("_INT_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("WINDOWS", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("TYRE_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("WHEEL_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("ROTOR_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return name.StartsWith("KoenigseggDamageBody", StringComparison.Ordinal) ||
               name.IndexOf("Vehicle_Exterior", StringComparison.OrdinalIgnoreCase) >= 0 ||
               string.Equals(name, "Hood.075_Body_0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Hood.075_Plastic_0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Logo_Logo_0", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Front_part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Front_vents", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Headlight_carbon", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("Daylight_Part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Daylight_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Mid_part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Mid_parts_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_plastic", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_vent", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_engine_carbon", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Exhaust_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Tail_light_Plastic", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Tail_light_", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Taillight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("Brake_light_", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Turning_light", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("Vents_", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Mirrors_Body_0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Mirrors_Carbon_0", StringComparison.OrdinalIgnoreCase);
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
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateJeskoPowerCurve());
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
            SetFloat(transmission, "shiftDuration", 0.03f);
            SetFloat(transmission, "_downshiftRPM", 3300f);
            SetFloat(transmission, "_upshiftRPM", 8300f);
            SetInt(transmission, "forwardGearCount", 9);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", JeskoGears);

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

            return GetInt(transmission, "forwardGearCount") == 9;
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
internal sealed class KoenigseggJeskoDamageBodyMeshController : MonoBehaviour
{
    private Mesh? runtimeMesh;
    private bool initialized;

    internal void Initialize(
        GameObject root,
        MeshFilter damageFilter,
        MeshRenderer damageRenderer,
        MeshFilter sourceFilter,
        MeshRenderer sourceRenderer,
        ModContext? context)
    {
        if (initialized)
            return;

        var sourceMesh = sourceFilter.sharedMesh;
        if (sourceMesh == null)
            throw new InvalidOperationException("Normal body shell has no mesh.");

        runtimeMesh = Instantiate(sourceMesh);
        runtimeMesh.name = "KoenigseggDamageBody_RuntimeMainShell";
        var sourceToRoot = root.transform.worldToLocalMatrix *
                           sourceFilter.transform.localToWorldMatrix;
        var vertices = runtimeMesh.vertices;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToRoot.MultiplyPoint3x4(vertices[index]);
        runtimeMesh.vertices = vertices;

        var normals = runtimeMesh.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToRoot.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            runtimeMesh.normals = normals;
        }

        var tangents = runtimeMesh.tangents;
        if (tangents.Length == vertices.Length)
        {
            var tangentMatrix = sourceToRoot;
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = tangentMatrix.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
            runtimeMesh.tangents = tangents;
        }

        runtimeMesh.RecalculateBounds();
        runtimeMesh.UploadMeshData(false);
        damageFilter.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        damageFilter.transform.localScale = Vector3.one;
        damageFilter.sharedMesh = runtimeMesh;
        damageRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        damageRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        damageRenderer.receiveShadows = sourceRenderer.receiveShadows;
        damageRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        damageRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        damageRenderer.motionVectorGenerationMode = sourceRenderer.motionVectorGenerationMode;
        damageRenderer.allowOcclusionWhenDynamic = sourceRenderer.allowOcclusionWhenDynamic;
        damageRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        sourceRenderer.enabled = false;
        sourceRenderer.sharedMaterials = Array.Empty<Material>();
        initialized = true;
        context?.Logger.Info(
            $"KoenigseggJesko body vehicle={root.GetInstanceID()}: replaced stale damage shell " +
            $"with normal source='{sourceRenderer.name}', vertices={vertices.Length}, " +
            "bonnet-camera geometry excluded.");
    }

    private void OnDestroy()
    {
        if (runtimeMesh != null)
            Destroy(runtimeMesh);
        runtimeMesh = null;
    }
}

[AddComponentMenu("")]
public sealed class KoenigseggJeskoRimGeometryController : MonoBehaviour
{
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private bool initialized;

    internal int Initialize(ModContext? context)
    {
        if (initialized)
            return runtimeMeshes.Count;

        initialized = true;
        var mirrored = 0;
        mirrored += MirrorRightMesh("Wheel_FR_Rim_0", "Wheel_FL_Rim_0");
        mirrored += MirrorRightMesh("Wheel_BR_Rim_0", "Wheel_BL_Rim_0");
        mirrored += MirrorRightMesh("Wheel_FR_Caliper_0", "Wheel_FL_Caliper_0");
        mirrored += MirrorRightMesh("Wheel_BR_Caliper_0", "Wheel_BL_Caliper_0");
        mirrored += MirrorRightMesh("Wheel_FR_Tire_0", "Wheel_FL_Tire_0");
        mirrored += MirrorRightMesh("Wheel_BR_Tire_0", "Wheel_BL_Tire_0");
        mirrored += MirrorRightMesh("Wheel_FR_Brake_rotor_0", "Wheel_FL_Brake_rotor_0");
        mirrored += MirrorRightMesh("Wheel_BR_Brake_rotor_0", "Wheel_BL_Brake_rotor_0");
        mirrored += MirrorRightMesh("Wheel_FR_Logo_0", "Wheel_FL_Logo_0");
        mirrored += MirrorRightMesh("Wheel_BR_Logo_0", "Wheel_BL_Logo_0");
        if (mirrored != 10)
        {
            context?.Logger.Warn(
                $"KoenigseggJesko wheel finish vehicle={GetInstanceID()}: mirrored " +
                $"{mirrored}/10 left-side wheel meshes; a mesh pair is missing.");
        }
        else
        {
            context?.Logger.Info(
                $"KoenigseggJesko wheel finish vehicle={GetInstanceID()}: complete left " +
                "wheel assemblies rebuilt as exact mirrors of the preferred right-side geometry.");
        }
        return mirrored;
    }

    private int MirrorRightMesh(string rightName, string leftName)
    {
        MeshFilter? right = null;
        MeshFilter? left = null;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (string.Equals(filter.name, rightName, StringComparison.Ordinal))
                right = filter;
            else if (string.Equals(filter.name, leftName, StringComparison.Ordinal))
                left = filter;
        }
        if (right?.sharedMesh == null || left == null)
            return 0;

        var mirroredMesh = Instantiate(right.sharedMesh);
        mirroredMesh.name = leftName + "_MirroredFromRight";
        var rightToRoot = transform.worldToLocalMatrix * right.transform.localToWorldMatrix;
        var rootToLeft = left.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        var rightToMirroredLeft =
            rootToLeft * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * rightToRoot;
        var vertices = mirroredMesh.vertices;
        var normals = mirroredMesh.normals;
        var tangents = mirroredMesh.tangents;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = rightToMirroredLeft.MultiplyPoint3x4(vertices[index]);
        mirroredMesh.vertices = vertices;

        // Preserve the preferred right-side authored smoothing exactly. A
        // recalculation produces subtly different highlights even when geometry
        // and materials match, which made the left wheels look washed out.
        var normalTransform = rightToMirroredLeft.inverse.transpose;
        for (var index = 0; index < normals.Length; index++)
            normals[index] = normalTransform.MultiplyVector(normals[index]).normalized;
        mirroredMesh.normals = normals;
        var handedness = rightToMirroredLeft.determinant < 0f ? -1f : 1f;
        for (var index = 0; index < tangents.Length; index++)
        {
            var direction = rightToMirroredLeft.MultiplyVector(
                new Vector3(tangents[index].x, tangents[index].y, tangents[index].z)).normalized;
            tangents[index] = new Vector4(
                direction.x,
                direction.y,
                direction.z,
                tangents[index].w * handedness);
        }
        mirroredMesh.tangents = tangents;

        // Mirroring reverses handedness. Restore outward-facing triangle winding
        // before deriving normals so both sides respond identically to lighting.
        for (var subMesh = 0; subMesh < mirroredMesh.subMeshCount; subMesh++)
        {
            var triangles = mirroredMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var second = triangles[index + 1];
                triangles[index + 1] = triangles[index + 2];
                triangles[index + 2] = second;
            }
            mirroredMesh.SetTriangles(triangles, subMesh, false);
        }
        mirroredMesh.RecalculateBounds();
        left.sharedMesh = mirroredMesh;
        runtimeMeshes.Add(mirroredMesh);
        return 1;
    }

    private void OnDestroy()
    {
        foreach (var mesh in runtimeMeshes)
        {
            if (mesh != null)
                Destroy(mesh);
        }
        runtimeMeshes.Clear();
    }
}

[AddComponentMenu("")]
public sealed class KoenigseggJeskoGlassController : MonoBehaviour
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
                    !KoenigseggJeskoMaterials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    KoenigseggJeskoMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
            renderer.allowOcclusionWhenDynamic = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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
                    KoenigseggJeskoMaterials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    KoenigseggJeskoMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            context?.Logger.Info(
                $"KoenigseggJesko glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            context?.Logger.Info(
                $"KoenigseggJesko glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class KoenigseggJeskoVisualDamageController : MonoBehaviour
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
                $"KoenigseggJesko damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
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
                    $"KoenigseggJesko damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
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
                $"KoenigseggJesko damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
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
