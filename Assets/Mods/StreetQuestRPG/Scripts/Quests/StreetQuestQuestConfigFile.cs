using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestConfigFile
    {
        [DataMember(Name = "quests")]
        public StreetQuestQuestDefinition[] quests;
    }
#pragma warning restore CS0649
}
