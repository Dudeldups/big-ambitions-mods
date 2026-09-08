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
    private const int HeavyCargoCapacity = 32;

    private readonly List<VehicleDeformationController.VehicleDeformation> approvedDeformations = new();
    private VehicleController? vehicle;
    private VehicleDeformationController? deformationController;
    private FieldInfo? deformationQueueField;
    private ModContext? context;
    private NWH.VehiclePhysics2.VehicleController? physicsVehicle;
    private UnityAction<Collision>? collisionListener;
    private float approvedDamage;
    private float suppressDamageUntil;
    private int heavyImpactFrame = -1;
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
            if (isHeavy)
            {
                heavyImpactFrame = Time.frameCount;
                suppressDamageUntil = 0f;
                context?.Logger.Info(
                    $"BigfootMonsterTruck: heavy vehicle impact kept damage-enabled " +
                    $"other='{otherName}'.");
                return;
            }

            if (heavyImpactFrame == Time.frameCount)
                return;
            suppressDamageUntil = Time.unscaledTime + SmallVehicleSuppressionWindow;
            ClearPendingDeformationQueue();
            context?.Logger.Info(
                $"BigfootMonsterTruck: suppressing small-vehicle impact damage " +
                $"other='{otherName}' for {SmallVehicleSuppressionWindow:F2}s.");
        }
        catch (Exception exception)
        {
            ReportFailureOnce(nameof(HandleVehicleCollision), exception);
        }
    }

    private void OnDestroy()
    {
        if (physicsVehicle != null && collisionListener != null)
            physicsVehicle.onCollision.RemoveListener(collisionListener);
        physicsVehicle = null;
        collisionListener = null;
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
        if (identity.Contains("truck") || identity.Contains("lorry") ||
            identity.Contains("semi") || identity.Contains("delivery"))
            return true;

        var colliders = trafficVehicle.GetComponentsInChildren<Collider>(true);
        var bounds = default(Bounds);
        var hasBounds = false;
        foreach (var collider in colliders)
        {
            if (collider.isTrigger)
                continue;
            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        if (!hasBounds)
            return false;
        return bounds.size.y >= 2.4f || bounds.size.z >= 5.5f || bounds.size.x >= 2.5f;
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
