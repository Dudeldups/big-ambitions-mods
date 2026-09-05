#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Helpers;

namespace FreelancePhotographer
{
    internal sealed class PhotographyEquipmentSnapshot
    {
        internal int CameraTier;
        internal bool HasLens;
        internal bool HasTripod;
        internal bool HasFlash;

        internal int QualityCap => CameraTier >= 3 ? 100 : CameraTier == 2 ? 90 : CameraTier == 1 ? 70 : 0;

        internal bool HasAccessory(PhotographyAccessory accessory)
        {
            return accessory == PhotographyAccessory.None ||
                   accessory == PhotographyAccessory.Lens && HasLens ||
                   accessory == PhotographyAccessory.Tripod && HasTripod ||
                   accessory == PhotographyAccessory.Flash && HasFlash;
        }
    }

    internal static class PhotographyEquipmentService
    {
        internal static PhotographyEquipmentSnapshot Capture()
        {
            var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hands = PlayerHelper.ItemInstanceInHands;
            if (hands != null)
            {
                Add(itemIds, hands.itemName);
                AddCargo(itemIds, hands.cargoInstances);
            }

            var save = SaveGameManager.Current;
            if (save != null && !string.IsNullOrWhiteSpace(save.ActiveVehicleId))
            {
                var vehicle = save.VehicleInstances?.FirstOrDefault(value =>
                    value != null && string.Equals(value.id, save.ActiveVehicleId, StringComparison.Ordinal));
                if (vehicle != null)
                    AddCargo(itemIds, vehicle.cargoInstances);
            }

            return new PhotographyEquipmentSnapshot
            {
                CameraTier = itemIds.Contains(FreelancePhotographerIds.ProfessionalCamera) ? 3 :
                    itemIds.Contains(FreelancePhotographerIds.DslrCamera) ? 2 :
                    itemIds.Contains(FreelancePhotographerIds.CompactCamera) ? 1 : 0,
                HasLens = itemIds.Contains(FreelancePhotographerIds.Lens),
                HasTripod = itemIds.Contains(FreelancePhotographerIds.Tripod),
                HasFlash = itemIds.Contains(FreelancePhotographerIds.Flash)
            };
        }

        internal static List<string> FindMissingCameraStoreItems()
        {
            var missing = new List<string>();
            if (ItemsGetter.AllItems == null)
            {
                missing.AddRange(FreelancePhotographerIds.CameraStoreItems);
                return missing;
            }

            foreach (var itemId in FreelancePhotographerIds.CameraStoreItems)
            {
                if (ItemsGetter.GetByName(itemId, true) == null)
                    missing.Add(itemId);
            }

            return missing;
        }

        private static void AddCargo(HashSet<string> itemIds, IEnumerable<CargoInstance>? cargo)
        {
            if (cargo == null)
                return;

            foreach (var entry in cargo)
            {
                if (entry == null || entry.amount <= 0)
                    continue;

                Add(itemIds, entry.itemName);
                if (entry.nestedCargoInstances == null)
                    continue;

                foreach (var nested in entry.nestedCargoInstances)
                {
                    if (nested != null && nested.amount > 0)
                        Add(itemIds, nested.itemName);
                }
            }
        }

        private static void Add(HashSet<string> itemIds, string? itemId)
        {
            if (!string.IsNullOrWhiteSpace(itemId))
                itemIds.Add(itemId!);
        }
    }
}
