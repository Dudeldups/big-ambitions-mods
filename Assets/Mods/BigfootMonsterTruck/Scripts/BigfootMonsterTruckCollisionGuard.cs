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
    private const float ClimbLiftAcceleration = 3.25f;
    private const float ClimbDriveAcceleration = 4f;
    private const float TireContactDriveAcceleration = 8.5f;
    private const float TireContactMinimumStrength = 0.7f;
    private const float TireContactLiftMultiplier = 0.22f;
    private const float LatchedClimbTriggerSpeed = 2.75f;
    private const float LatchedClimbDuration = 1.5f;
    private const float LatchedClimbMaximumSpeed = 5f;
    private const float LatchedClimbVerticalSpeed = 0.8f;
    private const float LatchedClimbDriveAcceleration = 5f;
    private const float LatchedClimbLateralVelocityRetention = 0.15f;
    private const float LatchedClimbYawRetention = 0.5f;
    private const float LatchedClimbRollRetention = 0.65f;
    private const float LatchedClimbSteeringRate = 15f;
    private const float LatchedClimbSteeringYawAcceleration = 1.6f;
    private const float ParkedContactDirectionHoldTime = 0.75f;
    private const float ParkedContactLateralVelocityRetention = 0.05f;
    private const float ParkedContactYawRetention = 0.3f;
    private const float ParkedContactRollRetention = 0.55f;
    private const float ParkedContactSteeringLateralRetention = 0.4f;
    private const float ParkedContactSteeringYawRetention = 0.78f;
    private const float ParkedContactMaximumDescentSpeed = 0.75f;
    private const float MaximumClimbAssistSpeed = 12f;
    private const float MaximumAssistedVerticalSpeed = 1.25f;
    private const int HeavyCargoCapacity = 32;

    private readonly List<VehicleDeformationController.VehicleDeformation> approvedDeformations = new();
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
    private float latchedClimbUntil;
    private Vector3 latchedClimbDirection;
    private int latchedClimbVehicleId;
    private float parkedContactDirectionUntil;
    private Vector3 parkedContactDirection;
    private int parkedContactVehicleId;
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

    private void FixedUpdate()
    {
        if (!initialized || vehicle?.controlledByPlayer != true || physicsVehicle == null ||
            vehicleBody == null || Time.unscaledTime > latchedClimbUntil)
            return;

        try
        {
            var driveInput = physicsVehicle.input.Vertical;
            var driveIntensity = Mathf.Abs(driveInput);
            if (driveIntensity < 0.2f || Vector3.Dot(vehicle.transform.up, Vector3.up) < 0.55f)
            {
                latchedClimbUntil = 0f;
                return;
            }

            var driveDirection = Mathf.Sign(driveInput);
            var currentDriveDirection = vehicle.transform.forward * driveDirection;
            var steeringInput = physicsVehicle.input.Steering;
            var steeringAmount = Mathf.Clamp01(Mathf.Abs(steeringInput));
            if (steeringAmount > 0.05f)
            {
                var steeringAngle = steeringInput * driveDirection *
                                    LatchedClimbSteeringRate * Time.fixedDeltaTime;
                latchedClimbDirection = Quaternion.AngleAxis(
                    steeringAngle,
                    Vector3.up) * latchedClimbDirection;
            }
            var climbDirection = Vector3.ProjectOnPlane(latchedClimbDirection, Vector3.up).normalized;
            if (climbDirection.sqrMagnitude < 0.9f ||
                Vector3.Dot(currentDriveDirection, climbDirection) < 0.75f)
            {
                latchedClimbUntil = 0f;
                return;
            }

            var velocity = vehicleBody.velocity;
            var forwardSpeed = Vector3.Dot(velocity, climbDirection);
            if (forwardSpeed > LatchedClimbMaximumSpeed)
            {
                latchedClimbUntil = 0f;
                return;
            }

            var sideDirection = Vector3.Cross(Vector3.up, climbDirection).normalized;
            var lateralSpeed = Vector3.Dot(velocity, sideDirection);
            velocity -= sideDirection *
                        (lateralSpeed * (1f - LatchedClimbLateralVelocityRetention));
            var verticalSpeed = Vector3.Dot(velocity, Vector3.up);
            if (verticalSpeed < LatchedClimbVerticalSpeed)
                velocity += Vector3.up * (LatchedClimbVerticalSpeed - verticalSpeed);
            vehicleBody.velocity = velocity;
            vehicleBody.AddForce(
                climbDirection *
                (LatchedClimbDriveAcceleration * Mathf.Clamp01(driveIntensity)),
                ForceMode.Acceleration);
            var localAngularVelocity = vehicle.transform.InverseTransformDirection(
                vehicleBody.angularVelocity);
            localAngularVelocity.y *= Mathf.Lerp(
                LatchedClimbYawRetention,
                0.82f,
                steeringAmount);
            localAngularVelocity.z *= LatchedClimbRollRetention;
            vehicleBody.angularVelocity = vehicle.transform.TransformDirection(localAngularVelocity);
            if (steeringAmount > 0.05f)
                vehicleBody.AddTorque(
                    Vector3.up *
                    (steeringInput * driveDirection * LatchedClimbSteeringYawAcceleration),
                    ForceMode.Acceleration);
        }
        catch (Exception exception)
        {
            latchedClimbUntil = 0f;
            ReportFailureOnce(nameof(FixedUpdate), exception);
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
            var parkedVehicle = otherPlayerVehicle == null && trafficVehicle == null
                ? FindParkedVehicle(collision.collider)
                : null;
            if (otherPlayerVehicle == null && trafficVehicle == null && parkedVehicle == null)
                return;

            var isHeavy = otherPlayerVehicle != null
                ? IsHeavyVehicle(otherPlayerVehicle)
                : trafficVehicle != null
                    ? IsHeavyVehicleIdentity(trafficVehicle.name)
                    : IsHeavyVehicleIdentity(parkedVehicle!.name);
            if (isHeavy)
            {
                heavyImpactFrame = Time.frameCount;
                suppressDamageUntil = 0f;
                return;
            }

            if (heavyImpactFrame == Time.frameCount)
                return;
            suppressDamageUntil = Time.unscaledTime + SmallVehicleSuppressionWindow;
            ClearPendingDeformationQueue();
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
            var parkedVehicle = otherPlayerVehicle == null && trafficVehicle == null
                ? FindParkedVehicle(collision.collider)
                : null;
            if (otherPlayerVehicle == null && trafficVehicle == null && parkedVehicle == null)
                return;
            if (otherPlayerVehicle != null
                    ? IsTooHeavyToClimb(otherPlayerVehicle)
                    : trafficVehicle != null
                        ? IsTooHeavyToClimbIdentity(trafficVehicle.name)
                        : IsTooHeavyToClimbIdentity(parkedVehicle!.name))
                return;

            var driveInput = physicsVehicle.input.Vertical;
            var driveIntensity = Mathf.Abs(driveInput);
            if (driveIntensity < 0.2f || Vector3.Dot(vehicle.transform.up, Vector3.up) < 0.55f)
                return;

            var driveDirection = Mathf.Sign(driveInput);
            var worldDriveDirection = vehicle.transform.forward * driveDirection;
            var velocity = vehicleBody.velocity;
            var forwardSpeed = Vector3.Dot(velocity, worldDriveDirection);
            if (forwardSpeed > MaximumClimbAssistSpeed)
                return;

            if (parkedVehicle != null)
            {
                StabilizeParkedContact(
                    parkedVehicle,
                    worldDriveDirection,
                    physicsVehicle.input.Steering);
                velocity = vehicleBody.velocity;
                forwardSpeed = Vector3.Dot(velocity, worldDriveDirection);
            }

            var tireContact = IsPhysicalTireContact(collision);
            var leadingEdgeContact = IsLeadingEdgeContact(
                collision,
                vehicle.transform,
                driveDirection);
            var latchedClimb = (tireContact || leadingEdgeContact) &&
                               forwardSpeed < LatchedClimbTriggerSpeed;
            if (latchedClimb)
            {
                var otherInstanceId = otherPlayerVehicle != null
                    ? otherPlayerVehicle.GetInstanceID()
                    : trafficVehicle != null
                        ? trafficVehicle.GetInstanceID()
                        : parkedVehicle!.GetInstanceID();
                var newLatch = Time.unscaledTime > latchedClimbUntil ||
                               latchedClimbVehicleId != otherInstanceId;
                if (newLatch)
                {
                    latchedClimbDirection = Vector3.ProjectOnPlane(
                        worldDriveDirection,
                        Vector3.up).normalized;
                    latchedClimbVehicleId = otherInstanceId;
                }
                latchedClimbUntil = Time.unscaledTime + LatchedClimbDuration;
            }
            var otherPosition = otherPlayerVehicle != null
                ? otherPlayerVehicle.transform.position
                : trafficVehicle != null
                    ? trafficVehicle.transform.position
                    : parkedVehicle!.position;
            var otherLocal = vehicle.transform.InverseTransformPoint(otherPosition);
            // Once a tire is on the vehicle, keep pulling even after its center passes
            // behind the front axle. Stopping here was what stranded cars beneath the truck.
            if (!tireContact && otherLocal.z * driveDirection < 0.75f)
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
            var driveStrength = tireContact
                ? Mathf.Lerp(TireContactMinimumStrength, 1f, speedFactor)
                : speedFactor;
            var liftFactor = Mathf.Lerp(0.45f, 1f, speedFactor);
            if (tireContact)
                liftFactor *= TireContactLiftMultiplier;
            if (verticalSpeed < MaximumAssistedVerticalSpeed)
                vehicleBody.AddForce(
                    Vector3.up * (ClimbLiftAcceleration * liftFactor),
                    ForceMode.Acceleration);
            vehicleBody.AddForce(
                worldDriveDirection *
                ((tireContact ? TireContactDriveAcceleration : ClimbDriveAcceleration) *
                 driveStrength * Mathf.Clamp01(driveIntensity)),
                ForceMode.Acceleration);

            var localAngularVelocity = vehicle.transform.InverseTransformDirection(
                vehicleBody.angularVelocity);
            localAngularVelocity.x *= 0.9f;
            localAngularVelocity.z *= 0.82f;
            vehicleBody.angularVelocity = vehicle.transform.TransformDirection(localAngularVelocity);

        }
        catch (Exception exception)
        {
            ReportFailureOnce(nameof(OnCollisionStay), exception);
        }
    }

    private void OnDestroy()
    {
        latchedClimbUntil = 0f;
        latchedClimbVehicleId = 0;
        parkedContactDirectionUntil = 0f;
        parkedContactVehicleId = 0;
        if (physicsVehicle != null && collisionListener != null)
            physicsVehicle.onCollision.RemoveListener(collisionListener);
        physicsVehicle = null;
        vehicleBody = null;
        collisionListener = null;
    }

    private void StabilizeParkedContact(
        Transform parkedVehicle,
        Vector3 worldDriveDirection,
        float steeringInput)
    {
        if (vehicle == null || vehicleBody == null)
            return;

        var now = Time.unscaledTime;
        var parkedVehicleId = parkedVehicle.GetInstanceID();
        if (parkedContactVehicleId != parkedVehicleId || now > parkedContactDirectionUntil)
        {
            parkedContactVehicleId = parkedVehicleId;
            parkedContactDirection = Vector3.ProjectOnPlane(
                worldDriveDirection,
                Vector3.up).normalized;
        }
        parkedContactDirectionUntil = now + ParkedContactDirectionHoldTime;
        if (parkedContactDirection.sqrMagnitude < 0.9f)
            return;

        var sideDirection = Vector3.Cross(Vector3.up, parkedContactDirection).normalized;
        var velocity = vehicleBody.velocity;
        var lateralSpeed = Vector3.Dot(velocity, sideDirection);
        var steeringAmount = Mathf.Clamp01(Mathf.Abs(steeringInput));
        var lateralRetention = Mathf.Lerp(
            ParkedContactLateralVelocityRetention,
            ParkedContactSteeringLateralRetention,
            steeringAmount);
        velocity -= sideDirection *
                    (lateralSpeed * (1f - lateralRetention));
        var verticalSpeed = Vector3.Dot(velocity, Vector3.up);
        var descentCorrection = Mathf.Max(
            0f,
            -ParkedContactMaximumDescentSpeed - verticalSpeed);
        if (descentCorrection > 0f)
            velocity += Vector3.up * descentCorrection;
        vehicleBody.velocity = velocity;

        var localAngularVelocity = vehicle.transform.InverseTransformDirection(
            vehicleBody.angularVelocity);
        localAngularVelocity.y *= Mathf.Lerp(
            ParkedContactYawRetention,
            ParkedContactSteeringYawRetention,
            steeringAmount);
        localAngularVelocity.z *= ParkedContactRollRetention;
        vehicleBody.angularVelocity = vehicle.transform.TransformDirection(localAngularVelocity);

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

    private static bool IsLeadingEdgeContact(
        Collision collision,
        Transform vehicleTransform,
        float driveDirection)
    {
        for (var contactIndex = 0; contactIndex < collision.contactCount; contactIndex++)
        {
            var localPoint = vehicleTransform.InverseTransformPoint(
                collision.GetContact(contactIndex).point);
            if (localPoint.z * driveDirection >= 0.8f && localPoint.y <= 1.55f)
                return true;
        }
        return false;
    }

    private static Transform? FindParkedVehicle(Collider collider)
    {
        var parkedVehiclesLayer = LayerMask.NameToLayer("ParkedVehicles");
        if (parkedVehiclesLayer < 0)
            return null;
        for (var transform = collider.transform; transform != null; transform = transform.parent)
            if (transform.gameObject.layer == parkedVehiclesLayer)
                return transform;
        return null;
    }

    private static bool IsHeavyVehicle(VehicleController other)
    {
        var type = other.vehicleType;
        if (type == null)
            return true;
        return type.HasTag(TagRef.Vehicletag.istruck) ||
               type.maxCargoCapacity >= HeavyCargoCapacity;
    }

    private static bool IsTooHeavyToClimb(VehicleController other)
    {
        var identity = GetVehicleName(other);
        return !IsVordV150(identity) && IsHeavyVehicle(other);
    }

    private static string GetVehicleName(VehicleController other) =>
        other.vehicleType?.vehicleTypeName ?? other.name;

    private static bool IsHeavyVehicleIdentity(string vehicleName)
    {
        var identity = vehicleName.ToLowerInvariant();
        // Vanilla traffic names are stable while world-axis collider bounds change
        // as a car turns. Bounds-based classification misidentified ordinary cars.
        return identity.Contains("freighttruck") || identity.Contains("deliverytruck") ||
               identity.Contains("ambulance") || identity.Contains("vordv150");
    }

    private static bool IsTooHeavyToClimbIdentity(string vehicleName)
    {
        var identity = vehicleName.ToLowerInvariant();
        return identity.Contains("freighttruck") || identity.Contains("deliverytruck") ||
               identity.Contains("ambulance");
    }

    private static bool IsVordV150(string vehicleName) =>
        vehicleName.IndexOf("vordv150", StringComparison.OrdinalIgnoreCase) >= 0;

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
