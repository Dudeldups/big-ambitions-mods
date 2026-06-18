using System;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable]
    internal sealed class StreetQuestCharacterStateDefinition
    {
        public string id;
        public string[] requiredStoryFlags;
        public string[] forbiddenStoryFlags;
        public string[] requiredCompletedQuestIds;
        public string[] forbiddenCompletedQuestIds;
        public StreetQuestQuestFavorRequirementDefinition[] requiredFavors;
        public bool overrideEnabled;
        public bool enabled = true;
        public bool overrideUseFixedSpawnPosition;
        public bool useFixedSpawnPosition = true;
        public string appearanceId;
        public StreetQuestVector3Data position;
        public StreetQuestVector3Data forward;
        public StreetQuestVector3Data localPosition;
        public StreetQuestVector3Data localEulerAngles;
        public StreetQuestVector3Data localScale;
        public StreetQuestVector3Data navTargetLocalOffset;
        public StreetQuestVector3Data sellerPositionLocalOffset;
        public StreetQuestVector3Data colliderCenterWithPrefab;
        public StreetQuestVector3Data colliderSizeWithPrefab;
        public StreetQuestVector3Data colliderCenterFallback;
        public StreetQuestVector3Data colliderSizeFallback;
        public StreetQuestVector3Data interactionRendererLocalPosition;
        public StreetQuestVector3Data interactionRendererLocalScale;
    }
#pragma warning restore CS0649
}
