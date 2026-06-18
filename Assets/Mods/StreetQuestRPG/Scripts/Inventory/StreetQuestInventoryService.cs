using System;
using System.Collections.Generic;
using BigAmbitions.Items;
using Localizor;

namespace StreetQuestRPG
{
    internal static class StreetQuestInventoryService
    {
        private const string InventoryModDataKey = "streetquest:inventory_v1";

        public static bool AddItem(string itemId, int amount = 1)
        {
            if (!TryValidateRequest(itemId, amount))
                return false;

            var record = GetInventoryRecord();
            if (record == null)
                return false;

            record.SetAmount(itemId, record.GetAmount(itemId) + amount);
            SaveInventoryRecord(record);
            return true;
        }

        public static bool RemoveItem(string itemId, int amount = 1)
        {
            if (!TryValidateRequest(itemId, amount))
                return false;

            var record = GetInventoryRecord();
            if (record == null)
                return false;

            var currentAmount = record.GetAmount(itemId);
            if (currentAmount < amount)
                return false;

            record.SetAmount(itemId, currentAmount - amount);
            SaveInventoryRecord(record);
            return true;
        }

        public static int GetAmount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            var record = GetInventoryRecord();
            return record?.GetAmount(itemId) ?? 0;
        }

        public static bool HasItem(string itemId, int amount = 1)
        {
            return amount > 0 && GetAmount(itemId) >= amount;
        }

        public static IReadOnlyDictionary<string, int> GetAllItems()
        {
            var record = GetInventoryRecord();
            return record?.Items ?? new Dictionary<string, int>();
        }

        public static string GetDisplayName(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return string.Empty;

            return ItemsGetter.GetByName(itemId) != null
                ? itemId.GetLocalization().ToString()
                : itemId;
        }

        private static bool TryValidateRequest(string itemId, int amount)
        {
            return !string.IsNullOrWhiteSpace(itemId) && amount > 0;
        }

        private static StreetQuestInventoryRecord GetInventoryRecord()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return null;

            if (saveGame.modData == null)
                return new StreetQuestInventoryRecord();

            if (saveGame.modData.TryGetValue(InventoryModDataKey, out var serialized) &&
                !string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    return StreetQuestInventoryRecord.Deserialize(serialized);
                }
                catch
                {
                    return new StreetQuestInventoryRecord();
                }
            }

            return new StreetQuestInventoryRecord();
        }

        private static void SaveInventoryRecord(StreetQuestInventoryRecord record)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null || record == null)
                return;

            saveGame.modData ??= new Dictionary<string, string>();
            saveGame.modData[InventoryModDataKey] = record.Serialize();
            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }
    }
}
