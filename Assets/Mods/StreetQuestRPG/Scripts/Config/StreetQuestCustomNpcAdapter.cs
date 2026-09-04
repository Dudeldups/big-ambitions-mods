using CustomNPCAPI;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static class StreetQuestCustomNpcAdapter
    {
        internal static CustomNpcDefinition ToApiDefinition(
            StreetQuestCharacterDefinition source,
            Vector3 spawnPosition,
            Vector3 facingForward,
            string rootName)
        {
            if (source == null)
                return null;

            return new CustomNpcDefinition
            {
                Id = source.id,
                DisplayName = source.displayName,
                NameKey = source.nameKey,
                PrefabName = source.prefabName,
                GameObjectName = rootName,
                VisualObjectName = source.visualObjectName,
                OverlayHeaderKey = string.IsNullOrWhiteSpace(source.overlayHeaderKey) ? source.nameKey : source.overlayHeaderKey,
                CtaTextKey = string.IsNullOrWhiteSpace(source.ctaKey) ? "streetquest:cta_talk" : source.ctaKey,
                CtaTextFallback = $"Talk to {{npcname}}",
                Interactable = source.interactable,
                Gender = source.gender,
                AgeInDays = source.ageInDays > 0 ? source.ageInDays : 42 * 365,
                AppearanceSeed = source.appearanceSeed != 0 ? source.appearanceSeed : 104729,
                Position = spawnPosition,
                Forward = facingForward,
                LocalPosition = source.LocalPositionOr(Vector3.zero),
                LocalEulerAngles = source.LocalEulerAnglesOr(Vector3.zero),
                LocalScale = source.LocalScaleOr(Vector3.one),
                NavTargetLocalOffset = source.NavTargetLocalOffsetOr(new Vector3(0f, 0f, 1.25f)),
                SellerPositionLocalOffset = source.SellerPositionLocalOffsetOr(new Vector3(0f, 0f, -0.85f)),
                ColliderCenterWithPrefab = source.ColliderCenterWithPrefabOr(new Vector3(0f, 1.05f, -0.05f)),
                ColliderSizeWithPrefab = source.ColliderSizeWithPrefabOr(new Vector3(1.3f, 2.1f, 0.55f)),
                ColliderCenterFallback = source.ColliderCenterFallbackOr(new Vector3(0f, 0.95f, 0f)),
                ColliderSizeFallback = source.ColliderSizeFallbackOr(new Vector3(1.8f, 1.9f, 1.2f)),
                InteractionRendererLocalPosition = source.InteractionRendererLocalPositionOr(new Vector3(0f, 0.9f, 0f)),
                InteractionRendererLocalScale = source.InteractionRendererLocalScaleOr(new Vector3(0.08f, 0.08f, 0.08f)),
                HiddenChildObjectNames = source.hiddenChildObjectNames ?? System.Array.Empty<string>()
            };
        }

        internal static CustomNpcSpawnOptions BuildSpawnOptions(StreetQuestCharacterDefinition source, bool visible)
        {
            var options = new CustomNpcSpawnOptions
            {
                Visible = visible,
                OnInteract = _ =>
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.dialogTypeKey))
                        return;

                    CustomNpcApi.OpenDialog(source.dialogTypeKey);
                }
            };

            if (source != null && StreetQuestAssetBundleService.IsBundledStreetQuestAssetPath(source.prefabName))
            {
                options.VisualFactory = parent =>
                {
                    return StreetQuestAssetBundleService.TrySpawnPrefab(source.prefabName, parent, out var visual)
                        ? visual
                        : null;
                };
            }

            return options;
        }
    }
}
