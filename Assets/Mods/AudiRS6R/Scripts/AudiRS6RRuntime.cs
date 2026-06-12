#nullable enable
using System;
using BAModAPI;
using Helpers;
using UnityEngine;
using Vehicles.VehicleTypes;

public sealed class AudiRS6RRuntime : MonoBehaviour
{
    private const float DealerSyncInterval = 1f;
    private const bool DebugVehicleSpawnEnabled = false;
    private const float SpawnDistance = 6f;

    private float nextDealerSyncAt;
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
        runtime.nextDealerSyncAt = 0f;
        return runtime;
    }

    public void Shutdown()
    {
        Destroy(gameObject);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextDealerSyncAt)
        {
            nextDealerSyncAt = Time.unscaledTime + DealerSyncInterval;
            TryRegisterWithDealer();
        }

        if (DebugVehicleSpawnEnabled && Input.GetKeyDown(KeyCode.F9))
            TrySpawnVehicleInFrontOfPlayer();
    }

    private void TryRegisterWithDealer()
    {
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
            return;

        try
        {
            var dealerType = Type.GetType("BackAlleyDealer.BackAlleyDealerInit, BackAlleyDealer");
            var dealerInstance = dealerType?.GetProperty("Instance")?.GetValue(null);
            var registerMethod = dealerType?.GetMethod("RegisterVehicle");
            if (dealerInstance == null || registerMethod == null)
                return;

            registerMethod.Invoke(dealerInstance, new object[] { vehicleTypeName });
        }
        catch (Exception ex)
        {
            context?.Logger.Warn($"AudiRS6R: dealer sync failed: {ex.Message}");
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
