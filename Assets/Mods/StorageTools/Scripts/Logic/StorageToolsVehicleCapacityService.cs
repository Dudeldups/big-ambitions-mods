#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace StorageTools
{
    internal sealed class StorageToolsVehicleCapacityService
    {
        private readonly Dictionary<string, int> originalCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string? lastActiveVehicleTypeName;

        public void ApplyConfiguredCapacities(ModContext context, StorageToolsSettings settings)
        {
            ApplyFreightTruckCapacity(context, settings.FreightTruckT1Capacity);

            var activeVehicle = FindActiveVehicleController();
            var activeVehicleTypeName = activeVehicle?.vehicleType?.vehicleTypeName;
            if (!string.Equals(lastActiveVehicleTypeName, activeVehicleTypeName, StringComparison.OrdinalIgnoreCase))
            {
                RestoreNonActiveOverride(lastActiveVehicleTypeName, settings.FreightTruckT1Capacity);
                lastActiveVehicleTypeName = activeVehicleTypeName;
            }

            if (activeVehicle?.vehicleType == null || string.IsNullOrWhiteSpace(activeVehicleTypeName))
                return;

            CaptureOriginalCapacity(activeVehicle.vehicleType);
            activeVehicle.vehicleType.maxCargoCapacity = settings.ActiveVehicleCapacity;
        }

        public void RestoreOriginalCapacities()
        {
            if (!string.IsNullOrWhiteSpace(lastActiveVehicleTypeName))
                RestoreVehicleType(lastActiveVehicleTypeName);

            RestoreVehicleType(StorageToolsTargetIds.FreightTruckT1VehicleTypeName);
            lastActiveVehicleTypeName = null;
        }

        private void ApplyFreightTruckCapacity(ModContext context, int capacity)
        {
            var freightTruckType = VehicleTypeHelper.GetVehicleType(StorageToolsTargetIds.FreightTruckT1VehicleTypeName);
            if (freightTruckType == null)
            {
                StorageToolsLogger.WarnOnce(
                    context,
                    "missing-vehicle-" + StorageToolsTargetIds.FreightTruckT1VehicleTypeName,
                    $"StorageTools: could not resolve vehicle type '{StorageToolsTargetIds.FreightTruckT1VehicleTypeName}'.");
                return;
            }

            CaptureOriginalCapacity(freightTruckType);
            freightTruckType.maxCargoCapacity = capacity;
        }

        private void CaptureOriginalCapacity(VehicleType vehicleType)
        {
            if (string.IsNullOrWhiteSpace(vehicleType.vehicleTypeName) || originalCapacities.ContainsKey(vehicleType.vehicleTypeName))
                return;

            originalCapacities[vehicleType.vehicleTypeName] = vehicleType.maxCargoCapacity;
        }

        private void RestoreNonActiveOverride(string? vehicleTypeName, int freightTruckCapacity)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
                return;

            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
            if (vehicleType == null)
                return;

            if (string.Equals(vehicleTypeName, StorageToolsTargetIds.FreightTruckT1VehicleTypeName, StringComparison.OrdinalIgnoreCase))
            {
                vehicleType.maxCargoCapacity = freightTruckCapacity;
                return;
            }

            if (originalCapacities.TryGetValue(vehicleTypeName, out var originalCapacity))
                vehicleType.maxCargoCapacity = originalCapacity;
        }

        private void RestoreVehicleType(string vehicleTypeName)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
                return;

            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
            if (vehicleType == null)
                return;

            if (originalCapacities.TryGetValue(vehicleTypeName, out var originalCapacity))
                vehicleType.maxCargoCapacity = originalCapacity;
        }

        private static VehicleController? FindActiveVehicleController()
        {
            foreach (var vehicleController in UnityEngine.Object.FindObjectsOfType<VehicleController>())
            {
                if (vehicleController != null && vehicleController.controlledByPlayer)
                    return vehicleController;
            }

            return null;
        }
    }
}
