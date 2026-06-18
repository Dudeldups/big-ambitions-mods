using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestFavorStateEntry
    {
        [DataMember(Name = "characterId")] public string characterId;
        [DataMember(Name = "value")] public int value;
    }
#pragma warning restore CS0649
}
