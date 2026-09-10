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
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public sealed class Porsche911GT3RSRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1450f;
    private const float EnginePowerKw = 386f;
    private const float EngineIdleRpm = 900f;
    private const float EngineLimitRpm = 9000f;
    private const float SpeedLimitKph = 296f;
    private const float FinalDriveRatio = 4.27f;
    private const float EngineInertia = 0.075f;
    private const float EngineStartDuration = 0.38f;
    private const float ClutchEngagementRpm = 1250f;
    private const float ClutchThrottleOffsetRpm = 600f;
    private const float ClutchEngagementRange = 550f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 9000f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.080f;
    private const float FrontTireRadius = 0.35025f;
    private const float RearTireRadius = 0.36720f;
    private const float FrontTireWidth = 0.275f;
    private const float RearTireWidth = 0.335f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.08f, -0.28f);

    private static readonly float[] GT3RSGears =
    {
        -3.42f,
        0f,
        3.75f,
        2.38f,
        1.72f,
        1.34f,
        1.11f,
        0.96f,
        0.84f,
    };

    private static AnimationCurve CreateGT3RSPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0.14f),
            new Keyframe(0.40f, 0.48f),
            new Keyframe(0.67f, 0.80f),
            new Keyframe(0.94f, 1f),
            new Keyframe(1f, 0.94f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerReadyLogged;

    public static Porsche911GT3RSRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<Porsche911GT3RSRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(Porsche911GT3RSRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<Porsche911GT3RSRuntime>();
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
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
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
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReadyLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<Porsche911GT3RSGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<Porsche911GT3RSPaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = StartCoroutine(ActivateEnteredVehicle(vehicle));
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

    private IEnumerator ActivateEnteredVehicle(VehicleController vehicle)
    {
        const int maximumPasses = 4;
        yield return null;

        var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
        var physics = vehicle.GetComponent<PhysicsVehicle>();
        var wasKinematic = rigidbody != null && rigidbody.isKinematic;
        var physicsWasEnabled = physics != null && physics.enabled;
        var engineWasRunning = physics?.powertrain?.engine?.IsRunning ?? false;
        var gearBefore = physics?.powertrain?.transmission?.Gear ?? 0;
        var constraintsBefore = rigidbody?.constraints ?? RigidbodyConstraints.None;
        var appliedPasses = 0;
        var wheelControllersEnabled = 0;

        for (var pass = 0; pass < maximumPasses; pass++)
        {
            yield return new WaitForFixedUpdate();
            if (vehicle == null || !vehicle.controlledByPlayer)
                continue;

            appliedPasses++;
            // Dealer purchase finalization can assign the chosen color a frame
            // after the entry event. This bounded entry sequence catches that
            // transition without adding a permanent paint polling loop.
            vehicle.GetComponent<Porsche911GT3RSPaintController>()
                ?.ApplyCurrentColor($"vehicle-entered-pass-{pass + 1}");
            // Dealer display vehicles are frozen with Rigidbody constraints,
            // not only isKinematic. Use the game's own transition so all
            // vehicle physics state and center-of-mass bookkeeping is restored.
            vehicle.SetFreeze(false);
            if (physics != null)
                physics.enabled = true;
            foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != null && string.Equals(
                        component.GetType().FullName,
                        "NWH.WheelController3D.WheelController",
                        StringComparison.Ordinal))
                {
                    if (!component.enabled)
                        wheelControllersEnabled++;
                    component.enabled = true;
                }
            }

            if (rigidbody != null)
            {
                rigidbody.isKinematic = false;
                rigidbody.WakeUp();
            }

            var engine = physics?.powertrain?.engine;
            var transmission = physics?.powertrain?.transmission;
            if (engine != null && !engine.IsRunning)
                engine.StartEngine();
            if (transmission != null && transmission.Gear == 0)
                transmission.ShiftInto(1, true);
        }

        enteredVehicleActivationCoroutine = null;
        if (vehicle == null)
            yield break;

        var engineRunning = physics?.powertrain?.engine?.IsRunning ?? false;
        var gearAfter = physics?.powertrain?.transmission?.Gear ?? 0;
        var isKinematic = rigidbody != null && rigidbody.isKinematic;
        var constraintsAfter = rigidbody?.constraints ?? RigidbodyConstraints.None;
        var physicsEnabled = physics != null && physics.enabled;
        context?.Logger.Info(
            $"Porsche911GT3RS: entered-vehicle activation instance={vehicle.GetInstanceID()}, " +
            $"controlled={vehicle.controlledByPlayer}, passes={appliedPasses}/{maximumPasses}, " +
            $"kinematic={wasKinematic}->{isKinematic}, physicsEnabled={physicsWasEnabled}->{physicsEnabled}, " +
            $"constraints={constraintsBefore}->{constraintsAfter}, " +
            $"wheelControllersEnabled={wheelControllersEnabled}, " +
            $"engineRunning={engineWasRunning}->{engineRunning}, gear={gearBefore}->{gearAfter}, " +
            $"fuel={vehicle.GetCurrentFuel():F2}.");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (Porsche911GT3RSLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
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
                $"Porsche911GT3RS: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
    }

    private bool EnsureDealerStock(string source)
    {
        try
        {
            var ready = Porsche911GT3RSLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                context?.Logger.Info(
                    $"Porsche911GT3RS: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"Porsche911GT3RS: dealer stock update failed source='{source}': " +
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
            ConfigureWheelControllers(vehicle.gameObject);
            ConfigureBodyColliders(vehicle.gameObject);
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var wheelGeometryController = vehicle.GetComponent<Porsche911GT3RSWheelGeometryController>();
            if (wheelGeometryController == null)
                wheelGeometryController = vehicle.gameObject.AddComponent<Porsche911GT3RSWheelGeometryController>();
            wheelGeometryController.Initialize(context);
            var caliperController = vehicle.GetComponent<Porsche911GT3RSCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<Porsche911GT3RSCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<Porsche911GT3RSMaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<Porsche911GT3RSMaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<Porsche911GT3RSGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<Porsche911GT3RSGlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<Porsche911GT3RSPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<Porsche911GT3RSPaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<Porsche911GT3RSLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<Porsche911GT3RSLightingController>();
            lightingController.Initialize(vehicle, context);
            var driverController = vehicle.GetComponent<Porsche911GT3RSDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<Porsche911GT3RSDriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<Porsche911GT3RSAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<Porsche911GT3RSAudioController>();
            audioController.Initialize(vehicle, context);
            context?.Logger.Info(
                $"Porsche911GT3RS: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=7-speed-PDK, rwd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=official-flat-six-profile, steeringCalipers=4, " +
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
                $"Porsche911GT3RS: vehicle configuration failed instance={instanceId}: " +
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
                SetFloat(spring, "maxForce", 19000f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? FrontTireRadius : RearTireRadius);
                SetFloat(wheel, "width", isFront ? FrontTireWidth : RearTireWidth);
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
                colliders[0].center = new Vector3(0f, 0.32f, 0f);
                colliders[0].size = new Vector3(1.82f, 0.42f, 4.40f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.78f, -0.08f);
                colliders[1].size = new Vector3(1.62f, 0.72f, 2.70f);
            }
        }
    }

    private int ConfigureVisualDamage(VehicleController vehicle)
    {
        var deformableBodyMeshes = 0;
        foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
                continue;
            component.enabled = true;
            ClearCollection(component, "_deformationQueue");
            SetFloat(component, "deformationStrength", DeformationStrength);
            SetFloat(component, "deformationRadius", DeformationRadius);
            SetFloat(component, "deformationRandomness", DeformationRandomness);
            if (GetMember(component, "meshFilters") is IList meshFilters)
                deformableBodyMeshes += meshFilters.Count;
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"Porsche911GT3RS damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        if (deformableBodyMeshes == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"Porsche911GT3RS damage vehicle={vehicle.GetInstanceID()}: " +
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
        // PorscheDamageBody is baked into vehicle-root coordinates, allowing the
        // game's collision and Repair() lifecycle to deform and reset it directly.
        damageHandler.meshDeform = false;

        context?.Logger.Info(
            $"Porsche911GT3RS damage vehicle={vehicle.GetInstanceID()}: enabled game-native deformation " +
            $"bodyMeshes={deformableBodyMeshes} threshold={DamageDecelerationThreshold / 100f:0.0}mps; " +
            "repairReset=CarController.Repair/event-driven.");
        return deformableBodyMeshes;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (HasAncestor(filter.transform, "PorscheWheel") ||
            HasAncestor(filter.transform, "PorscheFixedCaliper") ||
            HasAncestor(filter.transform, "windshield") ||
            HasAncestor(filter.transform, "doorglass") ||
            HasAncestor(filter.transform, "quarterglass") ||
            HasAncestor(filter.transform, "backlight_tint") ||
            HasAncestor(filter.transform, "seat_") ||
            HasAncestor(filter.transform, "dash_") ||
            HasAncestor(filter.transform, "steer_") ||
            HasAncestor(filter.transform, "pedal") ||
            HasAncestor(filter.transform, "engine_") ||
            HasAncestor(filter.transform, "underbody") ||
            HasAncestor(filter.transform, "suspension") ||
            HasMaterial(filter, "carpet", "fabric", "leather", "seatbelt", "gauges", "symbols"))
            return false;

        if (name.StartsWith("PorscheDamageBody", StringComparison.Ordinal))
            return true;

        return HasAncestor(filter.transform, "gt3rs_carbon_Wing") ||
               HasAncestor(filter.transform, "gt3rs_carbon_roof") ||
               HasAncestor(filter.transform, "gt3rs_carbon_hood") ||
               HasAncestor(filter.transform, "gt3rs_sideskirts_") ||
               HasAncestor(filter.transform, "gt3rs_tailgate") ||
               HasAncestor(filter.transform, "gt3rs_left_leg") ||
               HasAncestor(filter.transform, "gt3rs_right_leg") ||
               HasAncestor(filter.transform, "gt3rs_bumper_") ||
               HasAncestor(filter.transform, "gt3rs_spoiler") ||
               HasAncestor(filter.transform, "gt3rs_fender_") ||
               HasAncestor(filter.transform, "gt3rs_door_") ||
               HasAncestor(filter.transform, "headlight_") ||
               HasAncestor(filter.transform, "headlightglass_") ||
               HasAncestor(filter.transform, "mirror_") ||
               HasAncestor(filter.transform, "fascia_") ||
               (HasAncestor(filter.transform, "body_gt3rs") &&
                HasMaterial(filter, "carPaint", "plastic", "mirror", "chrome", "rivet"));
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool HasMaterial(MeshFilter filter, params string[] markers)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            foreach (var marker in markers)
                if (material.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
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
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateGT3RSPowerCurve());
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
            SetFloat(transmission, "shiftDuration", 0.075f);
            SetFloat(transmission, "_downshiftRPM", 3200f);
            SetFloat(transmission, "_upshiftRPM", 8850f);
            SetInt(transmission, "forwardGearCount", 7);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", GT3RSGears);

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
            {
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);
            }

            var rearDriveConfigured = false;
            if (GetMember(powertrain, "differentials") is IList differentials)
            {
                foreach (var differential in differentials)
                {
                    if (!string.Equals(
                            GetMember(differential, "name") as string,
                            "Center Differential",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // The reference center differential's B output is the rear differential.
                    SetFloat(differential, "biasAB", 1f);
                    rearDriveConfigured = true;
                }
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

            return GetInt(transmission, "forwardGearCount") == 7 && rearDriveConfigured;
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
public sealed class Porsche911GT3RSWheelGeometryController : MonoBehaviour
{
    private const float RimInsetMeters = 0.055f;
    private const float TireComponentDiameterRatio = 0.88f;
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private bool initialized;

    internal int Initialize(ModContext? context)
    {
        if (initialized)
            return runtimeMeshes.Count;

        initialized = true;
        var correctedMeshes = 0;
        var correctedComponents = 0;
        var correctedVertices = 0;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter?.sharedMesh == null || !IsPorscheWheelMesh(filter.transform))
                continue;

            var materialName = filter.GetComponent<Renderer>()?.sharedMaterial?.name ?? string.Empty;
            try
            {
                if (materialName.IndexOf("GT3RS_black", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    correctedVertices += InsetWholeMesh(filter);
                    correctedComponents++;
                    correctedMeshes++;
                }
                else if (materialName.IndexOf("wheels_chrome", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var result = InsetInteriorComponents(filter);
                    correctedComponents += result.Components;
                    correctedVertices += result.Vertices;
                    correctedMeshes++;
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Porsche911GT3RS wheel geometry vehicle={GetInstanceID()} mesh='{filter.name}' " +
                    $"could not be inset: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (correctedMeshes == 8)
        {
            context?.Logger.Info(
                $"Porsche911GT3RS wheel geometry vehicle={GetInstanceID()}: inset " +
                $"{correctedComponents} rim components ({correctedVertices} vertices) by " +
                $"{RimInsetMeters:F3}m; four tire envelopes retained.");
        }
        else
        {
            context?.Logger.Warn(
                $"Porsche911GT3RS wheel geometry vehicle={GetInstanceID()}: corrected " +
                $"{correctedMeshes}/8 rim meshes; expected four wheel and four center-cap meshes.");
        }
        return correctedMeshes;
    }

    private static bool IsPorscheWheelMesh(Transform candidate)
    {
        for (var current = candidate; current != null && current.parent != null; current = current.parent)
        {
            if (current.name.StartsWith("PorscheWheel", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private int InsetWholeMesh(MeshFilter filter)
    {
        var mesh = CreateRuntimeMesh(filter, "InsetCap");
        var vertices = mesh.vertices;
        var displacement = GetLocalInset(filter);
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] += displacement;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return vertices.Length;
    }

    private CorrectionResult InsetInteriorComponents(MeshFilter filter)
    {
        var mesh = CreateRuntimeMesh(filter, "InsetRim");
        var vertices = mesh.vertices;
        var parent = new int[vertices.Length];
        for (var index = 0; index < parent.Length; index++)
            parent[index] = index;

        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            var triangles = mesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                Union(parent, triangles[index], triangles[index + 1]);
                Union(parent, triangles[index], triangles[index + 2]);
            }
        }

        var extents = new Dictionary<int, ComponentExtents>();
        for (var index = 0; index < vertices.Length; index++)
        {
            var root = Find(parent, index);
            if (!extents.TryGetValue(root, out var component))
                component = new ComponentExtents(vertices[index]);
            else
                component.Include(vertices[index]);
            extents[root] = component;
        }

        var tireDiameter = Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z);
        var componentLimit = tireDiameter * TireComponentDiameterRatio;
        var displacement = GetLocalInset(filter);
        var correctedRoots = new HashSet<int>();
        var correctedVertices = 0;
        for (var index = 0; index < vertices.Length; index++)
        {
            var root = Find(parent, index);
            var component = extents[root];
            if (Mathf.Max(component.SizeY, component.SizeZ) >= componentLimit)
                continue;
            vertices[index] += displacement;
            correctedRoots.Add(root);
            correctedVertices++;
        }

        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return new CorrectionResult(correctedRoots.Count, correctedVertices);
    }

    private Mesh CreateRuntimeMesh(MeshFilter filter, string suffix)
    {
        var mesh = Instantiate(filter.sharedMesh);
        mesh.name = filter.sharedMesh.name + "_" + suffix;
        filter.sharedMesh = mesh;
        runtimeMeshes.Add(mesh);
        return mesh;
    }

    private Vector3 GetLocalInset(MeshFilter filter)
    {
        var center = transform.InverseTransformPoint(filter.GetComponent<Renderer>().bounds.center);
        var side = center.x < 0f ? -1f : 1f;
        return filter.transform.InverseTransformVector(
            transform.right * (-side * RimInsetMeters));
    }

    private static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }
        return value;
    }

    private static void Union(int[] parent, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot != rightRoot)
            parent[rightRoot] = leftRoot;
    }

    private readonly struct CorrectionResult
    {
        public readonly int Components;
        public readonly int Vertices;

        public CorrectionResult(int components, int vertices)
        {
            Components = components;
            Vertices = vertices;
        }
    }

    private struct ComponentExtents
    {
        private float minimumY;
        private float maximumY;
        private float minimumZ;
        private float maximumZ;

        public float SizeY => maximumY - minimumY;
        public float SizeZ => maximumZ - minimumZ;

        public ComponentExtents(Vector3 vertex)
        {
            minimumY = maximumY = vertex.y;
            minimumZ = maximumZ = vertex.z;
        }

        public void Include(Vector3 vertex)
        {
            minimumY = Mathf.Min(minimumY, vertex.y);
            maximumY = Mathf.Max(maximumY, vertex.y);
            minimumZ = Mathf.Min(minimumZ, vertex.z);
            maximumZ = Mathf.Max(maximumZ, vertex.z);
        }
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
public sealed class Porsche911GT3RSGlassController : MonoBehaviour
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
                    Porsche911GT3RSMaterials.GetTransparentRole(renderer, source) !=
                    Porsche911GT3RSTransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    Porsche911GT3RSMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    Porsche911GT3RSMaterials.GetTransparentRole(renderer, material) ==
                    Porsche911GT3RSTransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    Porsche911GT3RSMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            context?.Logger.Info(
                $"Porsche911GT3RS glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            context?.Logger.Info(
                $"Porsche911GT3RS glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class Porsche911GT3RSVisualDamageController : MonoBehaviour
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
                $"Porsche911GT3RS damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
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
                    $"Porsche911GT3RS damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
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
                $"Porsche911GT3RS damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
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

