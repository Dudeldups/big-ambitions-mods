using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestRewardDefinition
    {
        [DataMember] public string type;
        [DataMember] public int amount;
        [DataMember] public string storyFlagId;
        [DataMember] public string characterId;

        public int Amount => amount;
        public string StoryFlagId => storyFlagId;
        public string CharacterId => characterId;

        public StreetQuestQuestRewardType RewardType
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(type) &&
                    Enum.TryParse(type, true, out StreetQuestQuestRewardType parsed))
                    return parsed;

                return StreetQuestQuestRewardType.None;
            }
        }
    }
#pragma warning restore CS0649
}
