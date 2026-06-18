using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestFavorRequirementDefinition
    {
        [DataMember] public string characterId;
        [DataMember] public int minValue = -100;
        [DataMember] public int maxValue = 100;

        public string CharacterId => characterId;
        public int MinValue => minValue < -100 ? -100 : minValue;
        public int MaxValue => maxValue > 100 ? 100 : maxValue;
    }
#pragma warning restore CS0649
}
