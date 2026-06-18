using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestCharacterConfigFile
    {
        [DataMember(Name = "characters")]
        public StreetQuestCharacterDefinition[] characters;
    }
#pragma warning restore CS0649
}
