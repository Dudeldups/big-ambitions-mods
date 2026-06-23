using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestMapMarkerWatcher
    {
        private static bool TryGetCharacterMapWorldPosition(string characterId, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var definition = StreetQuestCharacterCatalog.Get(characterId);
            if (definition == null)
                return false;

            var runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (runtimeDefinition != null)
            {
                if (!runtimeDefinition.enabled)
                    return false;

                if (TryResolveCharacterDefinitionMapWorldPosition(runtimeDefinition, out worldPosition))
                    return true;
            }

            if (!definition.enabled)
                return false;

            return TryResolveCharacterDefinitionMapWorldPosition(definition, out worldPosition);
        }

        private static bool TryResolveCharacterDefinitionMapWorldPosition(
            StreetQuestCharacterDefinition definition,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (definition == null)
                return false;

            if (!string.IsNullOrWhiteSpace(definition.buildingAddress) &&
                StreetQuestShared.TryResolveAddressWorldAnchor(definition.buildingAddress, out worldPosition))
            {
                return true;
            }

            worldPosition = definition.PositionOr(Vector3.zero);
            return definition.position != null;
        }
    }
}
