using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestIntroStageDefinition
    {
        [DataMember] public string textKey;
        [DataMember] public string confirmTextKey;

        public string TextKey => textKey;
        public string ConfirmTextKey => confirmTextKey;
    }
#pragma warning restore CS0649
}
