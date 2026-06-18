using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StreetQuestRPG
{
    [Serializable]
    internal sealed class StreetQuestInventoryRecord
    {
        [SerializeField]
        private List<StreetQuestInventoryEntryData> entries = new();

        private Dictionary<string, int> _cache;

        public IReadOnlyDictionary<string, int> Items => EnsureCache();

        public int GetAmount(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            return EnsureCache().TryGetValue(itemId, out var amount) ? amount : 0;
        }

        public void SetAmount(string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            var cache = EnsureCache();
            if (amount <= 0)
            {
                cache.Remove(itemId);
            }
            else
            {
                cache[itemId] = amount;
            }

            RebuildEntries();
        }

        public string Serialize()
        {
            RebuildEntries();
            return JsonUtility.ToJson(this);
        }

        public static StreetQuestInventoryRecord Deserialize(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
                return new StreetQuestInventoryRecord();

            var record = JsonUtility.FromJson<StreetQuestInventoryRecord>(serialized);
            record ??= new StreetQuestInventoryRecord();
            record.EnsureCache();
            return record;
        }

        private Dictionary<string, int> EnsureCache()
        {
            if (_cache != null)
                return _cache;

            _cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
                return _cache;

            foreach (var entry in entries.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.itemId) &&
                         value.amount > 0))
            {
                _cache[entry.itemId] = entry.amount;
            }

            return _cache;
        }

        private void RebuildEntries()
        {
            var cache = EnsureCache();
            entries = cache
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new StreetQuestInventoryEntryData
                {
                    itemId = pair.Key,
                    amount = pair.Value
                })
                .ToList();
        }
    }

    [Serializable]
    internal sealed class StreetQuestInventoryEntryData
    {
        public string itemId;
        public int amount;
    }
}
