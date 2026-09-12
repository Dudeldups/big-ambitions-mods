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
    private const float VehicleLinearDrag = 0.035f;
    private const float FrontForwardGrip = 0.90f;
    private const float RearForwardGrip = 0.95f;
    private const float FrontForwardStiffness = 1.27f;
    private const float RearForwardStiffness = 1.35f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.08f, -0.28f);
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.61f, 1.58f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.08f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.60f, -1.78f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.02f);

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
            new Keyframe(0.10f, 0.03f),
            new Keyframe(0.23f, 0.10f),
            new Keyframe(0.45f, 0.26f),
            new Keyframe(0.67f, 0.56f),
            new Keyframe(0.82f, 0.68f),
            new Keyframe(0.90f, 0.79f),
            new Keyframe(0.94f, 1f),
            new Keyframe(1f, 0.94f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerRegistrationReady;
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
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
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

    private void Update()
    {
        // Dealer purchases do not raise onEnterVehicle. Keep the hot path to
        // one count comparison and enumerate only after the collection changes.
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var vehicleCount = vehicles?.Count ?? 0;
        if (vehicleCount == cachedPlayerVehicleCount)
            return;

        ConfigureExistingVehicles(out _);
    }

    private void SubscribeEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GameEvent.onGameEventTriggered += HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
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
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleGameEvent(string _)
    {
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        selectedVehicle!
            .GetComponent<Porsche911GT3RSPaintController>()
            ?.ApplyCurrentColor("game-event");
    }

    private void HandleVehicleVariablesChanged() => ConfigureExistingVehicles(out _);

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
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
        configuredVehicleIds.Clear();
        cachedPlayerVehicleCount = -1;
        dealerRegistrationReady = false;
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

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = StartCoroutine(RecoverPlayerNavMeshAfterExit(vehicle));
    }

    private IEnumerator RecoverPlayerNavMeshAfterExit(VehicleController exitedVehicle)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        var root = PlayerHelper.PlayerController?.transform;
        if (root == null)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
        var needsRecovery = false;
        foreach (var agent in agents)
            needsRecovery |= agent != null && agent.enabled && !agent.isOnNavMesh;
        needsRecovery |= !IsPlayerExitClear(root, root.position);
        if (!needsRecovery || !TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var characterControllers = root.GetComponentsInChildren<CharacterController>(true);
        var controllerStates = Array.ConvertAll(
            characterControllers,
            controller => controller != null && controller.enabled);
        var agentStates = Array.ConvertAll(agents, agent => agent != null && agent.enabled);
        try
        {
            foreach (var controller in characterControllers)
                if (controller != null) controller.enabled = false;
            foreach (var agent in agents)
                if (agent != null) agent.enabled = false;
            root.position = target;
            Physics.SyncTransforms();
        }
        finally
        {
            for (var index = 0; index < agents.Length; index++)
            {
                var agent = agents[index];
                if (agent == null)
                    continue;
                agent.enabled = agentStates[index];
                if (agent.enabled && agent.isOnNavMesh)
                {
                    agent.Warp(target);
                    agent.ResetPath();
                }
            }
            for (var index = 0; index < characterControllers.Length; index++)
                if (characterControllers[index] != null)
                    characterControllers[index].enabled = controllerStates[index];
            Physics.SyncTransforms();
        }
        exitedPlayerRecoveryCoroutine = null;
    }

    private static bool TryFindClearExitPosition(
        Transform playerRoot,
        VehicleController exitedVehicle,
        out Vector3 target)
    {
        var vehicleTransform = exitedVehicle.transform;
        var candidates = new[]
        {
            playerRoot.position,
            vehicleTransform.position - vehicleTransform.right * 2.05f,
            vehicleTransform.position + vehicleTransform.right * 2.05f,
            vehicleTransform.position - vehicleTransform.forward * 2.35f,
            vehicleTransform.position + vehicleTransform.forward * 2.35f,
            vehicleTransform.position - vehicleTransform.right * 2.05f - vehicleTransform.forward * 1.35f,
            vehicleTransform.position + vehicleTransform.right * 2.05f - vehicleTransform.forward * 1.35f,
            vehicleTransform.position - vehicleTransform.right * 2.45f + vehicleTransform.forward * 1.15f,
            vehicleTransform.position + vehicleTransform.right * 2.45f + vehicleTransform.forward * 1.15f,
        };

        foreach (var candidate in candidates)
        {
            if (!NavMesh.SamplePosition(candidate, out var hit, 1.25f, NavMesh.AllAreas))
                continue;
            var sampled = hit.position + Vector3.up * 0.05f;
            if (!IsPlayerExitClear(playerRoot, sampled))
                continue;
            target = sampled;
            return true;
        }

        target = default;
        return false;
    }

    private static bool IsPlayerExitClear(Transform playerRoot, Vector3 position)
    {
        var overlaps = Physics.OverlapCapsule(
            position + Vector3.up * 0.42f,
            position + Vector3.up * 1.55f,
            0.30f,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (var overlap in overlaps)
        {
            if (overlap == null || overlap.transform.IsChildOf(playerRoot))
                continue;
            return false;
        }
        return true;
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
        Porsche911GT3RSDiagnostics.Info(
            context,
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
        if (!Porsche911GT3RSLuxuryDealerStock.IsTargetDealer(registration?.BusinessName) ||
            dealerRegistrationReady)
        {
            return;
        }

        if (BusinessLayoutSetHelper.loadingLayouts)
        {
            ScheduleInitialization("dealer-entered");
            return;
        }

        EnsureDealerStock("dealer-entered");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen || dealerRegistrationReady)
            return;

        if (BusinessLayoutSetHelper.loadingLayouts)
        {
            ScheduleInitialization("full-menu");
            return;
        }

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
        while (BusinessLayoutSetHelper.loadingLayouts)
        {
            ConfigureExistingVehicles(out _);
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        var dealerReady = dealerRegistrationReady;
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
                $"Porsche911GT3RS: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerRegistrationReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            var ready = Porsche911GT3RSLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                Porsche911GT3RSDiagnostics.Info(
                    context,
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
        cachedPlayerVehicleCount = vehicles?.Count ?? 0;
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
        {
            vehicle.GetComponent<Porsche911GT3RSPaintController>()
                ?.ApplyCurrentColor("vehicle-variables-changed");
            return;
        }

        try
        {
            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = 1.45f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<Porsche911GT3RSContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<Porsche911GT3RSContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
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
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<Porsche911GT3RSDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<Porsche911GT3RSDriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<Porsche911GT3RSAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<Porsche911GT3RSAudioController>();
            audioController.Initialize(vehicle, context);
            Porsche911GT3RSDiagnostics.Info(
                context,
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
                var forwardFriction = GetMember(component, "forwardFriction");
                if (forwardFriction != null)
                {
                    SetFloat(
                        forwardFriction,
                        "grip",
                        isFront ? FrontForwardGrip : RearForwardGrip);
                    SetFloat(
                        forwardFriction,
                        "stiffness",
                        isFront ? FrontForwardStiffness : RearForwardStiffness);
                    SetValue(
                        component,
                        "forwardFriction",
                        forwardFriction.GetType(),
                        forwardFriction);
                }
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

    private static void ConfigureBodyColliders(GameObject root, PhysicMaterial contactMaterial)
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
            var frontContactCollider = colliders.Length > 2
                ? colliders[2]
                : transform.gameObject.AddComponent<BoxCollider>();
            frontContactCollider.center = FrontContactColliderCenter;
            frontContactCollider.size = FrontContactColliderSize;
            frontContactCollider.isTrigger = false;
            frontContactCollider.enabled = true;
            var rearContactCollider = colliders.Length > 3
                ? colliders[3]
                : transform.gameObject.AddComponent<BoxCollider>();
            rearContactCollider.center = RearContactColliderCenter;
            rearContactCollider.size = RearContactColliderSize;
            rearContactCollider.isTrigger = false;
            rearContactCollider.enabled = true;

            foreach (var collider in transform.GetComponents<BoxCollider>())
                collider.sharedMaterial = contactMaterial;
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
                $"Porsche911GT3RS damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var filters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !IsDeformableExterior(filter))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null)
                filters.Add(filter);
        }

        if (filters.Count == 0)
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
        // Imported panels do not share one root-local coordinate system, so the
        // model-aware controller deforms the selected outer shell in world space.
        damageHandler.meshDeform = false;

        var visualDamage = vehicle.GetComponent<Porsche911GT3RSVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<Porsche911GT3RSVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        Porsche911GT3RSDiagnostics.DamageInfo(
            context,
            $"Porsche911GT3RS damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (HasAncestor(filter.transform, "gt3rs_tailgate_TwiXeR_992_CSR2_Badge") ||
            HasAncestor(filter.transform, "Object_119") ||
            HasAncestor(filter.transform, "fascia_glass") ||
            HasAncestor(filter.transform, "fascia_mid") ||
            HasAncestor(filter.transform, "exhausttip_3_") ||
            HasAncestor(filter.transform, "exhaust_180") ||
            HasAncestor(filter.transform, "exhaust_TT"))
        {
            return true;
        }

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
            HasAncestor(filter.transform, "suspension") ||
            HasMaterial(filter, "carpet", "fabric", "leather", "seatbelt", "gauges"))
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
               HasAncestor(filter.transform, "underbody_gt3rs") ||
               HasAncestor(filter.transform, "exhausttip_3_") ||
               HasAncestor(filter.transform, "bumperbar_F") ||
               HasAncestor(filter.transform, "bumperbar_R") ||
               HasAncestor(filter.transform, "TwiXeR_992_radiator") ||
               HasAncestor(filter.transform, "headlight_") ||
               HasAncestor(filter.transform, "headlightglass_") ||
               HasAncestor(filter.transform, "mirror_") ||
               HasAncestor(filter.transform, "fascia_glass") ||
               HasAncestor(filter.transform, "fascia_") ||
               HasAncestor(filter.transform, "body_chrome_end") ||
               HasAncestor(filter.transform, "body.002") ||
               HasAncestor(filter.transform, "body.005") ||
               (HasAncestor(filter.transform, "body_gt3rs") &&
                HasMaterial(filter, "carPaint", "plastic", "mirror", "chrome", "rivet",
                    "rubbertrim", "metal_radiator"));
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

#if false // Retired: wheel geometry is authored statically in the prefab.
[AddComponentMenu("")]
[DefaultExecutionOrder(900)]
public sealed class Porsche911GT3RSWheelGeometryController : MonoBehaviour
{
    private const float WheelAssemblyInsetMeters = 0.085f;
    private static readonly string[,] CornerNames =
    {
        { "FrontLeft_WheelController", "PorscheWheelFrontLeft", "PorscheFixedCaliperFrontLeft" },
        { "FrontRight_WheelController", "PorscheWheelFrontRight", "PorscheFixedCaliperFrontRight" },
        { "RearLeft_WheelController", "PorscheWheelRearLeft", "PorscheFixedCaliperRearLeft" },
        { "RearRight_WheelController", "PorscheWheelRearRight", "PorscheFixedCaliperRearRight" },
    };
    private readonly List<WheelVisualBinding> bindings = new List<WheelVisualBinding>(4);
    private bool initialized;

    internal int Initialize(ModContext? context, float chassisAndTireDrop)
    {
        if (initialized)
            return CornerNames.GetLength(0);

        initialized = true;
        bindings.Clear();
        var visual = FindTransform("PorscheVisual");
        if (visual != null)
            visual.position -= transform.up * chassisAndTireDrop;
        var damageBody = FindTransform("PorscheDamageBody");
        if (damageBody != null)
            damageBody.position -= transform.up * chassisAndTireDrop;

        var correctedCorners = 0;
        for (var index = 0; index < CornerNames.GetLength(0); index++)
        {
            try
            {
                var controller = FindTransform(CornerNames[index, 0]);
                var wheel = FindTransform(CornerNames[index, 1]);
                var caliper = FindTransform(CornerNames[index, 2]);
                if (controller == null || wheel == null || caliper == null)
                    throw new InvalidOperationException(
                        $"wheel assembly is incomplete for corner={index}");

                RecenterRollingGeometry(wheel, caliper);

                // Wheel.Initialize() reparents the visual under its controller.
                // Configuration can run on either side of that lifecycle point,
                // so never move the visual twice when it already follows the
                // controller hierarchy.
                var wheelFollowsController = wheel.IsChildOf(controller);
                MoveInward(controller);
                if (!wheelFollowsController)
                    MoveInward(wheel);
                MoveInward(caliper);
                MoveDown(controller, chassisAndTireDrop);
                if (!wheelFollowsController)
                    MoveDown(wheel, chassisAndTireDrop);
                MoveDown(caliper, chassisAndTireDrop);
                var wheelController = BindRollingVisual(controller, wheel);
                bindings.Add(new WheelVisualBinding(wheelController, wheel));
                correctedCorners++;
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Porsche911GT3RS wheel geometry vehicle={GetInstanceID()} corner={index} " +
                    $"could not be inset: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (correctedCorners == CornerNames.GetLength(0))
        {
            Porsche911GT3RSDiagnostics.Info(
                context,
                $"Porsche911GT3RS wheel geometry vehicle={GetInstanceID()}: moved " +
                $"{correctedCorners} complete wheel/controller/caliper assemblies inward by " +
                $"{WheelAssemblyInsetMeters:F3}m and lowered the complete chassis/wheel datum by " +
                $"{chassisAndTireDrop:F3}m; rolling assemblies bound to steering controllers.");
        }
        else
        {
            context?.Logger.Warn(
                $"Porsche911GT3RS wheel geometry vehicle={GetInstanceID()}: corrected " +
                $"{correctedCorners}/{CornerNames.GetLength(0)} wheel assemblies.");
        }
        return correctedCorners;
    }

    private Transform? FindTransform(string name)
    {
        foreach (var candidate in GetComponentsInChildren<Transform>(true))
            if (string.Equals(candidate.name, name, StringComparison.Ordinal))
                return candidate;
        return null;
    }

    private void MoveInward(Transform target)
    {
        var side = Mathf.Sign(transform.InverseTransformPoint(target.position).x);
        if (Mathf.Approximately(side, 0f))
            throw new InvalidOperationException($"'{target.name}' has no lateral side.");
        var worldOffset = transform.right * (-side * WheelAssemblyInsetMeters);
        target.position += worldOffset;
    }

    private void MoveDown(Transform target, float distance) =>
        target.position -= transform.up * distance;

    private static void RecenterRollingGeometry(Transform wheel, Transform caliper)
    {
        MeshFilter? wheelFilter = null;
        foreach (var filter in wheel.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (filter.sharedMesh == null || renderer == null)
                continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && material.name.IndexOf(
                        "wheels_chrome_1",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    wheelFilter = filter;
                    break;
                }
            }
            if (wheelFilter != null)
                break;
        }

        if (wheelFilter?.sharedMesh == null)
            return;
        var mesh = wheelFilter.sharedMesh;
        var vertices = mesh.vertices;
        if (vertices.Length == 0)
            return;

        var parent = new int[vertices.Length];
        for (var index = 0; index < parent.Length; index++)
            parent[index] = index;
        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            var triangles = mesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                Union(parent, triangles[index], triangles[index + 1]);
                Union(parent, triangles[index + 1], triangles[index + 2]);
            }
        }

        var componentMinimum = new Dictionary<int, Vector3>();
        var componentMaximum = new Dictionary<int, Vector3>();
        for (var index = 0; index < vertices.Length; index++)
        {
            var local = wheel.InverseTransformPoint(
                wheelFilter.transform.TransformPoint(vertices[index]));
            var root = Find(parent, index);
            if (!componentMinimum.TryGetValue(root, out var minimum))
            {
                componentMinimum[root] = local;
                componentMaximum[root] = local;
            }
            else
            {
                componentMinimum[root] = Vector3.Min(minimum, local);
                componentMaximum[root] = Vector3.Max(componentMaximum[root], local);
            }
        }

        var bestRadialDiameter = 0f;
        var bestRadialCenter = Vector2.zero;
        foreach (var pair in componentMinimum)
        {
            var maximum = componentMaximum[pair.Key];
            var size = maximum - pair.Value;
            var radialDiameter = Mathf.Min(size.y, size.z);
            if (radialDiameter <= bestRadialDiameter)
                continue;
            bestRadialDiameter = radialDiameter;
            bestRadialCenter = new Vector2(
                (pair.Value.y + maximum.y) * 0.5f,
                (pair.Value.z + maximum.z) * 0.5f);
        }

        if (bestRadialDiameter <= 0.001f)
            return;
        var localCorrection = new Vector3(
            0f,
            -bestRadialCenter.x,
            -bestRadialCenter.y);
        var worldCorrection = wheel.TransformVector(localCorrection);
        for (var index = 0; index < wheel.childCount; index++)
            wheel.GetChild(index).localPosition += localCorrection;
        for (var index = 0; index < caliper.childCount; index++)
            caliper.GetChild(index).position += worldCorrection;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }
        return index;
    }

    private static void Union(int[] parent, int first, int second)
    {
        var firstRoot = Find(parent, first);
        var secondRoot = Find(parent, second);
        if (firstRoot != secondRoot)
            parent[secondRoot] = firstRoot;
    }

    private static NWH.WheelController3D.WheelController BindRollingVisual(
        Transform controller,
        Transform wheel)
    {
        var wheelController =
            controller.GetComponent<NWH.WheelController3D.WheelController>();
        if (wheelController == null)
            throw new InvalidOperationException(
                $"'{controller.name}' has no NWH wheel controller.");
        wheelController.wheel.visual = wheel.gameObject;
        wheelController.wheel.visualTransform = wheel;
        if (wheel.parent != controller)
            wheel.SetParent(controller, true);
        return wheelController;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static bool IsFinite(Quaternion value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
        !float.IsNaN(value.w) && !float.IsInfinity(value.w);

    private sealed class WheelVisualBinding
    {
        internal WheelVisualBinding(
            NWH.WheelController3D.WheelController controller,
            Transform visual)
        {
            Controller = controller;
            Visual = visual;
        }

        internal NWH.WheelController3D.WheelController Controller { get; }
        internal Transform Visual { get; }
    }
}

[AddComponentMenu("")]
public sealed class Porsche911GT3RSCollisionSeparationController : MonoBehaviour
{
    private const float MinimumPenetration = 0.025f;
    private const float SeparationPadding = 0.015f;
    private const float MaximumCorrectionPerPass = 0.18f;
    private const int MaximumPasses = 3;

    private readonly List<Collider> bodyColliders = new List<Collider>(2);
    private VehicleController? vehicle;
    private Rigidbody? body;
    private Coroutine? separationCoroutine;

    internal void Initialize(VehicleController controller)
    {
        if (vehicle == controller && bodyColliders.Count > 0)
            return;

        vehicle = controller;
        body = controller.GetComponent<Rigidbody>();
        bodyColliders.Clear();
        foreach (var transform in controller.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;
            foreach (var collider in transform.GetComponents<Collider>())
                if (collider != null && collider.enabled && !collider.isTrigger)
                    bodyColliders.Add(collider);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || body == null || collision?.collider == null)
            return;
        var otherVehicle = collision.collider.GetComponentInParent<VehicleController>();
        if (otherVehicle == null || otherVehicle == vehicle)
            return;

        if (separationCoroutine != null)
            StopCoroutine(separationCoroutine);
        separationCoroutine = StartCoroutine(ResolveVehiclePenetration(otherVehicle));
    }

    private IEnumerator ResolveVehiclePenetration(VehicleController otherVehicle)
    {
        yield return new WaitForFixedUpdate();
        var otherColliders = otherVehicle != null
            ? otherVehicle.GetComponentsInChildren<Collider>(true)
            : Array.Empty<Collider>();
        for (var pass = 0; pass < MaximumPasses; pass++)
        {
            if (body == null || otherVehicle == null)
                break;

            var bestDirection = Vector3.zero;
            var bestDistance = 0f;
            foreach (var ownCollider in bodyColliders)
            {
                if (ownCollider == null || !ownCollider.enabled)
                    continue;
                foreach (var otherCollider in otherColliders)
                {
                    if (otherCollider == null || !otherCollider.enabled ||
                        otherCollider.isTrigger ||
                        otherCollider.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                            ownCollider,
                            ownCollider.transform.position,
                            ownCollider.transform.rotation,
                            otherCollider,
                            otherCollider.transform.position,
                            otherCollider.transform.rotation,
                            out var direction,
                            out var distance) ||
                        distance <= bestDistance)
                    {
                        continue;
                    }

                    bestDirection = direction;
                    bestDistance = distance;
                }
            }

            if (bestDistance <= MinimumPenetration || bestDirection.sqrMagnitude < 0.5f)
                break;

            var correction = Mathf.Min(
                bestDistance + SeparationPadding,
                MaximumCorrectionPerPass);
            body.position += bestDirection.normalized * correction;
            var inwardSpeed = Vector3.Dot(body.velocity, -bestDirection.normalized);
            if (inwardSpeed > 0f)
                body.velocity += bestDirection.normalized * inwardSpeed;
            body.WakeUp();
            yield return new WaitForFixedUpdate();
        }
        separationCoroutine = null;
    }

    private void OnDisable()
    {
        if (separationCoroutine != null)
            StopCoroutine(separationCoroutine);
        separationCoroutine = null;
    }
}
#endif

[AddComponentMenu("")]
internal sealed class Porsche911GT3RSContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("Porsche 911 GT3 RS body contact")
        {
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        return contactMaterial;
    }

    private void OnDestroy()
    {
        if (contactMaterial != null)
            Destroy(contactMaterial);
        contactMaterial = null;
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
            Porsche911GT3RSDiagnostics.Info(
                context,
                $"Porsche911GT3RS glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            Porsche911GT3RSDiagnostics.Info(
                context,
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
    private const float RearDentLateralRadius = 0.88f;
    private const float RearDentVerticalRadius = 0.72f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.42f;
    private const float RearDepthPerExcessMps = 0.013f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Mesh> damageMeshes =
        new Dictionary<MeshFilter, Mesh>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private float previousSavedDamage;
    private int diagnosticLogs;
    private bool initialized;
    private bool failureReported;
    private Coroutine? repairRecoveryCoroutine;

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
        previousSavedDamage = controller.vehicleInstance?.damage ?? 0f;
        deformableFilters.Clear();
        originalVertices.Clear();
        damageMeshes.Clear();
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
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
        }
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || damageHandler == null)
            return;

        var currentDamage = damageHandler.Damage;
        var currentSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        if ((previousDamage > 0.001f && currentDamage <= 0.001f) ||
            (previousSavedDamage > 0.001f && currentSavedDamage <= 0.001f))
        {
            foreach (var pair in originalVertices)
            {
                if (pair.Key == null || !damageMeshes.TryGetValue(pair.Key, out var mesh) ||
                    mesh == null)
                    continue;
                // CarController.Repair() invokes the disabled legacy deformation
                // component, which swaps its serialized source mesh back onto the
                // filter. Restore this vehicle-owned mesh before resetting it so
                // later impacts never mutate the shared prefab asset.
                pair.Key.sharedMesh = mesh;
                mesh.vertices = pair.Value;
                mesh.RecalculateBounds();
            }
            if (repairRecoveryCoroutine != null)
                StopCoroutine(repairRecoveryCoroutine);
            repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
            Porsche911GT3RSDiagnostics.DamageInfo(
                context,
                $"Porsche911GT3RS damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
        previousDamage = currentDamage;
        previousSavedDamage = currentSavedDamage;
    }

    private IEnumerator RestoreDrivingStateAfterRepair()
    {
        yield return null;
        for (var pass = 0; pass < 3; pass++)
        {
            yield return new WaitForFixedUpdate();
            if (vehicle == null || !vehicle.controlledByPlayer)
                continue;

            vehicle.SetFreeze(false);
            var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            if (physics != null)
            {
                physics.enabled = true;
                if (!physics.powertrain.engine.IsRunning)
                    physics.powertrain.engine.StartEngine();
                if (physics.powertrain.transmission.Gear == 0)
                    physics.powertrain.transmission.ShiftInto(1, true);
            }
            foreach (var wheelController in
                     vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
                wheelController.enabled = true;
            var rigidbody = vehicle.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = false;
                rigidbody.WakeUp();
            }
        }
        repairRecoveryCoroutine = null;
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
                var attachedDetail = IsAttachedExteriorDetail(filter, vertices.Length);
                var appliedWorldDisplacements = attachedDetail
                    ? new Vector3[vertices.Length]
                    : null;
                var totalWorldDisplacement = Vector3.zero;
                var changedVerticesInMesh = 0;
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
                    var worldDisplacement = inwardDirection * (selectedDepth * falloff);
                    worldVertex += worldDisplacement;
                    vertices[vertexIndex] = filter.transform.InverseTransformPoint(worldVertex);
                    if (appliedWorldDisplacements != null)
                        appliedWorldDisplacements[vertexIndex] = worldDisplacement;
                    totalWorldDisplacement += worldDisplacement;
                    changedVerticesInMesh++;
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;
                if (attachedDetail && appliedWorldDisplacements != null &&
                    changedVerticesInMesh > 0)
                {
                    // Lamps, badges, vents, fasteners, and similar separate
                    // pieces must remain attached to the panel. Translate the
                    // whole small mesh by the sampled regional deformation
                    // instead of leaving unaffected vertices hovering behind.
                    var averageDisplacement =
                        totalWorldDisplacement / changedVerticesInMesh;
                    for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        var undeformedWorld = filter.transform.TransformPoint(vertices[vertexIndex]) -
                                              appliedWorldDisplacements[vertexIndex];
                        vertices[vertexIndex] = filter.transform.InverseTransformPoint(
                            undeformedWorld + averageDisplacement);
                    }
                    changedVertices += vertices.Length - changedVerticesInMesh;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                changedMeshes++;
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                Porsche911GT3RSDiagnostics.DamageInfo(
                    context,
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

    private static bool IsAttachedExteriorDetail(MeshFilter filter, int vertexCount)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        return vertexCount <= 400 || longestSide <= 0.32f;
    }

    private void OnDestroy()
    {
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = null;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
        damageMeshes.Clear();
    }
}

