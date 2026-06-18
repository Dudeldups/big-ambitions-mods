using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestCharacterAppearanceFlagMapping
    {
        [DataMember] public string storyFlagId;
        [DataMember] public string appearanceId;
    }
#pragma warning restore CS0649
}
