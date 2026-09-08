#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Tags;
using UnityEngine;
using UnityEngine.Events;

internal sealed class BigfootMonsterTruckCollisionGuard : MonoBehaviour
{
    private const float DamageTolerance = 0.0001f;
    private const float SmallVehicleSuppressionWindow = 0.75f;
    private const float CollisionLogCooldown = 5f;
    private const float ClimbAssistLogCooldown = 3f;
    private const float ClimbLiftAcceleration = 3.25f;
    private const float ClimbDriveAcceleration = 4f;
    private const float MaximumClimbAssistSpeed = 12f;
    private const float MaximumAssistedVerticalSpeed = 1.25f;
    private const float VehicleSurfaceGripHoldTime = 0.15f;
    private const float VehicleSurfaceOtherBodyForceScale = 0.2f;
    private const int TrafficVehicleLayer = 12;
    private const int HeavyCargoCapacity = 32;

    private readonly List<VehicleDeformationController.VehicleDeformation> approvedDeformations = new();
    private readonly Dictionary<int, float> nextCollisionLogTimes = new();
    private readonly List<WheelSurfaceGripState> wheelSurfaceGripStates = new();
    private VehicleController? vehicle;
    private VehicleDeformationController? deformationController;
    private FieldInfo? deformationQueueField;
    private ModContext? context;
    private NWH.VehiclePhysics2.VehicleController? physicsVehicle;
    private Rigidbody? vehicleBody;
    private UnityAction<Collision>? collisionListener;
    private float approvedDamage;
    private float suppressDamageUntil;
    private int heavyImpactFrame = -1;
    private float nextClimbAssistLogTime;
    private float nextSurfaceGripLogTime;
    private float wheelSurfaceGripUntil;
    private bool wheelSurfaceGripActive;
    private bool initialized;
    private bool failureReported;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        deformationController = controller.GetComponentInChildren<VehicleDeformationController>(true);
        deformationQueueField = typeof(VehicleDeformationController).GetField(
            "_deformationQueue",
            BindingFlags.Instance | BindingFlags.NonPublic);
        approvedDamage = controller.vehicleInstance?.damage ?? 0f;
        vehicleBody = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        SnapshotApprovedDeformations();
        if (controller is CarController car && car.vehicleController != null)
        {
            physicsVehicle = car.vehicleController;
            CaptureWheelSurfaceGripStates(controller);
            collisionListener = HandleVehicleCollision;
            physicsVehicle.onCollision.AddListener(collisionListener);
            modContext?.Logger.Info(
                $"BigfootMonsterTruck collision guard subscribed vehicle={controller.GetInstanceID()}.");
        }
        else
        {
            modContext?.Logger.Warn(
                "BigfootMonsterTruck collision guard could not subscribe to the vehicle collision event.");
        }
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || vehicle?.vehicleInstance == null)
            return;

        try
        {
            UpdateWheelSurfaceGrip();
            if (Time.unscaledTime <= suppressDamageUntil)
            {
                ClearPendingDeformationQueue();
                RestoreApprovedDeformations();
                if (vehicle.vehicleInstance.damage > approvedDamage + DamageTolerance)
                {
                    if (vehicle is CarController car)
                        car.SetDamage(approvedDamage);
                    else
                        vehicle.vehicleInstance.damage = approvedDamage;
                }
                return;
            }

            approvedDamage = vehicle.vehicleInstance.damage;
            SnapshotApprovedDeformations();
        }
        catch (Exception exception)
        {
            ReportFailureOnce(nameof(Update), exception);
        }
    }

    private void HandleVehicleCollision(Collision collision)
    {
        if (!initialized || vehicle == null || collision?.collider == null)
            return;

        try
        {
            var otherPlayerVehicle = collision.collider.GetComponentInParent<VehicleController>();
            if (otherPlayerVehicle == vehicle)
                return;
            var trafficVehicle = collision.collider.GetComponentInParent<GleyTrafficSystem.VehicleComponent>();
            if (otherPlayerVehicle == null && trafficVehicle == null)
                return;

            var otherName = otherPlayerVehicle != null
                ? GetVehicleName(otherPlayerVehicle)
                : trafficVehicle!.name;
            var isHeavy = otherPlayerVehicle != null
                ? IsHeavyVehicle(otherPlayerVehicle)
                : IsHeavyTrafficVehicle(trafficVehicle!);
            var otherInstanceId = otherPlayerVehicle != null
                ? otherPlayerVehicle.GetInstanceID()
                : trafficVehicle!.GetInstanceID();
            if (isHeavy)
            {
                heavyImpactFrame = Time.frameCount;
                suppressDamageUntil = 0f;
                if (ShouldLogCollision(otherInstanceId))
                {
                    context?.Logger.Info(
                        $"BigfootMonsterTruck: heavy vehicle impact kept damage-enabled " +
                        $"other='{otherName}'.");
                }
                return;
            }

            if (heavyImpactFrame == Time.frameCount)
                return;
            suppressDamageUntil = Time.unscaledTime + SmallVehicleSuppressionWindow;
            ClearPendingDeformationQueue();
            if (ShouldLogCollision(otherInstanceId))
            {
                context?.Logger.Info(
                    $"BigfootMonsterTruck: suppressing small-vehicle impact damage " +
                    $"other='{otherName}' for {SmallVehicleSuppressionWindow:F2}s.");
            }
        }
        catch (Exception exception)
        {
            ReportFailureOnce(nameof(HandleVehicleCollision), exception);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!initialized || vehicle?.controlledByPlayer != true || physicsVehicle == null ||
            vehicleBody == null || collision?.collider == null)
            return;

        try
        {
            var otherPlayerVehicle = collision.collider.GetComponentInParent<VehicleController>();
            if (otherPlayerVehicle == vehicle)
                return;
            var trafficVehicle = collision.collider.GetComponentInParent<GleyTrafficSystem.VehicleComponent>();
            if (otherPlayerVehicle == null && trafficVehicle == null)
                return;
            if (otherPlayerVehicle != null
                    ? IsHeavyVehicle(otherPlayerVehicle)
                    : IsHeavyTrafficVehicle(trafficVehicle!))
                return;

            var throttle = physicsVehicle.input.Throttle;
            if (throttle < 0.2f || Vector3.Dot(vehicle.transform.up, Vector3.up) < 0.55f)
                return;

            var transmission = physicsVehicle.powertrain.transmission;
            var driveDirection = transmission.Gear < 0 ? -1f : 1f;
            var worldDriveDirection = vehicle.transform.forward * driveDirection;
            var velocity = vehicleBody.velocity;
            var forwardSpeed = Vector3.Dot(velocity, worldDriveDirection);
            if (forwardSpeed > MaximumClimbAssistSpeed)
                return;

            if (IsPhysicalTireContact(collision))
                EnableWheelSurfaceGrip();

            var otherLocal = vehicle.transform.InverseTransformPoint(collision.collider.bounds.center);
            if (otherLocal.z * driveDirection < 0.75f)
                return;
            if (forwardSpeed < 0f)
            {
                velocity -= worldDriveDirection * forwardSpeed;
                vehicleBody.velocity = velocity;
            }

            var verticalSpeed = Vector3.Dot(vehicleBody.velocity, Vector3.up);
            if (verticalSpeed > MaximumAssistedVerticalSpeed)
            {
                vehicleBody.velocity -=
                    Vector3.up * (verticalSpeed - MaximumAssistedVerticalSpeed);
                verticalSpeed = MaximumAssistedVerticalSpeed;
            }

            var speedFactor = 1f - Mathf.Clamp01(Mathf.Max(0f, forwardSpeed) /
                                                 MaximumClimbAssistSpeed);
            var liftFactor = Mathf.Lerp(0.45f, 1f, speedFactor);
            if (verticalSpeed < MaximumAssistedVerticalSpeed)
                vehicleBody.AddForce(
                    Vector3.up * (ClimbLiftAcceleration * liftFactor),
                    ForceMode.Acceleration);
            vehicleBody.AddForce(
                worldDriveDirection *
                (ClimbDriveAcceleration * speedFactor * Mathf.Clamp01(throttle)),
                ForceMode.Acceleration);

            var localAngularVelocity = vehicle.transform.InverseTransformDirection(
                vehicleBody.angularVelocity);
            localAngularVelocity.x *= 0.9f;
            localAngularVelocity.z *= 0.82f;
            vehicleBody.angularVelocity = vehicle.transform.TransformDirection(localAngularVelocity);

            if (Time.unscaledTime >= nextClimbAssistLogTime)
            {
                nextClimbAssistLogTime = Time.unscaledTime + ClimbAssistLogCooldown;
                var otherName = otherPlayerVehicle != null
                    ? GetVehicleName(otherPlayerVehicle)
                    : trafficVehicle!.name;
                context?.Logger.Info(
                    $"BigfootMonsterTruck: climb assist active other='{otherName}' " +
                    $"direction={(driveDirection > 0f ? "forward" : "reverse")}, " +
                    $"throttle={throttle:F2}, speed={forwardSpeed:F2}m/s, " +
                    $"vertical={verticalSpeed:F2}m/s, strength={speedFactor:F2}.");
            }
        }
        catch (Exception exception)
        {
            ReportFailureOnce(nameof(OnCollisionStay), exception);
        }
    }

    private void OnDestroy()
    {
        RestoreWheelSurfaceGrip();
        if (physicsVehicle != null && collisionListener != null)
            physicsVehicle.onCollision.RemoveListener(collisionListener);
        physicsVehicle = null;
        vehicleBody = null;
        collisionListener = null;
    }

    private void CaptureWheelSurfaceGripStates(VehicleController controller)
    {
        wheelSurfaceGripStates.Clear();
        foreach (var transform in controller.GetComponentsInChildren<Transform>(true))
        {
            if (!transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                continue;
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                if (component == null || !TryGetLayerMask(component, out var layerMask) ||
                    !TryGetFloat(component, "otherBodyForceScale", out var otherBodyForceScale))
                    continue;
                wheelSurfaceGripStates.Add(new WheelSurfaceGripState(
                    component,
                    layerMask,
                    otherBodyForceScale));
                break;
            }
        }

        if (wheelSurfaceGripStates.Count == 4)
            context?.Logger.Info(
                "BigfootMonsterTruck: four powered wheels prepared for temporary vehicle-surface grip.");
        else
            context?.Logger.Warn(
                $"BigfootMonsterTruck: expected four vehicle-surface grip wheels but found " +
                $"{wheelSurfaceGripStates.Count}.");
    }

    private void EnableWheelSurfaceGrip()
    {
        wheelSurfaceGripUntil = Time.unscaledTime + VehicleSurfaceGripHoldTime;
        if (wheelSurfaceGripActive || wheelSurfaceGripStates.Count == 0)
            return;

        foreach (var state in wheelSurfaceGripStates)
        {
            var layerMask = state.OriginalLayerMask;
            layerMask.value |= 1 << TrafficVehicleLayer;
            SetMember(state.Controller, "layerMask", layerMask);
            SetMember(
                state.Controller,
                "otherBodyForceScale",
                VehicleSurfaceOtherBodyForceScale);
        }
        wheelSurfaceGripActive = true;
        if (Time.unscaledTime >= nextSurfaceGripLogTime)
        {
            nextSurfaceGripLogTime = Time.unscaledTime + ClimbAssistLogCooldown;
            context?.Logger.Info(
                $"BigfootMonsterTruck: powered tire grip engaged on a small vehicle; " +
                $"wheels={wheelSurfaceGripStates.Count}.");
        }
    }

    private void UpdateWheelSurfaceGrip()
    {
        if (wheelSurfaceGripActive && Time.unscaledTime > wheelSurfaceGripUntil)
            RestoreWheelSurfaceGrip();
    }

    private void RestoreWheelSurfaceGrip()
    {
        if (!wheelSurfaceGripActive)
            return;
        foreach (var state in wheelSurfaceGripStates)
        {
            SetMember(state.Controller, "layerMask", state.OriginalLayerMask);
            SetMember(state.Controller, "otherBodyForceScale", state.OriginalOtherBodyForceScale);
        }
        wheelSurfaceGripActive = false;
    }

    private static bool IsPhysicalTireContact(Collision collision)
    {
        for (var contactIndex = 0; contactIndex < collision.contactCount; contactIndex++)
        {
            for (var transform = collision.GetContact(contactIndex).thisCollider?.transform;
                 transform != null;
                 transform = transform.parent)
                if (string.Equals(
                        transform.name,
                        "BigfootWheelContactColliders",
                        StringComparison.Ordinal))
                    return true;
        }
        return false;
    }

    private static bool TryGetLayerMask(object target, out LayerMask layerMask)
    {
        var value = GetMember(target, "layerMask");
        if (value is LayerMask mask)
        {
            layerMask = mask;
            return true;
        }
        layerMask = default;
        return false;
    }

    private static bool TryGetFloat(object target, string name, out float value)
    {
        var member = GetMember(target, name);
        if (member is float number)
        {
            value = number;
            return true;
        }
        value = 0f;
        return false;
    }

    private static object? GetMember(object target, string name)
    {
        var type = target.GetType();
        return FindField(type, name)?.GetValue(target) ??
               type.GetProperty(
                       name,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   ?.GetValue(target);
    }

    private static void SetMember(object target, string name, object value)
    {
        var type = target.GetType();
        var field = FindField(type, name);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }
        var property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field;
        }
        return null;
    }

    private sealed class WheelSurfaceGripState
    {
        public WheelSurfaceGripState(
            MonoBehaviour controller,
            LayerMask originalLayerMask,
            float originalOtherBodyForceScale)
        {
            Controller = controller;
            OriginalLayerMask = originalLayerMask;
            OriginalOtherBodyForceScale = originalOtherBodyForceScale;
        }

        public MonoBehaviour Controller { get; }
        public LayerMask OriginalLayerMask { get; }
        public float OriginalOtherBodyForceScale { get; }
    }

    private static bool IsHeavyVehicle(VehicleController other)
    {
        var type = other.vehicleType;
        if (type == null)
            return true;
        return type.HasTag(TagRef.Vehicletag.istruck) ||
               type.maxCargoCapacity >= HeavyCargoCapacity;
    }

    private static string GetVehicleName(VehicleController other) =>
        other.vehicleType?.vehicleTypeName ?? other.name;

    private static bool IsHeavyTrafficVehicle(GleyTrafficSystem.VehicleComponent trafficVehicle)
    {
        var identity = trafficVehicle.name.ToLowerInvariant();
        // Vanilla traffic names are stable while world-axis collider bounds change
        // as a car turns. Bounds-based classification misidentified ordinary cars.
        return identity.Contains("freighttruck") || identity.Contains("deliverytruck") ||
               identity.Contains("ambulance") || identity.Contains("vordv150");
    }

    private bool ShouldLogCollision(int otherInstanceId)
    {
        var now = Time.unscaledTime;
        if (nextCollisionLogTimes.TryGetValue(otherInstanceId, out var nextLogTime) &&
            now < nextLogTime)
            return false;
        nextCollisionLogTimes[otherInstanceId] = now + CollisionLogCooldown;
        return true;
    }

    private void ClearPendingDeformationQueue()
    {
        if (deformationController == null || deformationQueueField == null)
            return;
        var queue = deformationQueueField.GetValue(deformationController);
        queue?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(queue, null);
    }

    private void SnapshotApprovedDeformations()
    {
        approvedDeformations.Clear();
        var deformations = vehicle?.vehicleInstance?.deformations;
        if (deformations != null)
            approvedDeformations.AddRange(deformations);
    }

    private void RestoreApprovedDeformations()
    {
        var deformations = vehicle?.vehicleInstance?.deformations;
        if (deformations == null || MatchesApprovedDeformations(deformations))
            return;
        deformations.Clear();
        deformations.AddRange(approvedDeformations);
    }

    private bool MatchesApprovedDeformations(
        List<VehicleDeformationController.VehicleDeformation> deformations)
    {
        if (deformations.Count != approvedDeformations.Count)
            return false;
        for (var index = 0; index < deformations.Count; index++)
            if (!ReferenceEquals(deformations[index], approvedDeformations[index]))
                return false;
        return true;
    }

    private void ReportFailureOnce(string scope, Exception exception)
    {
        if (failureReported)
            return;
        failureReported = true;
        context?.Logger.Warn(
            $"BigfootMonsterTruck collision guard {scope} failed: " +
            $"{exception.GetType().Name}: {exception.Message}");
    }
}
