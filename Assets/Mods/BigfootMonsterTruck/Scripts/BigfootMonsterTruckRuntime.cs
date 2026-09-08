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
    private const float VehicleMass = 4500f;
    private const float WheelRadius = 0.81f;
    private const float WheelWidth = 1.0f;
    private const float SuspensionLength = 0.55f;
    private const float SuspensionForce = 28000f;
    private const float FrontAxleZ = 1.60f;
    private const float RearAxleZ = -1.60f;
    private const float HalfTrack = 1.35f;
    private const float AxleHeight = 0.82f;
    private const float CenterOfMassHeight = 0.72f;
    private const float BrakeTorque = 15000f;
    private const float AntiRollForce = 4200f;

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

        var wheelCount = ConfigureWheels(vehicle);
        var colliderCount = ConfigureBodyColliders(vehicle);
        ConfigureVehicleModules(vehicle);
        ConfigureExitMarkers(vehicle);

        var driver = vehicle.gameObject.AddComponent<BigfootMonsterTruckDriverController>();
        driver.Initialize(vehicle, context);
        vehicle.gameObject.AddComponent<BigfootMonsterTruckConfigured>();
        context?.Logger.Info(
            $"BigfootMonsterTruck: configured vehicle id={vehicle.GetInstanceID()}, " +
            $"mass={VehicleMass:F0}, wheels={wheelCount}, colliders={colliderCount}, " +
            $"wheelRadius={WheelRadius:F2}, suspensionTravel={SuspensionLength:F2}.");
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

                SetFloat(component, "loadRating", 20000f);
                SetFloat(component, "rollingResistanceTorque", 90f);
                SetFloat(component, "forceApplicationPointDistance", 0.5f);
            }
        }

        return count;
    }

    private static bool TryGetWheelPosition(string name, out Vector3 position)
    {
        switch (name)
        {
            case "FrontLeft_WheelController":
                position = new Vector3(-HalfTrack, AxleHeight, FrontAxleZ);
                return true;
            case "FrontRight_WheelController":
                position = new Vector3(HalfTrack, AxleHeight, FrontAxleZ);
                return true;
            case "RearLeft_WheelController":
                position = new Vector3(-HalfTrack, AxleHeight, RearAxleZ);
                return true;
            case "RearRight_WheelController":
                position = new Vector3(HalfTrack, AxleHeight, RearAxleZ);
                return true;
            default:
                position = default;
                return false;
        }
    }

    private static int ConfigureBodyColliders(VehicleController vehicle)
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
                    collider.center = new Vector3(0f, 1.05f, 0f);
                    collider.size = new Vector3(2.45f, 1.0f, 5.3f);
                }
                else
                {
                    collider.center = new Vector3(0f, 2.0f, 0.2f);
                    collider.size = new Vector3(1.95f, 1.25f, 2.8f);
                }
                count++;
            }
        }
        return count;
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
                    SetFloat(engine, "maxPower", vehicle.vehicleType?.enginePower ?? 500f);
                }

                var brakes = GetMember(component, "brakes");
                SetFloat(brakes, "maxTorque", BrakeTorque);
                SetFloat(brakes, "actuationTime", 0.1f);
                SetMember(component, "brakes", brakes);

                var wheelGroups = GetMember(component, "wheelGroups") as IList;
                if (wheelGroups != null)
                {
                    for (var index = 0; index < wheelGroups.Count; index++)
                    {
                        var group = wheelGroups[index];
                        SetFloat(group, "antiRollBarForce", AntiRollForce);
                        SetFloat(group, "brakeCoefficient", index == 0 ? 0.72f : 0.55f);
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
}
