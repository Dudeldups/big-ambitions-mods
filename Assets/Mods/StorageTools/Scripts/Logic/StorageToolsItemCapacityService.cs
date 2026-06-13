#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Items;
using Helpers;
using UnityEngine;

namespace StorageTools
{
    internal sealed class StorageToolsItemCapacityService
    {
        private readonly Dictionary<string, int> originalCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public void ApplyConfiguredCapacities(ModContext context, StorageToolsSettings settings)
        {
            ApplyItemCapacity(context, StorageToolsTargetIds.StandardFridgeItemName, settings.StandardFridgeCapacity);
            ApplyItemCapacity(context, StorageToolsTargetIds.PalletShelfItemName, settings.PalletShelfCapacity);
            RefreshCurrentCargoHolder(StorageToolsTargetIds.StandardFridgeItemName);
            RefreshCurrentCargoHolder(StorageToolsTargetIds.PalletShelfItemName);
        }

        public void RestoreOriginalCapacities()
        {
            foreach (var item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemName))
                    continue;

                if (!originalCapacities.TryGetValue(item.itemName, out var originalCapacity))
                    continue;

                item.cargoCapacity = originalCapacity;
            }
        }

        private void ApplyItemCapacity(ModContext context, string itemName, int capacity)
        {
            var foundAny = false;
            foreach (var item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item == null || !string.Equals(item.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foundAny = true;
                if (!originalCapacities.ContainsKey(itemName))
                    originalCapacities[itemName] = item.cargoCapacity;

                item.cargoCapacity = capacity;
            }

            if (!foundAny)
                StorageToolsLogger.WarnOnce(context, "missing-item-" + itemName, $"StorageTools: could not resolve item definition '{itemName}'.");
        }

        private static void RefreshCurrentCargoHolder(string itemName)
        {
            if (PlayerHelper.GetCurrentCargoHolder() is not ItemInstance itemInstance)
                return;

            if (!string.Equals(itemInstance.itemName, itemName, StringComparison.OrdinalIgnoreCase))
                return;

            itemInstance.GetMaxCargoSize();
            TriggerCargoUpdated(itemInstance);
        }

        private static void TriggerCargoUpdated(ItemInstance itemInstance)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = typeof(ItemInstance).GetMethod("OnItemsInCargoUpdated", Flags);
            if (method == null)
                return;

            try
            {
                var callback = method.Invoke(itemInstance, null) as Action;
                callback?.Invoke();
            }
            catch
            {
                // Best effort only; some UI paths update lazily from the patched capacity anyway.
            }
        }
    }
}
