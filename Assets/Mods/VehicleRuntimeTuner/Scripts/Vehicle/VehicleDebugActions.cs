#nullable enable
using System;
using BigAmbitions.SaveSystem.Legacy;
using Helpers;
using UnityEngine;
using VehicleRuntimeTuner.Applying;
using Vehicles.VehicleTypes;

namespace VehicleRuntimeTuner.Vehicle
{
    public sealed class VehicleDebugActions
    {
        private const float RespawnOffsetDistance = 3f;
        private const float RespawnHeightOffset = 0.5f;

        public bool TryRespawnTestVehicle(
            ActiveVehicleInfo activeVehicle,
            Profiles.VehicleTuningProfile profile,
            VehicleTuningApplier tuningApplier,
            out string message)
        {
            message = "Respawn failed.";
            if (activeVehicle.Root == null || string.IsNullOrWhiteSpace(activeVehicle.VehicleTypeName))
            {
                message = "No active vehicle root to respawn.";
                return false;
            }

            var vehicleType = VehicleTypeHelper.GetVehicleType(activeVehicle.VehicleTypeName);
            if (vehicleType == null)
            {
                message = $"Vehicle type '{activeVehicle.VehicleTypeName}' is not registered.";
                return false;
            }

            var origin = activeVehicle.Root.transform;
            var spawnPosition = origin.position + origin.right * RespawnOffsetDistance;
            spawnPosition.y += RespawnHeightOffset;
            var spawnRotation = origin.rotation;

            var vehicleInstance = new VehicleInstance(activeVehicle.VehicleTypeName)
            {
                id = CreateVehicleId(),
                fuel = vehicleType.maxFuel * 0.98f
            };

            VehicleHelper.CreateAndSpawnVehicle(vehicleInstance, spawnPosition, spawnRotation);
            var spawnedController = FindVehicleControllerById(vehicleInstance.id);
            if (spawnedController == null)
            {
                message = $"Spawned '{activeVehicle.VehicleTypeName}', but could not reacquire the controller.";
                return false;
            }

            VehicleHelper.TeleportVehicleToGround(spawnedController, spawnPosition, spawnRotation);
            var spawnedVehicle = ActiveVehicleResolver.CreateInfoFromController(
                spawnedController,
                spawnedController.vehicleInstance,
                spawnedController.vehicleType);
            tuningApplier.Apply(spawnedVehicle, profile);
            message = $"Respawned test vehicle '{activeVehicle.VehicleTypeName}'.";
            return true;
        }

        public bool TryTeleportCurrentVehicleToGround(ActiveVehicleInfo activeVehicle, out string message)
        {
            message = "Snap to ground failed.";
            if (activeVehicle.Root == null || string.IsNullOrWhiteSpace(activeVehicle.VehicleInstanceId))
            {
                message = "No active vehicle root to snap.";
                return false;
            }

            var vehicleController = FindVehicleControllerById(activeVehicle.VehicleInstanceId);
            if (vehicleController == null)
            {
                message = "Active vehicle controller was not found.";
                return false;
            }

            VehicleHelper.TeleportVehicleToGround(
                vehicleController,
                activeVehicle.Root.transform.position,
                activeVehicle.Root.transform.rotation);
            message = "Active vehicle snapped to ground.";
            return true;
        }

        public bool TryResetRigidbodyVelocity(ActiveVehicleInfo activeVehicle, out string message)
        {
            message = "Reset velocity failed.";
            if (activeVehicle.Rigidbody == null)
            {
                message = "Active vehicle has no Rigidbody.";
                return false;
            }

            activeVehicle.Rigidbody.velocity = Vector3.zero;
            activeVehicle.Rigidbody.angularVelocity = Vector3.zero;
            message = "Active vehicle velocity reset.";
            return true;
        }

        private static VehicleController? FindVehicleControllerById(string? vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                return null;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return null;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance == null)
                    continue;

                if (string.Equals(vehicleController.vehicleInstance.id, vehicleId, StringComparison.Ordinal))
                    return vehicleController;
            }

            return null;
        }

        private static string CreateVehicleId()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
    }
}
