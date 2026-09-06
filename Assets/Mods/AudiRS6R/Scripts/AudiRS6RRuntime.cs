#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

public sealed class AudiRS6RRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStableInitializationPasses = 8;
    private const float InitializationRetryDelay = 0.25f;
    private const float AntiRollBarForce = 6500f;
    private const float BrakeActuationTime = 0.06f;
    private const float BrakeMaxTorque = 18000f;
    private const float CenterOfMassHeight = 0.25f;
    private const float DamageIntensity = 0.5f;
    private const float DeformationRadius = 0.32f;
    private const float DeformationStrength = 0.35f;
    private const float DriverExitLocalX = -1.5f;
    private const float ExitLocalY = 0.1f;
    private const float ExitLocalZ = 0.117f;
    private const float FuelConsumptionMultiplier = 20f;
    private const float FuelIdleConsumption = 0.045f;
    private const float HandbrakeCoefficient = 2f;
    private const float LowerBodyColliderCenterY = 0.6f;
    private const float LowerBodyColliderHeight = 0.62f;
    private const float PassengerExitLocalX = 1.5f;
    private const float RearBrakeCoefficient = 0.55f;
    private const float SuspensionMaxLength = 0.25f;
    private const float VehicleLinearDrag = 0f;
    private const float VehicleMass = 2050f;
    private const float VisualBodyLocalHeight = 0.08f;

    private static readonly string[] VehicleLightGroupFieldNames =
    {
        "brakeLights",
        "extraLights",
        "highBeamLights",
        "leftBlinkers",
        "lowBeamLights",
        "reverseLights",
        "rightBlinkers",
        "tailLights"
    };

    private Coroutine? initializationCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;

    public static AudiRS6RRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<AudiRS6RRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(AudiRS6RRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<AudiRS6RRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.SubscribeGlobalEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        return runtime;
    }

    public void Shutdown()
    {
        if (initializationCoroutine != null)
        {
            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }

        RemoveVehicleRuntimeComponents();
        Destroy(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeGlobalEvents();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeGlobalEvents();
    }

    private void SubscribeGlobalEvents()
    {
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onBuildingRegistrationChange += HandleBuildingRegistrationChanged;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void UnsubscribeGlobalEvents()
    {
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // GlobalEvents delegates can be reset during a game-state transition.
        SubscribeGlobalEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeGlobalEvents();
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
        {
            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }
    }

    private void HandleVehicleEntered(VehicleController vehicleController)
    {
        TryConfigureVehicle(vehicleController);
    }

    private void HandleBuildingEntered(Address address)
    {
        RefreshDealerStockForAddress(address, "dealer-entered");
    }

    private void HandleBuildingRegistrationChanged(Address address)
    {
        RefreshDealerStockForAddress(address, "dealer-registration-changed");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen)
            return;

        if (!AudiRS6RLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName, context))
            context?.Logger.Warn("AudiRS6R: luxury dealer catalog was not ready when the full menu opened.");
    }

    private void RefreshDealerStockForAddress(Address address, string source)
    {
        if (address == null)
            return;

        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!AudiRS6RLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            return;

        var ready = AudiRS6RLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName, context);
        if (!ready)
            context?.Logger.Warn($"AudiRS6R: luxury dealer catalog was not ready source='{source}'.");
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
        var attempts = 0;
        var maximumMatchedCount = 0;
        var configuredCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            attempts = attempt;
            dealerReady |= AudiRS6RLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName, context);
            EnsureVehiclesConfigured(out var matchedCount, out var configuredThisPass);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);
            configuredCount += configuredThisPass;

            if (dealerReady && matchedCount == previousMatchedCount)
                stablePasses++;
            else
                stablePasses = 0;

            previousMatchedCount = matchedCount;
            if (dealerReady && stablePasses >= RequiredStableInitializationPasses)
                break;

            if (attempt < InitializationRetryCount)
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"AudiRS6R: lifecycle initialization did not find dealer stock source='{source}', " +
                $"attempts={attempts}, matchedVehicles={maximumMatchedCount}, configuredVehicles={configuredCount}.");
        }
    }

    private void EnsureVehiclesConfigured(out int matchedCount, out int configuredCount)
    {
        matchedCount = 0;
        configuredCount = 0;
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
            return;

        var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
        if (allPlayerVehicles == null)
            return;

        foreach (var vehicleController in allPlayerVehicles)
        {
            if (vehicleController?.vehicleInstance == null ||
                !string.Equals(vehicleController.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            matchedCount++;
            if (TryConfigureVehicle(vehicleController))
                configuredCount++;
        }
    }

    private bool TryConfigureVehicle(VehicleController? vehicleController)
    {
        if (vehicleController?.vehicleInstance == null ||
            !string.Equals(vehicleController.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        var sleepConfigured = ConfigureSleepEnvironment(vehicleController);
        var roadDamageGuard = vehicleController.GetComponent<AudiRS6RRoadDamageGuard>();
        var lightingController = vehicleController.GetComponent<AudiRS6RLightingController>();
        var addedRoadDamageGuard = roadDamageGuard == null;
        var addedLightingController = lightingController == null;

        if (addedRoadDamageGuard)
        {
            ConfigureVehiclePhysics(vehicleController);
            roadDamageGuard = vehicleController.gameObject.AddComponent<AudiRS6RRoadDamageGuard>();
        }

        if (addedLightingController)
            lightingController = vehicleController.gameObject.AddComponent<AudiRS6RLightingController>();

        lightingController!.Initialize(vehicleController, context);
        roadDamageGuard!.Initialize(vehicleController);

        var driverController = vehicleController.GetComponent<AudiRS6RDriverController>();
        var addedDriverController = driverController == null;
        if (addedDriverController)
            driverController = vehicleController.gameObject.AddComponent<AudiRS6RDriverController>();
        driverController!.Initialize(vehicleController, context);

        return sleepConfigured > 0 || addedRoadDamageGuard || addedLightingController || addedDriverController;
    }

    private void ConfigureVehiclePhysics(VehicleController vehicleController)
    {
        var rigidbody = vehicleController.GetComponent<Rigidbody>() ?? vehicleController.GetComponentInParent<Rigidbody>();
        ConfigureCenterOfMassModules(vehicleController);
        if (rigidbody != null)
        {
            rigidbody.centerOfMass = new Vector3(0f, CenterOfMassHeight, 0f);
            rigidbody.mass = VehicleMass;
            rigidbody.drag = VehicleLinearDrag;
        }

        ConfigureAccelerationDynamics(vehicleController);
        ConfigureVisualBodyHeight(vehicleController);
        ConfigureExitMarkers(vehicleController);
        ConfigureBodyColliders(vehicleController, out _, out _);
        ConfigureSuspension(vehicleController, out _, out _);
        ConfigureBrakes(vehicleController, out _, out _);
        ConfigureDamageHandlers(vehicleController);
        ConfigureDeformationControllers(vehicleController);
        ConfigureFuelConsumption(vehicleController);
        ConfigureLights(vehicleController, out _, out _, out _);
    }

    private static void ConfigureAccelerationDynamics(VehicleController vehicleController)
    {
        var targetEnginePower = vehicleController.vehicleType?.enginePower ?? 0f;
        var targetSpeedLimit = vehicleController.vehicleType?.maxSpeed ?? 0;

        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            var componentType = component.GetType();
            if (string.Equals(componentType.FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                var powertrain = GetMemberValue(component, "powertrain");
                var engine = GetMemberValue(powertrain, "engine");
                if (engine != null && targetEnginePower > 0f)
                    TrySetFloatMember(engine, "maxPower", targetEnginePower);
            }
            else if (string.Equals(componentType.Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                var module = GetMemberValue(component, "module") ?? InvokeNoArgumentMethod(component, "GetModule");
                if (module != null && targetSpeedLimit > 0)
                    TrySetFloatMember(module, "speedLimit", targetSpeedLimit);
            }
        }
    }

    private static int ConfigureCenterOfMassModules(VehicleController vehicleController)
    {
        var configuredCount = 0;
        var targetCenterOfMass = new Vector3(0f, CenterOfMassHeight, 0f);
        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            var configured = TrySetVector3Field(component, "centerOfMass", targetCenterOfMass);
            configured |= TrySetVector3Field(component, "combinedCenterOfMass", targetCenterOfMass);
            if (configured)
                configuredCount++;
        }

        return configuredCount;
    }

    private static int ConfigureVisualBodyHeight(VehicleController vehicleController)
    {
        var configuredCount = 0;
        foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child.parent == null ||
                !string.Equals(child.parent.name, "CarHolder", StringComparison.Ordinal) ||
                (!string.Equals(child.name, "Body", StringComparison.Ordinal) &&
                 !string.Equals(child.name, "Paint", StringComparison.Ordinal)))
            {
                continue;
            }

            var position = child.localPosition;
            position.y = VisualBodyLocalHeight;
            child.localPosition = position;
            configuredCount++;
        }

        return configuredCount;
    }

    private static int ConfigureExitMarkers(VehicleController vehicleController)
    {
        var configuredCount = 0;
        foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
        {
            if (child == null)
                continue;

            float localX;
            if (string.Equals(child.name, "Driverside", StringComparison.Ordinal))
                localX = DriverExitLocalX;
            else if (string.Equals(child.name, "Passengerside", StringComparison.Ordinal))
                localX = PassengerExitLocalX;
            else
                continue;

            child.localPosition = new Vector3(localX, ExitLocalY, ExitLocalZ);
            configuredCount++;
        }

        return configuredCount;
    }

    private static void ConfigureBodyColliders(
        VehicleController vehicleController,
        out int bodyColliderCount,
        out int adjustedCount)
    {
        bodyColliderCount = 0;
        adjustedCount = 0;

        foreach (var boxCollider in vehicleController.GetComponentsInChildren<BoxCollider>(true))
        {
            if (boxCollider == null ||
                !string.Equals(boxCollider.name, "BodyCollider", StringComparison.Ordinal))
            {
                continue;
            }

            bodyColliderCount++;
            if (boxCollider.center.y >= 0.8f)
                continue;

            var center = boxCollider.center;
            var size = boxCollider.size;
            center.y = LowerBodyColliderCenterY;
            size.y = LowerBodyColliderHeight;
            boxCollider.center = center;
            boxCollider.size = size;
            adjustedCount++;
        }
    }

    private int ConfigureDamageHandlers(VehicleController vehicleController)
    {
        var configuredCount = 0;
        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            try
            {
                var damageHandlerField = component.GetType().GetField(
                    "damageHandler",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var damageHandler = damageHandlerField?.GetValue(component);
                if (damageHandler != null && TrySetFloatField(damageHandler, "damageIntensity", DamageIntensity))
                    configuredCount++;
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not configure damage handler: {ex.Message}");
            }
        }

        return configuredCount;
    }

    private int ConfigureDeformationControllers(VehicleController vehicleController)
    {
        var configuredCount = 0;
        foreach (var component in vehicleController.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Name != "VehicleDeformationController")
                continue;

            try
            {
                var configured = TrySetFloatField(component, "deformationStrength", DeformationStrength);
                configured |= TrySetFloatField(component, "deformationRadius", DeformationRadius);
                if (configured)
                    configuredCount++;
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not configure deformation controller: {ex.Message}");
            }
        }

        return configuredCount;
    }

    private int ConfigureSleepEnvironment(VehicleController vehicleController)
    {
        try
        {
            var environmentField = typeof(VehicleController).GetField(
                "sleepEnvironment",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var environment = environmentField?.GetValue(vehicleController);
            if (environmentField == null || environment == null)
                return 0;

            var configField = environment.GetType().BaseType?.GetField(
                "config",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (configField == null)
                return 0;

            if (configField.GetValue(environment) is UnityEngine.Object currentConfig && currentConfig != null)
                return 0;

            UnityEngine.Object? carConfig = FindLoadedVehicleCarSleepConfig(
                vehicleController,
                environmentField,
                configField);
            foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (carConfig != null)
                    break;
                if (candidate == null || candidate.GetType().FullName != "PlayerActivity.SleepEnvironmentConfig")
                    continue;
                if (!IsCarSleepConfig(candidate))
                    continue;

                carConfig = candidate;
            }

            if (carConfig == null)
            {
                carConfig = CreateFallbackCarSleepConfig(configField.FieldType);
                if (carConfig == null)
                {
                    context?.Logger.Warn("AudiRS6R: current Car sleep configuration was unavailable and fallback creation failed.");
                    return 0;
                }
            }

            configField.SetValue(environment, carConfig);
            environmentField.SetValue(vehicleController, environment);
            return 1;
        }
        catch (Exception ex)
        {
            context?.Logger.Warn($"AudiRS6R: could not assign the current Car sleep configuration: {ex.Message}");
            return 0;
        }
    }

    private static UnityEngine.Object? FindLoadedVehicleCarSleepConfig(
        VehicleController target,
        FieldInfo environmentField,
        FieldInfo configField)
    {
        foreach (var otherVehicle in Resources.FindObjectsOfTypeAll<VehicleController>())
        {
            if (otherVehicle == null || otherVehicle == target)
                continue;

            var otherEnvironment = environmentField.GetValue(otherVehicle);
            if (otherEnvironment == null)
                continue;

            var candidate = configField.GetValue(otherEnvironment) as UnityEngine.Object;
            if (candidate != null && IsCarSleepConfig(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsCarSleepConfig(UnityEngine.Object candidate)
    {
        var typeField = FindField(candidate.GetType(), "sleepEnvironmentType");
        var typeValue = typeField?.GetValue(candidate);
        return typeValue != null && Convert.ToInt32(typeValue) == 1;
    }

    private static UnityEngine.Object? CreateFallbackCarSleepConfig(Type configType)
    {
        if (!typeof(ScriptableObject).IsAssignableFrom(configType))
            return null;

        var config = ScriptableObject.CreateInstance(configType);
        config.name = "AudiRS6R Runtime Car Sleep Config";
        config.hideFlags = HideFlags.HideAndDontSave;

        SetEnumField(config, "sleepEnvironmentType", 1);
        SetEnumField(config, "energyRegen", 3);

        var balanceConfigField = FindField(configType, "balanceConfig");
        if (balanceConfigField == null || !typeof(ScriptableObject).IsAssignableFrom(balanceConfigField.FieldType))
        {
            Destroy(config);
            return null;
        }

        var balance = ScriptableObject.CreateInstance(balanceConfigField.FieldType);
        balance.name = "AudiRS6R Runtime Car Sleep Balance";
        balance.hideFlags = HideFlags.HideAndDontSave;
        SetStringField(balance, "displayName", "Car");
        SetEnumField(balance, "source", 0);
        SetIntField(balance, "defaultDurationMinutes", 480);
        SetIntField(balance, "minDurationMinutes", 60);
        SetIntField(balance, "maxDurationMinutes", 1440);
        balanceConfigField.SetValue(config, balance);

        var luxuryBalanceField = FindField(configType, "luxuryOverrideBalanceConfig");
        luxuryBalanceField?.SetValue(config, balance);
        return config;
    }

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }

    private static void SetEnumField(object target, string fieldName, int value)
    {
        var field = FindField(target.GetType(), fieldName);
        if (field?.FieldType.IsEnum == true)
            field.SetValue(target, Enum.ToObject(field.FieldType, value));
    }

    private static void SetIntField(object target, string fieldName, int value)
    {
        var field = FindField(target.GetType(), fieldName);
        if (field?.FieldType == typeof(int))
            field.SetValue(target, value);
    }

    private static void SetStringField(object target, string fieldName, string value)
    {
        var field = FindField(target.GetType(), fieldName);
        if (field?.FieldType == typeof(string))
            field.SetValue(target, value);
    }

    private void ConfigureSuspension(
        VehicleController vehicleController,
        out int suspensionCount,
        out int adjustedCount)
    {
        suspensionCount = 0;
        adjustedCount = 0;

        foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || !IsWheelController(child.name))
                continue;

            foreach (var component in child.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;

                try
                {
                    var springField = component.GetType().GetField(
                        "spring",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var spring = springField?.GetValue(component);
                    if (springField == null || spring == null)
                        continue;

                    var maxLengthField = spring.GetType().GetField(
                        "maxLength",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (maxLengthField == null || maxLengthField.FieldType != typeof(float))
                        continue;

                    suspensionCount++;
                    var currentMaxLength = (float)(maxLengthField.GetValue(spring) ?? 0f);
                    if (Mathf.Approximately(currentMaxLength, SuspensionMaxLength))
                        continue;

                    maxLengthField.SetValue(spring, SuspensionMaxLength);
                    springField.SetValue(component, spring);
                    adjustedCount++;
                }
                catch (Exception ex)
                {
                    context?.Logger.Warn(
                        $"AudiRS6R: could not configure suspension '{child.name}': {ex.Message}");
                }
            }
        }
    }

    private void ConfigureBrakes(
        VehicleController vehicleController,
        out int brakeModuleCount,
        out int axleGroupCount)
    {
        brakeModuleCount = 0;
        axleGroupCount = 0;

        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            try
            {
                var componentType = component.GetType();
                var brakesField = componentType.GetField(
                    "brakes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var brakes = brakesField?.GetValue(component);
                if (brakesField != null && brakes != null)
                {
                    var configured = TrySetFloatField(brakes, "maxTorque", BrakeMaxTorque);
                    configured |= TrySetFloatField(brakes, "actuationTime", BrakeActuationTime);
                    if (configured)
                    {
                        brakesField.SetValue(component, brakes);
                        brakeModuleCount++;
                    }
                }

                var wheelGroupsField = componentType.GetField(
                    "wheelGroups",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (!(wheelGroupsField?.GetValue(component) is IList wheelGroups))
                    continue;

                for (var index = 0; index < wheelGroups.Count; index++)
                {
                    var wheelGroup = wheelGroups[index];
                    if (wheelGroup == null)
                        continue;

                    var groupName = GetStringField(wheelGroup, "name");
                    var isRearAxle = groupName.IndexOf("Rear", StringComparison.OrdinalIgnoreCase) >= 0 || index == 1;
                    var configured = TrySetFloatField(wheelGroup, "antiRollBarForce", AntiRollBarForce);
                    if (isRearAxle)
                    {
                        configured |= TrySetFloatField(wheelGroup, "brakeCoefficient", RearBrakeCoefficient);
                        configured |= TrySetFloatField(wheelGroup, "handbrakeCoefficient", HandbrakeCoefficient);
                    }

                    if (!configured)
                        continue;

                    if (wheelGroup.GetType().IsValueType)
                        wheelGroups[index] = wheelGroup;
                    axleGroupCount++;
                }

                wheelGroupsField.SetValue(component, wheelGroups);
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not configure brakes: {ex.Message}");
            }
        }
    }

    private void ConfigureLights(
        VehicleController vehicleController,
        out int lightManagerCount,
        out int validLightSourceCount,
        out int invalidLightSourceCount)
    {
        lightManagerCount = 0;
        validLightSourceCount = 0;
        invalidLightSourceCount = 0;

        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            try
            {
                var effectsManagerField = component.GetType().GetField(
                    "effectsManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var effectsManager = effectsManagerField?.GetValue(component);
                if (effectsManagerField == null || effectsManager == null)
                    continue;

                var lightsManagerField = effectsManager.GetType().GetField(
                    "lightsManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var lightsManager = lightsManagerField?.GetValue(effectsManager);
                if (lightsManagerField == null || lightsManager == null)
                    continue;

                lightManagerCount++;
                foreach (var groupFieldName in VehicleLightGroupFieldNames)
                {
                    var groupField = lightsManager.GetType().GetField(
                        groupFieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var group = groupField?.GetValue(lightsManager);
                    if (groupField == null || group == null)
                        continue;

                    var sourcesField = group.GetType().GetField(
                        "lightSources",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (!(sourcesField?.GetValue(group) is IList sources))
                        continue;

                    for (var index = sources.Count - 1; index >= 0; index--)
                    {
                        var source = sources[index];
                        if (source == null)
                        {
                            sources.RemoveAt(index);
                            invalidLightSourceCount++;
                            continue;
                        }

                        var light = GetObjectField(source, "light");
                        var meshRenderer = GetObjectField(source, "meshRenderer");
                        var lightSourceType = GetIntField(source, "type", -1);
                        var isValid = lightSourceType == 0 ? light != null : meshRenderer != null;
                        if (!isValid)
                        {
                            sources.RemoveAt(index);
                            invalidLightSourceCount++;
                        }
                        else
                        {
                            validLightSourceCount++;
                        }
                    }

                    sourcesField.SetValue(group, sources);
                    if (group.GetType().IsValueType)
                        groupField.SetValue(lightsManager, group);
                }

                if (lightsManager.GetType().IsValueType)
                    lightsManagerField.SetValue(effectsManager, lightsManager);
                if (effectsManager.GetType().IsValueType)
                    effectsManagerField.SetValue(component, effectsManager);
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not sanitize vehicle lights: {ex.Message}");
            }
        }
    }

    private void ConfigureFuelConsumption(VehicleController vehicleController)
    {
        var fuelModuleFound = false;
        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.Modules.Fuel.FuelModuleWrapper",
                    StringComparison.Ordinal))
            {
                continue;
            }

            fuelModuleFound = true;
            try
            {
                var moduleField = component.GetType().GetField(
                    "module",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var module = moduleField?.GetValue(component);
                if (moduleField == null || module == null)
                    continue;

                var multiplierConfigured =
                    TrySetFloatField(module, "consumptionMultiplier", FuelConsumptionMultiplier);
                var idleConfigured = TrySetFloatField(module, "idleConsumption", FuelIdleConsumption);
                if (multiplierConfigured || idleConfigured)
                    moduleField.SetValue(component, module);
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not configure fuel consumption: {ex.Message}");
            }
        }

        if (!fuelModuleFound)
            context?.Logger.Warn("AudiRS6R: fuel module was not found on the configured vehicle.");
    }

    private static bool TrySetFloatField(object target, string fieldName, float value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(float))
            return false;

        field.SetValue(target, value);
        return true;
    }

    private static object? GetMemberValue(object? target, string memberName)
    {
        if (target == null)
            return null;

        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);
        }

        return null;
    }

    private static bool TrySetFloatMember(object target, string memberName, float value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
                return true;
            }

            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null && property.CanWrite && property.PropertyType == typeof(float))
            {
                property.SetValue(target, value, null);
                return true;
            }
        }

        return false;
    }

    private static object? InvokeNoArgumentMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        return method?.Invoke(target, null);
    }

    private static bool TrySetVector3Field(object target, string fieldName, Vector3 value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(Vector3))
            return false;

        field.SetValue(target, value);
        return true;
    }

    private static string GetStringField(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(target) as string ?? string.Empty;
    }

    private static UnityEngine.Object? GetObjectField(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(target) as UnityEngine.Object;
    }

    private static int GetIntField(object target, string fieldName, int fallback)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var value = field?.GetValue(target);
        return value != null ? Convert.ToInt32(value) : fallback;
    }

    private static bool IsWheelController(string objectName)
    {
        return string.Equals(objectName, "FrontLeft_WheelController", StringComparison.Ordinal) ||
               string.Equals(objectName, "FrontRight_WheelController", StringComparison.Ordinal) ||
               string.Equals(objectName, "RearLeft_WheelController", StringComparison.Ordinal) ||
               string.Equals(objectName, "RearRight_WheelController", StringComparison.Ordinal);
    }

    private void RemoveVehicleRuntimeComponents()
    {
        foreach (var driverController in FindObjectsOfType<AudiRS6RDriverController>(true))
            if (driverController != null) Destroy(driverController);

        var roadDamageGuards = FindObjectsOfType<AudiRS6RRoadDamageGuard>();
        foreach (var roadDamageGuard in roadDamageGuards)
        {
            if (roadDamageGuard != null)
                Destroy(roadDamageGuard);
        }

        var lightingControllers = FindObjectsOfType<AudiRS6RLightingController>();
        foreach (var lightingController in lightingControllers)
        {
            if (lightingController != null)
                Destroy(lightingController);
        }
    }

}
