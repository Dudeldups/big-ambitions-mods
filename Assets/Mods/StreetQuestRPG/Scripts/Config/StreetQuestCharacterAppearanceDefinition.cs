using System;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable]
    internal sealed class StreetQuestCharacterAppearanceDefinition
    {
        public string id;
        public string visualObjectName;
        public string fallbackLabel;
        public string gender;
        public int ageInDays;
        public int appearanceSeed;
        public string[] prefabNames;
        public StreetQuestVector3Data localPosition;
        public StreetQuestVector3Data localEulerAngles;
        public StreetQuestVector3Data localScale;
        public StreetQuestVector3Data colliderCenterWithPrefab;
        public StreetQuestVector3Data colliderSizeWithPrefab;
        public StreetQuestVector3Data colliderCenterFallback;
        public StreetQuestVector3Data colliderSizeFallback;
        public StreetQuestVector3Data interactionRendererLocalPosition;
        public StreetQuestVector3Data interactionRendererLocalScale;
    }
#pragma warning restore CS0649
}
