using System;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable]
    internal sealed class StreetQuestQuestRewardDefinition
    {
        public string type;
        public int amount;
        public string storyFlagId;

        public int Amount => amount;
        public string StoryFlagId => storyFlagId;

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
