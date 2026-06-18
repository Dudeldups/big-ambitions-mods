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
        [DataMember] public string dialogTypeKey;
        [DataMember] public string gameObjectName;
        [DataMember] public string visualObjectName;
        [DataMember] public string overlayHeaderKey;
        [DataMember] public string ctaKey;
        [DataMember] public string professionKey;
        [DataMember] public string defaultAppearanceId;
        [DataMember] public string gender;
        [DataMember] public int ageInDays;
        [DataMember] public int appearanceSeed;
        [DataMember] public bool enabled = true;
        [DataMember] public bool useFixedSpawnPosition = true;
        [DataMember] public string prefabName;
        [DataMember] public StreetQuestVector3Data position;
        [DataMember] public StreetQuestVector3Data forward;
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
        [DataMember] public StreetQuestCharacterAppearanceDefinition[] appearances;
        [DataMember] public StreetQuestCharacterAppearanceFlagMapping[] appearanceFlagMappings;
        [DataMember] public StreetQuestCharacterStateDefinition[] states;

        public bool HasPrefabName => !string.IsNullOrWhiteSpace(prefabName);

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
            if (string.IsNullOrWhiteSpace(professionKey)) professionKey = fallback.professionKey;
            if (string.IsNullOrWhiteSpace(defaultAppearanceId)) defaultAppearanceId = fallback.defaultAppearanceId;
            if (string.IsNullOrWhiteSpace(gender)) gender = fallback.gender;
            if (ageInDays <= 0) ageInDays = fallback.ageInDays;
            if (appearanceSeed == 0) appearanceSeed = fallback.appearanceSeed;
            if (string.IsNullOrWhiteSpace(prefabName)) prefabName = fallback.prefabName;
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
