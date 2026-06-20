using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestObjectiveDefinition
    {
        [DataMember] public string id;
        [DataMember] public string type;
        [DataMember] public string itemName;
        [DataMember] public int amount = 1;
        [DataMember] public string characterId;
        [DataMember] public string questId;
        [DataMember] public string storyFlagId;
        [DataMember] public string inventorySource;
        [DataMember] public string locationId;
        [DataMember] public StreetQuestVector3Data worldPosition;
        [DataMember] public float radius = 2.5f;
        [DataMember] public string progressTextKey;
        [DataMember] public string dialogTextKey;
        [DataMember] public string confirmTextKey;
        [DataMember] public string afterConfirmTextKey;
        [DataMember] public string[] completedStoryFlags;

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
        public string DialogTextKey => dialogTextKey;
        public string ConfirmTextKey => confirmTextKey;
        public string AfterConfirmTextKey => afterConfirmTextKey;
        public string[] CompletedStoryFlags => completedStoryFlags ?? Array.Empty<string>();

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
