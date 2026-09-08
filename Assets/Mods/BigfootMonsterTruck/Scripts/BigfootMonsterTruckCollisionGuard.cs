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
    private const float TireContactDriveAcceleration = 8.5f;
    private const float TireContactMinimumStrength = 0.7f;
    private const float TireContactLiftMultiplier = 0.22f;
    private const float LowSpeedMountThreshold = 2.75f;
    private const float MountingPhaseDuration = 0.9f;
    private const float MountingDriveAcceleration = 4.5f;
    private const float MountingLiftAcceleration = 2.2f;
    private const float MountingMaximumVerticalSpeed = 0.55f;
    private const float TractionBridgeDuration = 1.25f;
    private const float TireContactBreakDuration = 0.2f;
    private const float MaximumClimbAssistSpeed = 12f;
    private const float MaximumAssistedVerticalSpeed = 1.25f;
    private const int HeavyCargoCapacity = 32;

    private readonly List<VehicleDeformationController.VehicleDeformation> approvedDeformations = new();
    private readonly Dictionary<int, float> nextCollisionLogTimes = new();
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
    private float lastTireContactTime;
    private float mountingPhaseUntil;
    private float tractionBridgeUntil;
    private int assistedVehicleId;
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

            var now = Time.unscaledTime;
            var otherInstanceId = otherPlayerVehicle != null
                ? otherPlayerVehicle.GetInstanceID()
                : trafficVehicle!.GetInstanceID();
            var tireContact = IsPhysicalTireContact(collision);
            if (tireContact)
            {
                var isNewMount = assistedVehicleId != otherInstanceId ||
                                 now > lastTireContactTime + TireContactBreakDuration;
                if (isNewMount && forwardSpeed < LowSpeedMountThreshold)
                    mountingPhaseUntil = now + MountingPhaseDuration;
                assistedVehicleId = otherInstanceId;
                lastTireContactTime = now;
                tractionBridgeUntil = now + TractionBridgeDuration;
            }
            var bridgedTireContact = !tireContact && assistedVehicleId == otherInstanceId &&
                                     now <= tractionBridgeUntil;
            var tractionActive = tireContact || bridgedTireContact;
            var mounting = tractionActive && now <= mountingPhaseUntil &&
                           forwardSpeed < LowSpeedMountThreshold;
            var otherLocal = vehicle.transform.InverseTransformPoint(collision.collider.bounds.center);
            // Once a tire is on the vehicle, keep pulling even after its center passes
            // behind the front axle. The bridge covers brief contact loss as it enters
            // the gap between the axles, where stopping would strand it beneath the truck.
            if (!tractionActive && otherLocal.z * driveDirection < 0.75f)
                return;
            if (forwardSpeed < 0f)
            {
                velocity -= worldDriveDirection * forwardSpeed;
                vehicleBody.velocity = velocity;
            }

            var verticalSpeed = Vector3.Dot(vehicleBody.velocity, Vector3.up);
            var maximumVerticalSpeed = mounting
                ? MountingMaximumVerticalSpeed
                : MaximumAssistedVerticalSpeed;
            if (verticalSpeed > maximumVerticalSpeed)
            {
                vehicleBody.velocity -=
                    Vector3.up * (verticalSpeed - maximumVerticalSpeed);
                verticalSpeed = maximumVerticalSpeed;
            }

            var speedFactor = 1f - Mathf.Clamp01(Mathf.Max(0f, forwardSpeed) /
                                                 MaximumClimbAssistSpeed);
            var driveStrength = tractionActive
                ? Mathf.Lerp(TireContactMinimumStrength, 1f, speedFactor)
                : speedFactor;
            var liftFactor = Mathf.Lerp(0.45f, 1f, speedFactor);
            if (tractionActive)
                liftFactor *= TireContactLiftMultiplier;
            var liftAcceleration = mounting
                ? MountingLiftAcceleration
                : ClimbLiftAcceleration * liftFactor;
            if (verticalSpeed < maximumVerticalSpeed)
                vehicleBody.AddForce(
                    Vector3.up * liftAcceleration,
                    ForceMode.Acceleration);
            var driveAcceleration = mounting
                ? MountingDriveAcceleration
                : tractionActive
                    ? TireContactDriveAcceleration
                    : ClimbDriveAcceleration;
            vehicleBody.AddForce(
                worldDriveDirection *
                (driveAcceleration * driveStrength * Mathf.Clamp01(throttle)),
                ForceMode.Acceleration);

            var localAngularVelocity = vehicle.transform.InverseTransformDirection(
                vehicleBody.angularVelocity);
            localAngularVelocity.x *= mounting ? 0.72f : 0.9f;
            localAngularVelocity.z *= mounting ? 0.65f : 0.82f;
            vehicleBody.angularVelocity = vehicle.transform.TransformDirection(localAngularVelocity);

            if (Time.unscaledTime >= nextClimbAssistLogTime)
            {
                nextClimbAssistLogTime = Time.unscaledTime + ClimbAssistLogCooldown;
                var otherName = otherPlayerVehicle != null
                    ? GetVehicleName(otherPlayerVehicle)
                    : trafficVehicle!.name;
                var assistMode = mounting
                    ? "low-speed-mount"
                    : tireContact
                        ? "tire-traction"
                        : bridgedTireContact
                            ? "traction-bridge"
                            : "approach-lift";
                context?.Logger.Info(
                    $"BigfootMonsterTruck: climb assist active other='{otherName}' " +
                    $"mode={assistMode}, " +
                    $"direction={(driveDirection > 0f ? "forward" : "reverse")}, " +
                    $"throttle={throttle:F2}, speed={forwardSpeed:F2}m/s, " +
                    $"vertical={verticalSpeed:F2}m/s, strength={driveStrength:F2}.");
            }
        }
        catch (Exception exception)
        {
            ReportFailureOnce(nameof(OnCollisionStay), exception);
        }
    }

    private void OnDestroy()
    {
        if (physicsVehicle != null && collisionListener != null)
            physicsVehicle.onCollision.RemoveListener(collisionListener);
        physicsVehicle = null;
        vehicleBody = null;
        collisionListener = null;
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
