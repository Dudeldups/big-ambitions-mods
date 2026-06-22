using System;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;

namespace StreetQuestRPG
{
    [Serializable, DataContract]
    internal sealed class StreetQuestCharacterDefinition
    {
        [DataMember] public string id;
        [DataMember] public string displayName;
        [DataMember] public string nameKey;
        [DataMember] public string contactId;
        [DataMember] public string contactDescriptionKey;
        [DataMember] public string contactCategory;
        [DataMember] public string dialogTypeKey;
        [DataMember] public string gameObjectName;
        [DataMember] public string visualObjectName;
        [DataMember] public string overlayHeaderKey;
        [DataMember] public string ctaKey;
        [DataMember] public bool showSpeechBubble;
        [DataMember] public string speechBubbleEmojiName;
        [DataMember] public string speechBubbleColor;
        [DataMember] public string speechBubbleTextKey;
        [DataMember] public string[] speechBubbleTextKeys;
        [DataMember] public float speechBubbleVisibleSeconds = 2.5f;
        [DataMember] public float speechBubbleIntervalSeconds = 7f;
        [DataMember] public float speechBubbleMaxDistance = 14f;
        [DataMember] public StreetQuestVector3Data speechBubbleLocalOffset;
        [DataMember] public string professionKey;
        [DataMember] public string defaultAppearanceId;
        [DataMember] public StreetQuestCharacterScheduleDefinition schedule;
        [DataMember] public string buildingAddress;
        [DataMember] public string gender;
        [DataMember] public int ageInDays;
        [DataMember] public int appearanceSeed;
        [DataMember] public bool enabled = true;
        [DataMember] public bool interactable = true;
        [DataMember] public bool useFixedSpawnPosition = true;
        [DataMember] public string prefabName;
        [DataMember] public StreetQuestVector3Data position;
        [DataMember] public StreetQuestVector3Data forward;
        [DataMember] public StreetQuestVector3Data[] walkAwayWaypoints;
        [DataMember] public float walkAwaySpeed = 1.4f;
        [DataMember] public bool isRunning;
        [DataMember] public string[] walkAwayStartedStoryFlags;
        [DataMember] public string[] walkAwayCompletedStoryFlags;
        [DataMember] public bool despawnAfterWalkAway;
        [DataMember] public StreetQuestVector3Data[] walkInWaypoints;
        [DataMember] public float walkInSpeed = 6f;
        [DataMember] public int walkInArrivalHour = 8;
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
        [DataMember] public string[] hiddenChildObjectNames;
        [DataMember] public StreetQuestCharacterAppearanceDefinition[] appearances;
        [DataMember] public StreetQuestCharacterAppearanceFlagMapping[] appearanceFlagMappings;
        [DataMember] public StreetQuestCharacterStateDefinition[] states;
        [DataMember] public StreetQuestCharacterDefinition[] alternateActors;

        public bool HasPrefabName => !string.IsNullOrWhiteSpace(prefabName);

        public Vector3 PositionOr(Vector3 fallback) => position != null ? position.ToVector3() : fallback;
        public Vector3 ForwardOr(Vector3 fallback) => forward != null ? forward.ToVector3() : fallback;
        public Vector3 SpeechBubbleLocalOffsetOr(Vector3 fallback) => speechBubbleLocalOffset != null ? speechBubbleLocalOffset.ToVector3() : fallback;
        public Vector3[] WalkAwayWaypointsOrEmpty() => walkAwayWaypoints == null
            ? Array.Empty<Vector3>()
            : walkAwayWaypoints.Where(value => value != null).Select(value => value.ToVector3()).ToArray();
        public Vector3[] WalkInWaypointsOrEmpty() => walkInWaypoints == null
            ? Array.Empty<Vector3>()
            : walkInWaypoints.Where(value => value != null).Select(value => value.ToVector3()).ToArray();
        public Vector3 LocalPositionOr(Vector3 fallback) => localPosition != null ? localPosition.ToVector3() : fallback;
        public Vector3 LocalEulerAnglesOr(Vector3 fallback) => localEulerAngles != null ? localEulerAngles.ToVector3() : fallback;
        public Vector3 LocalScaleOr(Vector3 fallback) => localScale != null ? localScale.ToVector3() : fallback;
        public Vector3 NavTargetLocalOffsetOr(Vector3 fallback) => navTargetLocalOffset != null ? navTargetLocalOffset.ToVector3() : fallback;
        public Vector3 SellerPositionLocalOffsetOr(Vector3 fallback) => sellerPositionLocalOffset != null ? sellerPositionLocalOffset.ToVector3() : fallback;
        public Vector3 ColliderCenterWithPrefabOr(Vector3 fallback) => colliderCenterWithPrefab != null ? colliderCenterWithPrefab.ToVector3() : fallback;
        public Vector3 ColliderSizeWithPrefabOr(Vector3 fallback) => colliderSizeWithPrefab != null ? colliderSizeWithPrefab.ToVector3() : fallback;
        public Vector3 ColliderCenterFallbackOr(Vector3 fallback) => colliderCenterFallback != null ? colliderCenterFallback.ToVector3() : fallback;
        public Vector3 ColliderSizeFallbackOr(Vector3 fallback) => colliderSizeFallback != null ? colliderSizeFallback.ToVector3() : fallback;
        public Vector3 InteractionRendererLocalPositionOr(Vector3 fallback) => interactionRendererLocalPosition != null ? interactionRendererLocalPosition.ToVector3() : fallback;
        public Vector3 InteractionRendererLocalScaleOr(Vector3 fallback) => interactionRendererLocalScale != null ? interactionRendererLocalScale.ToVector3() : fallback;

