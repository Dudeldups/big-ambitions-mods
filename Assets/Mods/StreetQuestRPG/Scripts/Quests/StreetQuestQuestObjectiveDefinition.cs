using System;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable]
    internal sealed class StreetQuestQuestObjectiveDefinition
    {
        public string id;
        public string type;
        public string itemName;
        public int amount = 1;
        public string characterId;
        public string questId;
        public string storyFlagId;
        public string inventorySource;
        public string locationId;
        public StreetQuestVector3Data worldPosition;
        public float radius = 2.5f;
        public string progressTextKey;

        public string Id => string.IsNullOrWhiteSpace(id) ? "objective" : id;
        public string ItemName => itemName;
        public int Amount => amount <= 0 ? 1 : amount;
        public string CharacterId => characterId;
        public string QuestId => questId;
        public string StoryFlagId => storyFlagId;
        public string InventorySourceRaw => inventorySource;
        public string LocationId => locationId;
        public float Radius => radius <= 0f ? 2.5f : radius;
        public string ProgressTextKey => progressTextKey;

        public StreetQuestQuestObjectiveType ObjectiveType
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(type) &&
                    Enum.TryParse(type, true, out StreetQuestQuestObjectiveType parsed))
                    return parsed;

                return StreetQuestQuestObjectiveType.None;
            }
        }

        public StreetQuestQuestInventorySource InventorySource
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(inventorySource) &&
                    Enum.TryParse(inventorySource, true, out StreetQuestQuestInventorySource parsed))
                {
                    return parsed;
                }

                return StreetQuestQuestInventorySource.Vanilla;
            }
        }

        public string GetTrackingToken(string questId)
        {
            var stableId = string.IsNullOrWhiteSpace(Id) ? "objective" : Id;
            return $"objective:{questId}:{stableId}";
        }
    }
#pragma warning restore CS0649
}
