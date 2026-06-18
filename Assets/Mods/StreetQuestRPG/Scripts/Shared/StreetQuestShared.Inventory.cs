using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        public static int GetPlayerItemAmount(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return 0;

            return GetVanillaPlayerItemAmount(itemName);
        }


        public static int GetQuestInventoryItemAmount(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return 0;

            return StreetQuestInventoryService.GetAmount(itemName);
        }


        public static bool TryGivePlayerQuestItem(string itemName, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemName) || amount <= 0)
                return false;

            var succeeded = TryAddItemToHeldInventory(itemName, amount);
            LogDebug($"TryGivePlayerQuestItem item={itemName} requested={amount} succeeded={succeeded}");
            if (!succeeded)
                return false;

            var displayName = ResolveItemDisplayName(itemName);
            ShowDebugNotification(
                $"Added {amount}x {displayName} to held inventory",
                $"streetquest-debug-spawn-{itemName}");
            return true;
        }


        private static bool TryConsumeQuestItems(string itemName, int amount, StreetQuestQuestInventorySource inventorySource)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0)
                return false;

            var remainingAmount = amount;
            if (inventorySource != StreetQuestQuestInventorySource.Quest)
            {
                var holder = GetPlayerInventoryHolder();
                var cargoInstances = holder?.GetCargoInstances();
                if (cargoInstances != null)
                {
                    foreach (var cargoInstance in cargoInstances.Where(x => x != null && x.itemName == itemName).ToList())
                    {
                        var amountToRemove = Math.Min(cargoInstance.amount, remainingAmount);
                        holder.ReduceFromCargo(cargoInstance, amountToRemove);
                        remainingAmount -= amountToRemove;
                        if (remainingAmount <= 0)
                            return true;
                    }
                }
            }

            if (remainingAmount > 0 &&
                inventorySource != StreetQuestQuestInventorySource.Vanilla &&
                StreetQuestInventoryService.GetAmount(itemName) >= remainingAmount)
            {
                if (StreetQuestInventoryService.RemoveItem(itemName, remainingAmount))
                    remainingAmount = 0;
            }

            return remainingAmount <= 0;
        }


        private static int GetVanillaPlayerItemAmount(string itemName)
        {
            var holder = GetPlayerInventoryHolder();
            return holder?.GetAmountByItemName(itemName) ?? 0;
        }


        private static bool TryAddItemToHeldInventory(string itemName, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemName) || amount <= 0)
                return false;

            var heldItem = PlayerHelper.ItemInstanceInHands;
            var currentContents = heldItem != null
                ? GetAggregatedContents(heldItem)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            currentContents[itemName] = currentContents.TryGetValue(itemName, out var existingAmount)
                ? existingAmount + amount
                : amount;

            if (!TryBuildHeldInventoryItem(currentContents, heldItem?.itemName, out var replacementHeldItem))
                return false;

            ApplyHeldInventoryItem(replacementHeldItem);
            return true;
        }


        private static bool TryBuildHeldInventoryItem(
            IReadOnlyDictionary<string, int> contents,
            string preferredContainerItemName,
            out ItemInstance heldItem)
        {
            heldItem = null;
            if (contents == null || contents.Count == 0)
                return false;

            var bagItemName = string.IsNullOrWhiteSpace(preferredContainerItemName)
                ? ItemsGetter.GetRandomBag()
                : preferredContainerItemName;
            if (string.IsNullOrWhiteSpace(bagItemName))
                return false;

            var rebuiltBag = new ItemInstance(bagItemName);
            foreach (var pair in contents.Where(value => !string.IsNullOrWhiteSpace(value.Key) && value.Value > 0)
                         .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
            {
                var cargoInstance = new CargoInstance(pair.Key, pair.Value, 0f, true);
                if (!rebuiltBag.TryToAddToCargo(cargoInstance))
                    return false;
            }

            rebuiltBag.OnItemsInCargoUpdated();
            heldItem = rebuiltBag;
            return true;
        }


        private static void ApplyHeldInventoryItem(ItemInstance heldItem)
        {
            if (heldItem == null)
                return;

            PlayerHelper.ItemInstanceInHands = heldItem;
            PlayerHelper.OnItemInHandsCargoUpdated();
        }


        private static Dictionary<string, int> GetAggregatedContents(ItemInstance heldItem)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cargo in heldItem?.GetCargoInstances() ?? new List<CargoInstance>())
            {
                AppendCargo(result, cargo);
            }

            return result;
        }


        private static void AppendCargo(IDictionary<string, int> contents, CargoInstance cargo)
        {
            if (cargo == null)
                return;

            AddAmount(contents, cargo.itemName, cargo.amount);
            if (cargo.nestedCargoInstances == null)
                return;

            foreach (var nestedCargo in cargo.nestedCargoInstances)
            {
                if (nestedCargo == null)
                    continue;

                AddAmount(contents, nestedCargo.itemName, nestedCargo.amount);
            }
        }


        private static void AddAmount(IDictionary<string, int> contents, string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
                return;

            contents[itemId] = contents.TryGetValue(itemId, out var existingAmount)
                ? existingAmount + amount
                : amount;
        }


        private static string ResolveItemDisplayName(string itemName)
        {
            return ItemsGetter.GetByName(itemName) != null
                ? itemName.GetLocalization().ToString()
                : itemName;
        }


        private static ICargoHolder GetPlayerInventoryHolder()
        {
            return PlayerHelper.GetCurrentCargoHolder();
        }
    }
}