        public StreetQuestCharacterAppearanceDefinition FindAppearance(string appearanceId)
        {
            if (appearances == null || appearances.Length == 0 || string.IsNullOrWhiteSpace(appearanceId))
                return null;

            return appearances.FirstOrDefault(value =>
                value != null &&
                string.Equals(value.id, appearanceId, StringComparison.OrdinalIgnoreCase));
        }

        public StreetQuestCharacterDefinition ShallowCopy()
        {
            return (StreetQuestCharacterDefinition)MemberwiseClone();
        }

        public void FillMissingValuesFrom(StreetQuestCharacterDefinition fallback)
        {
            if (fallback == null)
                return;

            if (string.IsNullOrWhiteSpace(id)) id = fallback.id;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = fallback.displayName;
            if (string.IsNullOrWhiteSpace(nameKey)) nameKey = fallback.nameKey;
            if (string.IsNullOrWhiteSpace(contactId)) contactId = fallback.contactId;
            if (string.IsNullOrWhiteSpace(contactDescriptionKey)) contactDescriptionKey = fallback.contactDescriptionKey;
            if (string.IsNullOrWhiteSpace(contactCategory)) contactCategory = fallback.contactCategory;
            if (string.IsNullOrWhiteSpace(dialogTypeKey)) dialogTypeKey = fallback.dialogTypeKey;
            if (string.IsNullOrWhiteSpace(gameObjectName)) gameObjectName = fallback.gameObjectName;
            if (string.IsNullOrWhiteSpace(visualObjectName)) visualObjectName = fallback.visualObjectName;
            if (string.IsNullOrWhiteSpace(overlayHeaderKey)) overlayHeaderKey = fallback.overlayHeaderKey;
            if (string.IsNullOrWhiteSpace(ctaKey)) ctaKey = fallback.ctaKey;
            showSpeechBubble = showSpeechBubble || fallback.showSpeechBubble;
            if (string.IsNullOrWhiteSpace(speechBubbleEmojiName)) speechBubbleEmojiName = fallback.speechBubbleEmojiName;
            if (string.IsNullOrWhiteSpace(speechBubbleColor)) speechBubbleColor = fallback.speechBubbleColor;
            if (string.IsNullOrWhiteSpace(speechBubbleTextKey)) speechBubbleTextKey = fallback.speechBubbleTextKey;
            speechBubbleTextKeys ??= fallback.speechBubbleTextKeys;
            if (speechBubbleVisibleSeconds <= 0f) speechBubbleVisibleSeconds = fallback.speechBubbleVisibleSeconds;
            if (speechBubbleIntervalSeconds <= 0f) speechBubbleIntervalSeconds = fallback.speechBubbleIntervalSeconds;
            if (speechBubbleMaxDistance <= 0f) speechBubbleMaxDistance = fallback.speechBubbleMaxDistance;
            speechBubbleLocalOffset ??= fallback.speechBubbleLocalOffset;
            if (string.IsNullOrWhiteSpace(professionKey)) professionKey = fallback.professionKey;
            if (string.IsNullOrWhiteSpace(defaultAppearanceId)) defaultAppearanceId = fallback.defaultAppearanceId;
            schedule ??= fallback.schedule;
            if (string.IsNullOrWhiteSpace(buildingAddress)) buildingAddress = fallback.buildingAddress;
            if (string.IsNullOrWhiteSpace(gender)) gender = fallback.gender;
            if (ageInDays <= 0) ageInDays = fallback.ageInDays;
            if (appearanceSeed == 0) appearanceSeed = fallback.appearanceSeed;
            if (string.IsNullOrWhiteSpace(prefabName)) prefabName = fallback.prefabName;
            position ??= fallback.position;
            forward ??= fallback.forward;
            walkAwayWaypoints ??= fallback.walkAwayWaypoints;
            if (walkAwaySpeed <= 0f) walkAwaySpeed = fallback.walkAwaySpeed;
            isRunning = isRunning || fallback.isRunning;
            walkAwayStartedStoryFlags ??= fallback.walkAwayStartedStoryFlags;
            walkAwayCompletedStoryFlags ??= fallback.walkAwayCompletedStoryFlags;
            despawnAfterWalkAway = despawnAfterWalkAway || fallback.despawnAfterWalkAway;
            walkInWaypoints ??= fallback.walkInWaypoints;
            if (walkInSpeed <= 0f) walkInSpeed = fallback.walkInSpeed;
            if (walkInArrivalHour <= 0 && fallback.walkInArrivalHour > 0) walkInArrivalHour = fallback.walkInArrivalHour;
            if (walkInArrivalMinute <= 0 && fallback.walkInArrivalMinute > 0) walkInArrivalMinute = fallback.walkInArrivalMinute;
            localPosition ??= fallback.localPosition;
            localEulerAngles ??= fallback.localEulerAngles;
            localScale ??= fallback.localScale;
            navTargetLocalOffset ??= fallback.navTargetLocalOffset;
            sellerPositionLocalOffset ??= fallback.sellerPositionLocalOffset;
            colliderCenterWithPrefab ??= fallback.colliderCenterWithPrefab;
            colliderSizeWithPrefab ??= fallback.colliderSizeWithPrefab;
            colliderCenterFallback ??= fallback.colliderCenterFallback;
            colliderSizeFallback ??= fallback.colliderSizeFallback;
            interactionRendererLocalPosition ??= fallback.interactionRendererLocalPosition;
            interactionRendererLocalScale ??= fallback.interactionRendererLocalScale;
            hiddenChildObjectNames ??= fallback.hiddenChildObjectNames;
            appearances ??= fallback.appearances;
            appearanceFlagMappings ??= fallback.appearanceFlagMappings;
            states ??= fallback.states;
        }
    }
}
