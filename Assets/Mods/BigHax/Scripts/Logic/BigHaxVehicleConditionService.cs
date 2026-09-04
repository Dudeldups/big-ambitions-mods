#nullable enable
using System;
using BAModAPI;
using Helpers;
using Vehicles.VehicleTypes;

namespace BigHax
{
    /// <summary>
    /// Keeps player vehicles pristine through the game's existing vehicle-change events.
    /// No Update polling is used: the game emits onVehicleVariablesChanged whenever a
    /// vehicle's fuel, dirtiness, or damage state changes.
    /// </summary>
    internal sealed class BigHaxVehicleConditionService
    {
        public void ApplyConfiguredConditions(BigHaxSettings settings)
        {
            if (!settings.EnableNoVehicleDamage &&
                !settings.EnableInfiniteVehicleFuel &&
                !settings.EnableNeverDirtyVehicles)
            {
                return;
            }

            var controllers = VehicleHelper.AllPlayerVehicles;
            if (controllers != null)
            {
                foreach (var controller in controllers)
                {
                    if (controller?.vehicleInstance == null)
                        continue;

                    ApplyControllerConditions(controller, settings);
                }
            }

            // Process controllers first: Repair and SetDirtiness also refresh the
            // currently visible vehicle meshes. The save pass then covers vehicles
            // that are not presently instantiated in the scene.
            var changed = false;
            var savedVehicles = SaveGameManager.Current?.VehicleInstances;
            if (savedVehicles != null)
            {
                foreach (var vehicle in savedVehicles)
                    changed |= ApplySavedVehicleConditions(vehicle, settings);
            }

            if (changed)
                SaveGameManager.MarkChange();
        }

        private static bool ApplySavedVehicleConditions(VehicleInstance? vehicle, BigHaxSettings settings)
        {
            if (vehicle == null)
                return false;

            var changed = false;
            if (settings.EnableNoVehicleDamage && (vehicle.damage > 0.001f || (vehicle.deformations?.Count ?? 0) > 0))
            {
                vehicle.damage = 0f;
                vehicle.deformations?.Clear();
                changed = true;
            }

            if (settings.EnableNeverDirtyVehicles && vehicle.dirtiness > 0.001f)
            {
                vehicle.dirtiness = 0f;
                changed = true;
            }

            if (settings.EnableInfiniteVehicleFuel)
            {
                var vehicleType = VehicleTypeHelper.GetVehicleType(vehicle.vehicleTypeName);
                if (vehicleType != null && vehicleType.maxFuel > 0f && vehicle.fuel < vehicleType.maxFuel - 0.001f)
                {
                    vehicle.fuel = vehicleType.maxFuel;
                    changed = true;
                }
            }

            return changed;
        }

        private static void ApplyControllerConditions(VehicleController controller, BigHaxSettings settings)
        {
            var vehicle = controller.vehicleInstance;
            if (vehicle == null)
                return;

            // Use the controller setters for loaded cars so the visible fuel gauge,
            // dirt material, and deformation mesh update immediately as well.
            if (settings.EnableInfiniteVehicleFuel &&
                controller.vehicleType != null &&
                controller.vehicleType.maxFuel > 0f &&
                vehicle.fuel < controller.vehicleType.maxFuel - 0.001f)
            {
                controller.SetFuel(controller.vehicleType.maxFuel);
            }

            if (settings.EnableNeverDirtyVehicles && vehicle.dirtiness > 0.001f)
                controller.SetDirtiness(0f);

            if (settings.EnableNoVehicleDamage &&
                (vehicle.damage > 0.001f || (vehicle.deformations?.Count ?? 0) > 0))
            {
                controller.Repair();
            }
        }
    }
}
