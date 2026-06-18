using System;
using System.Linq;
using UnityEngine;

namespace StreetQuestRPG
{
    [Serializable]
    internal sealed class StreetQuestCharacterDefinition
    {
        public string id;
        public string displayName;
        public string nameKey;
        public string contactId;
        public string dialogTypeKey;
        public string gameObjectName;
        public string visualObjectName;
        public string overlayHeaderKey;
        public string ctaKey;
        public string fallbackLabel;
        public string defaultAppearanceId;
        public string gender;
        public int ageInDays;
        public int appearanceSeed;
        public bool enabled = true;
        public bool useFixedSpawnPosition = true;
        public string[] prefabNames;
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
        public StreetQuestCharacterAppearanceDefinition[] appearances;
        public StreetQuestCharacterAppearanceFlagMapping[] appearanceFlagMappings;
        public StreetQuestCharacterStateDefinition[] states;
        public string introStageOneTextKey;
        public string introStageOneConfirmTextKey;
        public string introStageOneCompletedFlagId;
        public string introStageTwoTextKey;
        public string introStageTwoConfirmTextKey;
        public string introStageTwoCompletedFlagId;

        public bool HasPrefabNames => prefabNames != null && prefabNames.Any(value => !string.IsNullOrWhiteSpace(value));

        public Vector3 PositionOr(Vector3 fallback) => position != null ? position.ToVector3() : fallback;
        public Vector3 ForwardOr(Vector3 fallback) => forward != null ? forward.ToVector3() : fallback;
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

        public void FillMissingValuesFrom(StreetQuestCharacterDefinition fallback)
        {
            if (fallback == null)
                return;

            if (string.IsNullOrWhiteSpace(id)) id = fallback.id;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = fallback.displayName;
            if (string.IsNullOrWhiteSpace(nameKey)) nameKey = fallback.nameKey;
            if (string.IsNullOrWhiteSpace(contactId)) contactId = fallback.contactId;
            if (string.IsNullOrWhiteSpace(dialogTypeKey)) dialogTypeKey = fallback.dialogTypeKey;
            if (string.IsNullOrWhiteSpace(gameObjectName)) gameObjectName = fallback.gameObjectName;
            if (string.IsNullOrWhiteSpace(visualObjectName)) visualObjectName = fallback.visualObjectName;
            if (string.IsNullOrWhiteSpace(overlayHeaderKey)) overlayHeaderKey = fallback.overlayHeaderKey;
            if (string.IsNullOrWhiteSpace(ctaKey)) ctaKey = fallback.ctaKey;
            if (string.IsNullOrWhiteSpace(fallbackLabel)) fallbackLabel = fallback.fallbackLabel;
            if (string.IsNullOrWhiteSpace(defaultAppearanceId)) defaultAppearanceId = fallback.defaultAppearanceId;
            if (string.IsNullOrWhiteSpace(gender)) gender = fallback.gender;
            if (ageInDays <= 0) ageInDays = fallback.ageInDays;
            if (appearanceSeed == 0) appearanceSeed = fallback.appearanceSeed;
            if (prefabNames == null || prefabNames.Length == 0) prefabNames = fallback.prefabNames;
            position ??= fallback.position;
            forward ??= fallback.forward;
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
            appearances ??= fallback.appearances;
            appearanceFlagMappings ??= fallback.appearanceFlagMappings;
            states ??= fallback.states;
        }
    }
}
