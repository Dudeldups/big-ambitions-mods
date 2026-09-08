#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BigfootMonsterTruckRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 24;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 6500f;
    private const float WheelRadius = 0.78f;
    private const float WheelWidth = 1.05f;
    private const float SuspensionLength = 0.65f;
    private const float SuspensionForce = 28000f;
    private const float FrontAxleZ = 1.60f;
    private const float RearAxleZ = -1.60f;
    private const float HalfTrack = 1.35f;
    private const float AxleHeight = 0.92f;
    private const float CenterOfMassHeight = 0.72f;
    private const float DriverSeatHeight = 2.02f;
    private const float BrakeTorque = 32000f;
    private const float AntiRollForce = 4200f;
    private const float TargetTopSpeed = 140f;
    private const float EngineRevLimiter = 6500f;
    private const float TransmissionFinalDrive = 13.6f;
    private static readonly float[] TransmissionGears =
    {
        -3.6f,
        0f,
        4.2f,
        2.5f,
        1.55f,
        1f,
    };

    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private Coroutine? initializationCoroutine;

    public static BigfootMonsterTruckRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<BigfootMonsterTruckRuntime>();
        if (runtime == null)
        {
            var host = new GameObject(nameof(BigfootMonsterTruckRuntime));
            DontDestroyOnLoad(host);
            runtime = host.AddComponent<BigfootMonsterTruckRuntime>();
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
        foreach (var marker in FindObjectsOfType<BigfootMonsterTruckConfigured>(true))
            if (marker != null)
                Destroy(marker);
        foreach (var driver in FindObjectsOfType<BigfootMonsterTruckDriverController>(true))
            if (driver != null)
                Destroy(driver);
        foreach (var guard in FindObjectsOfType<BigfootMonsterTruckCollisionGuard>(true))
            if (guard != null)
                Destroy(guard);
        foreach (var audioController in FindObjectsOfType<BigfootMonsterTruckAudioController>(true))
            if (audioController != null)
                Destroy(audioController);
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
        if (initializationCoroutine == null)
            return;
        StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
    }

    private void HandleVehicleEntered(VehicleController vehicle) => TryConfigureVehicle(vehicle);

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (string.Equals(
                registration?.BusinessName,
                BigfootTruckDealerStock.DealerContactId,
                StringComparison.Ordinal))
        {
            BigfootTruckDealerStock.EnsureVehicleAvailable(
                vehicleTypeName,
                context,
                "truck-dealer-entered");
        }
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen)
            BigfootTruckDealerStock.EnsureVehicleAvailable(vehicleTypeName, context, "menu-opened");
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
        var configuredTotal = 0;
        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            dealerReady |= BigfootTruckDealerStock.EnsureVehicleAvailable(
                vehicleTypeName,
                context,
                source);
            configuredTotal += ConfigureExistingVehicles();
            if (dealerReady && attempt >= 8)
                break;
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (dealerReady)
            context?.Logger.Info(
                $"BigfootMonsterTruck: lifecycle ready source='{source}', " +
                $"newlyConfigured={configuredTotal}.");
        else
            context?.Logger.Warn(
                $"BigfootMonsterTruck: truck dealer data was not ready source='{source}'.");
    }

    private int ConfigureExistingVehicles()
    {
        var configured = 0;
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return configured;
        foreach (var vehicle in vehicles)
            if (TryConfigureVehicle(vehicle))
                configured++;
        return configured;
    }

    private bool TryConfigureVehicle(VehicleController? vehicle)
    {
        if (vehicle?.vehicleInstance == null ||
            !string.Equals(vehicle.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal))
            return false;
        if (vehicle.GetComponent<BigfootMonsterTruckConfigured>() != null)
            return false;

        var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.mass = VehicleMass;
            rigidbody.centerOfMass = new Vector3(0f, CenterOfMassHeight, 0f);
            rigidbody.drag = 0.02f;
            rigidbody.angularDrag = 0.12f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        var contactMaterial = new PhysicMaterial("Bigfoot low-friction contact")
        {
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        var wheelCount = ConfigureWheels(vehicle);
        var colliderCount = ConfigureBodyColliders(vehicle, contactMaterial);
        var wheelColliderCount = ConfigureWheelContactColliders(vehicle, contactMaterial);
        ConfigureVehicleModules(vehicle);
        ConfigureExitMarkers(vehicle);
        ConfigureDriverSeat(vehicle);
        var materialFix = BigfootMonsterTruckMaterials.FixSolidMaterials(vehicle.gameObject);
        context?.Logger.Info(
            $"BigfootMonsterTruck materials vehicle={vehicle.GetInstanceID()}: " +
            $"renderers={materialFix.RendererCount}, " +
            $"decalMasksCleared={materialFix.DecalMasksCleared}, " +
            $"opaqueFixed={materialFix.OpaqueMaterialsFixed}, " +
            $"hdrpValidated={materialFix.MaterialsValidated}.");
        if (materialFix.MaterialsValidated < materialFix.OpaqueMaterialsFixed)
            context?.Logger.Warn(
                $"BigfootMonsterTruck materials vehicle={vehicle.GetInstanceID()}: " +
                "HDRP validation was unavailable for one or more opaque materials.");

        var driver = vehicle.gameObject.AddComponent<BigfootMonsterTruckDriverController>();
        driver.Initialize(vehicle, context);
        var collisionGuard = vehicle.gameObject.AddComponent<BigfootMonsterTruckCollisionGuard>();
        collisionGuard.Initialize(vehicle, context);
        var audioController = vehicle.gameObject.AddComponent<BigfootMonsterTruckAudioController>();
        audioController.Initialize(vehicle, context);
        var marker = vehicle.gameObject.AddComponent<BigfootMonsterTruckConfigured>();
        marker.Initialize(contactMaterial);
        context?.Logger.Info(
            $"BigfootMonsterTruck: configured vehicle id={vehicle.GetInstanceID()}, " +
            $"mass={VehicleMass:F0}, wheels={wheelCount}, colliders={colliderCount}, " +
            $"wheelColliders={wheelColliderCount}, " +
            $"power={vehicle.vehicleType?.enginePower ?? 0f:F0}, " +
            $"topSpeed={vehicle.vehicleType?.maxSpeed ?? 0}km/h, " +
            $"gearbox=4-speed/{TransmissionFinalDrive:F1}:1, " +
            $"turnRadius={(float)(vehicle.vehicleType?.turnRadius ?? 0):F1}, " +
            $"wheelRadius={WheelRadius:F2}, wheelWidth={WheelWidth:F2}, " +
            $"suspensionTravel={SuspensionLength:F2}, differentialSlip=1000, " +
            "climbAssist=small-vehicles, wheelTrafficContact=false.");
        if (wheelCount != 4)
            context?.Logger.Warn(
                $"BigfootMonsterTruck: expected four wheel controllers but configured {wheelCount}.");
        return true;
    }

    private int ConfigureWheels(VehicleController vehicle)
    {
        var count = 0;
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (!TryGetWheelPosition(transform.name, out var position))
                continue;
            transform.localPosition = position;
            count++;

            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(spring, "maxLength", SuspensionLength);
                SetFloat(spring, "maxForce", SuspensionForce);
                SetMember(component, "spring", spring);

                var damper = GetMember(component, "damper");
                SetFloat(damper, "bumpRate", 22000f);
                SetFloat(damper, "reboundRate", 26000f);
                SetMember(component, "damper", damper);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", WheelRadius);
                SetFloat(wheel, "width", WheelWidth);
                SetFloat(wheel, "mass", 120f);
                SetMember(component, "wheel", wheel);

                var sideFriction = GetMember(component, "sideFriction");
                SetFloat(sideFriction, "grip", 1.0f);
                SetFloat(sideFriction, "stiffness", 1.12f);
                SetMember(component, "sideFriction", sideFriction);

                var forwardFriction = GetMember(component, "forwardFriction");
                SetFloat(forwardFriction, "grip", 1.2f);
                SetFloat(forwardFriction, "stiffness", 1.15f);
                SetMember(component, "forwardFriction", forwardFriction);

                SetFloat(component, "loadRating", 30000f);
                SetFloat(component, "rollingResistanceTorque", 90f);
                SetFloat(component, "forceApplicationPointDistance", 0.5f);
                // Wheel raycasts against traffic vehicles made their rigidbodies latch
                // together and drag the truck. Body colliders handle vehicle impacts.
                SetFloat(component, "otherBodyForceScale", 1f);
            }
        }

        return count;
    }

    private static bool TryGetWheelPosition(string name, out Vector3 position)
    {
        if (!name.EndsWith("_WheelController", StringComparison.Ordinal))
        {
            position = default;
            return false;
        }

        if (name.IndexOf("FrontLeft", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(-HalfTrack, AxleHeight, FrontAxleZ);
        else if (name.IndexOf("FrontRight", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(HalfTrack, AxleHeight, FrontAxleZ);
        else if (name.IndexOf("RearLeft", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(-HalfTrack, AxleHeight, RearAxleZ);
        else if (name.IndexOf("RearRight", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(HalfTrack, AxleHeight, RearAxleZ);
        else
        {
            position = default;
            return false;
        }
        return true;
    }

    private static int ConfigureBodyColliders(VehicleController vehicle, PhysicMaterial contactMaterial)
    {
        var count = 0;
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;
            var colliders = transform.GetComponents<BoxCollider>();
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (index == 0)
                {
                    collider.center = new Vector3(0f, 1.2f, -0.05f);
                    collider.size = new Vector3(2.5f, 0.55f, 5.3f);
                }
                else
                {
                    collider.center = new Vector3(0f, 2.05f, 0.1f);
                    collider.size = new Vector3(2.2f, 1.2f, 3.7f);
                }
                collider.sharedMaterial = contactMaterial;
                count++;
            }
        }
        return count;
    }

    private static int ConfigureWheelContactColliders(
        VehicleController vehicle,
        PhysicMaterial contactMaterial)
    {
        var holder = vehicle.transform.Find("BigfootWheelContactColliders");
        if (holder == null)
        {
            var holderObject = new GameObject("BigfootWheelContactColliders")
            {
                layer = 12,
                tag = "Wheel",
            };
            holder = holderObject.transform;
            holder.SetParent(vehicle.transform, false);
        }

        foreach (var box in holder.GetComponents<BoxCollider>())
        {
            box.enabled = false;
            Destroy(box);
        }

        var colliders = holder.GetComponents<SphereCollider>();
        while (colliders.Length < 4)
        {
            holder.gameObject.AddComponent<SphereCollider>();
            colliders = holder.GetComponents<SphereCollider>();
        }

        var centers = new[]
        {
            new Vector3(-HalfTrack, 0.58f, FrontAxleZ),
            new Vector3(HalfTrack, 0.58f, FrontAxleZ),
            new Vector3(-HalfTrack, 0.58f, RearAxleZ),
            new Vector3(HalfTrack, 0.58f, RearAxleZ),
        };
        for (var index = 0; index < colliders.Length; index++)
        {
            colliders[index].enabled = index < centers.Length;
            if (index >= centers.Length)
                continue;
            colliders[index].isTrigger = false;
            colliders[index].center = centers[index];
            colliders[index].radius = 0.58f;
            colliders[index].sharedMaterial = contactMaterial;
        }
        return centers.Length;
    }

    private static void ConfigureExitMarkers(VehicleController vehicle)
    {
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
                transform.localPosition = new Vector3(-2.05f, 0.1f, 0.1f);
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
                transform.localPosition = new Vector3(2.05f, 0.1f, 0.1f);
        }
    }

    private static void ConfigureDriverSeat(VehicleController vehicle)
    {
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BigfootDriverSeat", StringComparison.Ordinal))
                continue;
            transform.localPosition = new Vector3(0f, DriverSeatHeight, 0.18f);
            return;
        }
    }

    private void ConfigureVehicleModules(VehicleController vehicle)
    {
        foreach (var component in vehicle.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            try
            {
                var type = component.GetType();
                if (string.Equals(type.FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                {
                    var powertrain = GetMember(component, "powertrain");
                    var engine = GetMember(powertrain, "engine");
                    SetFloat(engine, "maxPower", vehicle.vehicleType?.enginePower ?? 1200f);
                    SetFloat(engine, "revLimiterRPM", EngineRevLimiter);
                    SetMember(powertrain, "engine", engine);
                    var transmission = GetMember(powertrain, "transmission");
                    SetFloat(transmission, "finalGearRatio", TransmissionFinalDrive);
                    SetFloat(transmission, "_upshiftRPM", 5900f);
                    SetFloat(transmission, "_downshiftRPM", 2700f);
                    SetInt(transmission, "forwardGearCount", 4);
                    SetInt(transmission, "reverseGearCount", 1);
                    if (GetMember(transmission, "gears") is IList gears)
                    {
                        gears.Clear();
                        foreach (var ratio in TransmissionGears)
                            gears.Add(ratio);
                        SetMember(transmission, "gears", gears);
                    }
                    SetMember(powertrain, "transmission", transmission);
                    var differentials = GetMember(powertrain, "differentials") as IList;
                    if (differentials != null)
                    {
                        for (var index = 0; index < differentials.Count; index++)
                        {
                            var differential = differentials[index];
                            SetFloat(differential, "slipTorque", 1000f);
                            if (differential != null)
                                differentials[index] = differential;
                        }
                        SetMember(powertrain, "differentials", differentials);
                    }
                    VerifyFourWheelDrive(powertrain);
                    SetMember(component, "powertrain", powertrain);

                    var steering = GetMember(component, "steering");
                    SetFloat(steering, "maximumSteerAngle", 28f);
                    SetMember(component, "steering", steering);
                }
                else if (string.Equals(type.Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                {
                    var speedLimiter = GetMember(component, "module");
                    SetFloat(speedLimiter, "speedLimit", TargetTopSpeed);
                    SetMember(component, "module", speedLimiter);
                }

                var brakes = GetMember(component, "brakes");
                SetFloat(brakes, "maxTorque", BrakeTorque);
                SetFloat(brakes, "actuationTime", 0.1f);
                SetMember(component, "brakes", brakes);
                SetFloat(component, "baseMass", VehicleMass);

                var wheelGroups = GetMember(component, "wheelGroups") as IList;
                if (wheelGroups != null)
                {
                    for (var index = 0; index < wheelGroups.Count; index++)
                    {
                        var group = wheelGroups[index];
                        SetFloat(group, "antiRollBarForce", AntiRollForce);
                        SetFloat(group, "brakeCoefficient", index == 0 ? 1f : 0.85f);
                        if (index > 0)
                            SetFloat(group, "handbrakeCoefficient", 1.8f);
                        if (group != null && group.GetType().IsValueType)
                            wheelGroups[index] = group;
                    }
                    SetMember(component, "wheelGroups", wheelGroups);
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"BigfootMonsterTruck: module tuning failed for '{component.GetType().Name}': " +
                    exception.Message);
            }
        }
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;
        var type = target.GetType();
        return FindField(type, name)?.GetValue(target) ??
               type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   ?.GetValue(target);
    }

    private void VerifyFourWheelDrive(object? powertrain)
    {
        if (powertrain == null ||
            GetMember(powertrain, "engine") is not object engine ||
            GetMember(powertrain, "transmission") is not object transmission ||
            GetMember(powertrain, "differentials") is not IList differentials ||
            GetMember(powertrain, "wheels") is not IList wheels)
        {
            context?.Logger.Warn("BigfootMonsterTruck: four-wheel-drive components are unavailable.");
            return;
        }

        var center = FindNamedComponent(differentials, "Center Differential");
        var front = FindNamedComponent(differentials, "Front Differential");
        var rear = FindNamedComponent(differentials, "Rear Differential");
        var frontLeft = FindNamedComponent(wheels, "WheelFrontLeft_WheelController");
        var frontRight = FindNamedComponent(wheels, "WheelFrontRight_WheelController");
        var rearLeft = FindNamedComponent(wheels, "WheelRearLeft_WheelController");
        var rearRight = FindNamedComponent(wheels, "WheelRearRight_WheelController");
        if (center == null || front == null || rear == null || frontLeft == null ||
            frontRight == null || rearLeft == null || rearRight == null)
        {
            context?.Logger.Warn("BigfootMonsterTruck: four-wheel-drive topology is incomplete.");
            return;
        }

        var verified = ReferenceEquals(GetMember(engine, "Output"), transmission) &&
                       ReferenceEquals(GetMember(transmission, "Output"), center) &&
                       ReferenceEquals(GetMember(center, "Output"), front) &&
                       ReferenceEquals(GetMember(center, "OutputB"), rear) &&
                       ReferenceEquals(GetMember(front, "Output"), frontLeft) &&
                       ReferenceEquals(GetMember(front, "OutputB"), frontRight) &&
                       ReferenceEquals(GetMember(rear, "Output"), rearLeft) &&
                       ReferenceEquals(GetMember(rear, "OutputB"), rearRight);
        if (verified)
            context?.Logger.Info(
                "BigfootMonsterTruck: prefab four-wheel-drive topology verified without runtime rewiring.");
        else
            context?.Logger.Warn("BigfootMonsterTruck: four-wheel-drive topology did not verify.");
    }

    private static object? FindNamedComponent(IList components, string name)
    {
        foreach (var component in components)
            if (component != null && string.Equals(
                    GetMember(component, "name") as string,
                    name,
                    StringComparison.Ordinal))
                return component;
        return null;
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
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static bool SetFloat(object? target, string name, float value)
    {
        if (target == null)
            return false;
        var field = FindField(target.GetType(), name);
        if (field?.FieldType == typeof(float))
        {
            field.SetValue(target, value);
            return true;
        }
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType == typeof(float))
        {
            property.SetValue(target, value);
            return true;
        }
        return false;
    }

    private static bool SetInt(object? target, string name, int value)
    {
        if (target == null)
            return false;
        var field = FindField(target.GetType(), name);
        if (field?.FieldType == typeof(int))
        {
            field.SetValue(target, value);
            return true;
        }
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType == typeof(int))
        {
            property.SetValue(target, value);
            return true;
        }
        return false;
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
}

internal sealed class BigfootMonsterTruckConfigured : MonoBehaviour
{
    private PhysicMaterial? ownedContactMaterial;

    public void Initialize(PhysicMaterial contactMaterial) => ownedContactMaterial = contactMaterial;

    private void OnDestroy()
    {
        if (ownedContactMaterial != null)
            Destroy(ownedContactMaterial);
        ownedContactMaterial = null;
    }
}
