using System;
using System.Runtime.Serialization;

namespace StreetQuestRPG
{
#pragma warning disable CS0649
    [Serializable, DataContract]
    internal sealed class StreetQuestCharacterStateDefinition
    {
        [DataMember] public string id;
        [DataMember] public string[] requiredStoryFlags;
        [DataMember] public string[] forbiddenStoryFlags;
        [DataMember] public string[] requiredCompletedQuestIds;
        [DataMember] public string[] forbiddenCompletedQuestIds;
        [DataMember] public StreetQuestQuestFavorRequirementDefinition[] requiredFavors;
        [DataMember] public bool requireScheduleMatch;
        [DataMember] public bool requiredScheduleActive = true;
        [DataMember] public bool overrideEnabled;
        [DataMember] public bool enabled = true;
        [DataMember] public bool overrideUseFixedSpawnPosition;
        [DataMember] public bool useFixedSpawnPosition = true;
        [DataMember] public string appearanceId;
        [DataMember] public StreetQuestCharacterScheduleDefinition schedule;
        [DataMember] public StreetQuestVector3Data position;
        [DataMember] public StreetQuestVector3Data forward;
        [DataMember] public StreetQuestVector3Data[] walkAwayWaypoints;
        [DataMember] public float walkAwaySpeed;
        [DataMember] public bool overrideDespawnAfterWalkAway;
        [DataMember] public bool despawnAfterWalkAway;
        [DataMember] public StreetQuestVector3Data[] walkInWaypoints;
        [DataMember] public float walkInUnitsPerGameMinute;
        [DataMember] public int walkInArrivalHour;
        [DataMember] public int walkInArrivalMinute;
        [DataMember] public StreetQuestVector3Data localPosition;
        [DataMember] public StreetQuestVector3Data localEulerAngles;
        [DataMember] public StreetQuestVector3Data localScale;
        [DataMember] public StreetQuestVector3Data navTargetLocalOffset;
        [DataMember] public StreetQuestVector3Data sellerPositionLocalOffset;
        [DataMember] public StreetQuestVector3Data colliderCenterWithPrefab;
        [DataMember] public StreetQuestVector3Data colliderSizeWithPrefab;
        [DataMember] public StreetQuestVector3Data colliderCenterFallback;
        [DataMember] public StreetQuestVector3Data colliderSizeFallback;
        [DataMember] public StreetQuestVector3Data interactionRendererLocalPosition;
        [DataMember] public StreetQuestVector3Data interactionRendererLocalScale;
    }
#pragma warning restore CS0649
}
