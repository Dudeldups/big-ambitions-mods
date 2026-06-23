using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestMapMarkerWatcher
    {
        private static readonly Dictionary<string, string> LastMapWorldResolutionReasonsByCharacterId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                if (TryResolveCharacterDefinitionMapWorldPosition(characterId, runtimeDefinition, out worldPosition))
                    return true;
            }

            if (!definition.enabled)
                return false;

            return TryResolveCharacterDefinitionMapWorldPosition(characterId, definition, out worldPosition);
        }

        private static bool TryResolveCharacterDefinitionMapWorldPosition(
            string characterId,
            StreetQuestCharacterDefinition definition,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (definition == null)
                return false;

            if (!string.IsNullOrWhiteSpace(definition.buildingAddress) &&
                StreetQuestShared.TryResolveAddressWorldAnchor(definition.buildingAddress, out worldPosition, out var source))
            {
                LogMapWorldResolution(characterId, $"mode=buildingAddress stateAddress={definition.buildingAddress} source={source} world={FormatVector3(worldPosition)}");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(definition.buildingAddress))
                LogMapWorldResolution(characterId, $"mode=buildingAddress stateAddress={definition.buildingAddress} source=<failed>");

            worldPosition = definition.PositionOr(Vector3.zero);
            var usedFallback = definition.position != null;
            LogMapWorldResolution(
                characterId,
                usedFallback
                    ? $"mode=fallbackPosition world={FormatVector3(worldPosition)}"
                    : "mode=fallbackPosition source=<missing_position>");
            return usedFallback;
        }

        private static void LogMapWorldResolution(string characterId, string reason)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(reason))
                return;

            if (LastMapWorldResolutionReasonsByCharacterId.TryGetValue(characterId, out var previousReason) &&
                string.Equals(previousReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            LastMapWorldResolutionReasonsByCharacterId[characterId] = reason;
            StreetQuestShared.LogDebug($"MapMarkerWorldResolve characterId={characterId} {reason}");
        }
    }
}
