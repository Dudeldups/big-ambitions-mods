#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using Vehicles.VehicleTypes;

public sealed class AudiRS6RRuntime : MonoBehaviour
{
    private const float DealerRetryInterval = 1f;
    private const float DiagnosticsScanInterval = 1f;
    private const bool DebugVehicleSpawnEnabled = true;
    private const float AntiRollBarForce = 6500f;
    private const float BrakeActuationTime = 0.06f;
    private const float BrakeMaxTorque = 18000f;
    private const float CenterOfMassHeight = 0.25f;
    private const float DamageIntensity = 0.5f;
    private const float HandbrakeCoefficient = 2f;
    private const float LowerBodyColliderCenterY = 0.6f;
    private const float LowerBodyColliderHeight = 0.62f;
    private const float RearBrakeCoefficient = 0.55f;
    private const float SpawnDistance = 6f;
    private const float SuspensionMaxLength = 0.25f;
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

    private bool dealerRegistrationPending;
    private float nextDiagnosticsScanAt;
    private float nextDealerSyncAt;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;

    public static AudiRS6RRuntime Initialize(ModContext context, string vehicleTypeName, bool dealerRegistered)
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
        runtime.dealerRegistrationPending = !dealerRegistered;
        runtime.nextDealerSyncAt = runtime.dealerRegistrationPending ? 0f : float.PositiveInfinity;
        runtime.nextDiagnosticsScanAt = 0f;
        AudiRS6RDiagnostics.Initialize(context, runtime.vehicleTypeName);
        runtime.EnsureVehicleDiagnosticsAttached();
        return runtime;
    }

    public void Shutdown()
    {
        RemoveVehicleDiagnostics();
        AudiRS6RDiagnostics.Shutdown();
        Destroy(gameObject);
    }

    private void Update()
    {
        if (dealerRegistrationPending && Time.unscaledTime >= nextDealerSyncAt)
        {
            if (TryRegisterWithDealer())
            {
                dealerRegistrationPending = false;
                nextDealerSyncAt = float.PositiveInfinity;
            }
            else
            {
                nextDealerSyncAt = Time.unscaledTime + DealerRetryInterval;
            }
        }

        if (DebugVehicleSpawnEnabled && Input.GetKeyDown(KeyCode.F9))
            TrySpawnVehicleInFrontOfPlayer();

        if (Time.unscaledTime >= nextDiagnosticsScanAt)
        {
            EnsureVehicleDiagnosticsAttached();
            nextDiagnosticsScanAt = Time.unscaledTime + DiagnosticsScanInterval;
        }
    }

    private void EnsureVehicleDiagnosticsAttached()
    {
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

            var diagnostics = vehicleController.GetComponent<AudiRS6RVehicleDiagnostics>();
            if (diagnostics == null)
            {
                ConfigureVehiclePhysics(vehicleController);
                diagnostics = vehicleController.gameObject.AddComponent<AudiRS6RVehicleDiagnostics>();
            }

            var lightingController = vehicleController.GetComponent<AudiRS6RLightingController>();
            if (lightingController == null)
                lightingController = vehicleController.gameObject.AddComponent<AudiRS6RLightingController>();

            lightingController.Initialize(vehicleController);
            diagnostics.Initialize(vehicleController);
        }
    }

    private void ConfigureVehiclePhysics(VehicleController vehicleController)
    {
        var rigidbody = vehicleController.GetComponent<Rigidbody>() ?? vehicleController.GetComponentInParent<Rigidbody>();
        var centerOfMassModules = ConfigureCenterOfMassModules(vehicleController);
        if (rigidbody != null)
            rigidbody.centerOfMass = new Vector3(0f, CenterOfMassHeight, 0f);

        var visualPartsConfigured = ConfigureVisualBodyHeight(vehicleController);
        ConfigureBodyColliders(vehicleController, out var bodyColliderCount, out var bodyCollidersAdjusted);
        ConfigureSuspension(vehicleController, out var suspensionCount, out var suspensionAdjustedCount);
        ConfigureBrakes(vehicleController, out var brakeModuleCount, out var axleGroupCount);
        var damageHandlersConfigured = ConfigureDamageHandlers(vehicleController);
        ConfigureLights(vehicleController, out var lightManagerCount, out var lightSourceCount, out var invalidLightSourceCount);

        AudiRS6RDiagnostics.Vehicle(
            "VEHICLE_PHYSICS_CONFIG",
            $"vehicleId=\"{vehicleController.vehicleInstance.id}\" centerOfMass=(0.000,{CenterOfMassHeight:0.000},0.000) " +
            $"centerOfMassModules={centerOfMassModules} visualBodyLocalY={VisualBodyLocalHeight:0.000} " +
            $"visualPartsConfigured={visualPartsConfigured} lowerBodyColliderCenterY={LowerBodyColliderCenterY:0.000} " +
            $"lowerBodyColliderHeight={LowerBodyColliderHeight:0.000} bodyCollidersFound={bodyColliderCount} " +
            $"bodyCollidersAdjusted={bodyCollidersAdjusted} suspensionMaxLength={SuspensionMaxLength:0.000} " +
            $"suspensionsFound={suspensionCount} suspensionsAdjusted={suspensionAdjustedCount} " +
            $"brakeMaxTorque={BrakeMaxTorque:0} brakeActuationTime={BrakeActuationTime:0.000} " +
            $"rearBrakeCoefficient={RearBrakeCoefficient:0.000} handbrakeCoefficient={HandbrakeCoefficient:0.000} " +
            $"antiRollBarForce={AntiRollBarForce:0} brakeModules={brakeModuleCount} axleGroups={axleGroupCount} " +
            $"damageIntensity={DamageIntensity:0.000} damageHandlersConfigured={damageHandlersConfigured} " +
            $"lightManagers={lightManagerCount} validLightSources={lightSourceCount} " +
            $"invalidLightSourcesRemoved={invalidLightSourceCount}");
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

    private void RemoveVehicleDiagnostics()
    {
        var diagnostics = FindObjectsOfType<AudiRS6RVehicleDiagnostics>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic != null)
                Destroy(diagnostic);
        }

        var lightingControllers = FindObjectsOfType<AudiRS6RLightingController>();
        foreach (var lightingController in lightingControllers)
        {
            if (lightingController != null)
                Destroy(lightingController);
        }
    }

    private bool TryRegisterWithDealer()
    {
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
            return false;

        try
        {
            var dealerType = Type.GetType("BackAlleyDealer.BackAlleyDealerInit, BackAlleyDealer");
            var dealerInstance = dealerType?.GetProperty("Instance")?.GetValue(null);
            var registerMethod = dealerType?.GetMethod("RegisterVehicle");
            if (dealerInstance == null || registerMethod == null)
                return false;

            registerMethod.Invoke(dealerInstance, new object[] { vehicleTypeName });
            return true;
        }
        catch (Exception ex)
        {
            context?.Logger.Warn($"AudiRS6R: dealer sync failed: {ex.Message}");
            return false;
        }
    }

    private void TrySpawnVehicleInFrontOfPlayer()
    {
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
        {
            Debug.LogWarning("AudiRS6R: no vehicle type configured for F9 spawn.");
            return;
        }

        var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
        if (vehicleType == null)
        {
            Debug.LogWarning($"AudiRS6R: vehicle type '{vehicleTypeName}' is not registered.");
            return;
        }

        var playerController = GameManager.Instance?.playerController;
        if (playerController == null)
        {
            Debug.LogWarning("AudiRS6R: player controller unavailable, cannot spawn vehicle.");
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

        VehicleHelper.CreateAndSpawnVehicle(vehicleInstance, spawnPosition, spawnRotation);
        TrySnapSpawnedVehicleToGround(vehicleInstance.id, spawnPosition, spawnRotation);
        EnsureVehicleDiagnosticsAttached();
        Debug.Log($"AudiRS6R: spawned '{vehicleTypeName}' with id '{vehicleInstance.id}'.");
    }

    private static void TrySnapSpawnedVehicleToGround(string vehicleId, Vector3 spawnPosition, Quaternion spawnRotation)
    {
        var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
        if (allPlayerVehicles == null)
            return;

        foreach (var vehicleController in allPlayerVehicles)
        {
            if (vehicleController?.vehicleInstance == null || vehicleController.vehicleInstance.id != vehicleId)
                continue;

            VehicleHelper.TeleportVehicleToGround(vehicleController, spawnPosition, spawnRotation);
            return;
        }
    }

    private static string CreateVehicleId()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }
}
