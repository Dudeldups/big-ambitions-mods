#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using Helpers;
using Localizor;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsVehicleService
    {
        private const float SpawnDistance = 7f;
        private readonly List<CatalogEntry> entries = new List<CatalogEntry>();
        private readonly ModContext context;
        private string lastSpawnedVehicleId = string.Empty;

        public DeveloperToolsVehicleService(ModContext context) => this.context = context;
        public IReadOnlyList<CatalogEntry> Entries => entries;

        public void Refresh()
        {
            entries.Clear();
            foreach (var id in VehicleTypeHelper.GetVehicleTypeNames().Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
            {
                if (VehicleTypeHelper.GetVehicleType(id) != null)
                    entries.Add(new CatalogEntry(id, Localize(id)));
            }
            entries.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        public bool Spawn(string vehicleTypeName, out string message)
        {
            var player = PlayerHelper.PlayerController;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
            if (player == null || vehicleType == null)
            {
                message = player == null ? "Player is not available." : "Selected vehicle is no longer registered.";
                context.Logger.Warn("DeveloperTools: vehicle spawn failed; " + message);
                return false;
            }

            try
            {
                var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                var position = player.transform.position + forward * SpawnDistance + Vector3.up * 0.5f;
                var instance = new VehicleInstance(vehicleTypeName)
                {
                    id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                    fuel = vehicleType.maxFuel * 0.98f
                };
                var controller = VehicleHelper.CreateAndSpawnVehicle(instance, position, rotation);
                if (controller == null)
                {
                    message = "The game did not return a spawned vehicle controller.";
                    context.Logger.Warn("DeveloperTools: vehicle spawn failed; type=" + vehicleTypeName + ", reason=no controller.");
                    return false;
                }

                VehicleHelper.TeleportVehicleToGround(controller, position, rotation);
                lastSpawnedVehicleId = instance.id;
                message = "Spawned " + Localize(vehicleTypeName) + ".";
                return true;
            }
            catch (Exception exception)
            {
                message = "Vehicle spawn failed: " + exception.Message;
                context.Logger.Error(exception);
                return false;
            }
        }

        public bool DespawnLast(out string message)
        {
            if (string.IsNullOrEmpty(lastSpawnedVehicleId))
            {
                message = "No test vehicle has been spawned in this session.";
                return false;
            }

            var controller = VehicleHelper.AllPlayerVehicles?.FirstOrDefault(value =>
                value?.vehicleInstance != null && string.Equals(value.vehicleInstance.id, lastSpawnedVehicleId, StringComparison.Ordinal));
            if (controller == null)
            {
                message = "The last spawned vehicle is no longer present.";
                lastSpawnedVehicleId = string.Empty;
                return false;
            }

            VehicleHelper.Delete(controller.vehicleInstance, controller);
            lastSpawnedVehicleId = string.Empty;
            message = "Despawned the last test vehicle.";
            return true;
        }

        private static string Localize(string id)
        {
            try
            {
                var localized = id.GetLocalization()?.ToString();
                return string.IsNullOrWhiteSpace(localized) ? id : localized!;
            }
            catch
            {
                return id;
            }
        }
    }

    internal sealed class CatalogEntry
    {
        public CatalogEntry(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }
}
