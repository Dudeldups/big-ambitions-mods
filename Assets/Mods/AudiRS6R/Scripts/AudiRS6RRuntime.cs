#nullable enable
using System;
using BAModAPI;
using Helpers;
using UnityEngine;
using Vehicles.VehicleTypes;

public sealed class AudiRS6RRuntime : MonoBehaviour
{
    private const float DealerRetryInterval = 1f;
    private const bool DebugVehicleSpawnEnabled = false;
    private const float SpawnDistance = 6f;

    private bool dealerRegistrationPending;
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
        return runtime;
    }

    public void Shutdown()
    {
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
