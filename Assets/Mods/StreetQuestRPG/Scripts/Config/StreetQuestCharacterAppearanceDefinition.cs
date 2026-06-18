using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestCharacterAppearanceDefinition
    {
        [DataMember] public string id;
        [DataMember] public string visualObjectName;
        [DataMember] public string gender;
        [DataMember] public int ageInDays;
        [DataMember] public int appearanceSeed;
        [DataMember] public string prefabName;
        [DataMember] public StreetQuestVector3Data localPosition;
        [DataMember] public StreetQuestVector3Data localEulerAngles;
        [DataMember] public StreetQuestVector3Data localScale;
        [DataMember] public StreetQuestVector3Data colliderCenterWithPrefab;
        [DataMember] public StreetQuestVector3Data colliderSizeWithPrefab;
        [DataMember] public StreetQuestVector3Data colliderCenterFallback;
        [DataMember] public StreetQuestVector3Data colliderSizeFallback;
        [DataMember] public StreetQuestVector3Data interactionRendererLocalPosition;
        [DataMember] public StreetQuestVector3Data interactionRendererLocalScale;
    }
#pragma warning restore CS0649
}
