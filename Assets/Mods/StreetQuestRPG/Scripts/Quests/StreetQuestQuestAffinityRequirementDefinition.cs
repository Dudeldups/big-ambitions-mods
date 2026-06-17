using System;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable]
    internal sealed class StreetQuestQuestAffinityRequirementDefinition
    {
        public string characterId;
        public int minValue = -100;
        public int maxValue = 100;

        public string CharacterId => characterId;
        public int MinValue => minValue < -100 ? -100 : minValue;
        public int MaxValue => maxValue > 100 ? 100 : maxValue;
    }
#pragma warning restore CS0649
}
