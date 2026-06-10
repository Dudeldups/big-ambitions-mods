#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BackAlleyDealer;
using BAModAPI;
using Helpers;
using UnityEngine;
using Vehicles.VehicleTypes;

public sealed class ExampleVehicleRuntime : MonoBehaviour
{
    private const float SpawnDistance = 6f;
    private static readonly StringComparison VehicleNameComparison = StringComparison.OrdinalIgnoreCase;

    private string[] vehicleTypeNames = Array.Empty<string>();
    private BackAlleyDealerInit? registeredDealerInstance;
    private ModContext? context;

    public static ExampleVehicleRuntime Initialize(ModContext context, IEnumerable<VehicleType> vehicleTypes)
    {
        var runtime = FindObjectOfType<ExampleVehicleRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(ExampleVehicleRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<ExampleVehicleRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeNames = vehicleTypes
            .Select(vehicleType => vehicleType.vehicleTypeName)
            .Where(vehicleTypeName => !string.IsNullOrWhiteSpace(vehicleTypeName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        runtime.registeredDealerInstance = null;
        runtime.TryRegisterVehiclesWithDealer();
        Debug.Log(
            $"ExampleVehicleRuntime: loaded vehicle types [{string.Join(", ", runtime.vehicleTypeNames)}]. Press F9 to spawn the first one in front of the player.");
        return runtime;
    }

    public void Shutdown()
    {
        registeredDealerInstance = null;
        Destroy(gameObject);
    }

    private void Update()
    {
        TryRegisterVehiclesWithDealer();

        if (Input.GetKeyDown(KeyCode.F9))
            TrySpawnVehicleInFrontOfPlayer();
    }

    private void TryRegisterVehiclesWithDealer()
    {
        var dealerInstance = BackAlleyDealerInit.Instance;
        if (dealerInstance == null || ReferenceEquals(registeredDealerInstance, dealerInstance))
            return;

        foreach (var vehicleTypeName in vehicleTypeNames)
            dealerInstance.RegisterVehicle(vehicleTypeName);

        registeredDealerInstance = dealerInstance;
        context?.Logger.Info(
            $"Registered {vehicleTypeNames.Length} vehicle type(s) with Back Alley Dealer: {string.Join(", ", vehicleTypeNames)}");
    }

    private void TrySpawnVehicleInFrontOfPlayer()
    {
        var vehicleTypeName = GetPreferredVehicleTypeName();
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
        {
            Debug.LogWarning("ExampleVehicleRuntime: no vehicle types are available for the F9 test spawn.");
            return;
        }

        var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
        if (vehicleType == null)
        {
            Debug.LogWarning($"ExampleVehicleRuntime: vehicle type '{vehicleTypeName}' is not registered.");
            return;
        }

        var playerController = GameManager.Instance?.playerController;
        if (playerController == null)
        {
            Debug.LogWarning("ExampleVehicleRuntime: player controller is unavailable, cannot spawn vehicle.");
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
        Debug.Log(
            $"ExampleVehicleRuntime: spawned '{vehicleTypeName}' with id '{vehicleInstance.id}' at {spawnPosition}.");
    }

    private string? GetPreferredVehicleTypeName()
    {
        if (vehicleTypeNames.Length == 0)
            return null;

        foreach (var vehicleTypeName in vehicleTypeNames)
        {
            if (vehicleTypeName.IndexOf("mimic", VehicleNameComparison) >= 0 ||
                vehicleTypeName.IndexOf("honza", VehicleNameComparison) >= 0)
                return vehicleTypeName;
        }

        return vehicleTypeNames[0];
    }

    private static string CreateVehicleId()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }
}
