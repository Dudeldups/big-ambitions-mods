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
                if (runtimeDefinition.enabled &&
                    TryResolveCharacterDefinitionMapWorldPosition(characterId, runtimeDefinition, out worldPosition))
                    return true;
            }

            var mapDefinition = TryBuildMapDefinition(definition);
            if (mapDefinition != null &&
                TryResolveCharacterDefinitionMapWorldPosition(characterId, mapDefinition, out worldPosition))
            {
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

            if (!string.IsNullOrWhiteSpace(definition.buildingAddress))
            {
                if (StreetQuestShared.TryResolveAddressWorldAnchor(definition.buildingAddress, out worldPosition, out var source))
                {
                    LogMapWorldResolution(characterId, $"mode=buildingAddress stateAddress={definition.buildingAddress} source={source} world={FormatVector3(worldPosition)}");
                    return true;
                }

                LogMapWorldResolution(characterId, $"mode=buildingAddress stateAddress={definition.buildingAddress} source=<failed>");
                return false;
            }

            worldPosition = definition.PositionOr(Vector3.zero);
            var usedFallback = definition.position != null;
            LogMapWorldResolution(
                characterId,
                usedFallback
                    ? $"mode=fallbackPosition world={FormatVector3(worldPosition)}"
                    : "mode=fallbackPosition source=<missing_position>");
            return usedFallback;
        }

        private static StreetQuestCharacterDefinition TryBuildMapDefinition(StreetQuestCharacterDefinition definition)
        {
            if (definition == null || !StreetQuestCharacterRuntimeResolver.HasConfiguredStates(definition))
                return null;

            var activeState = StreetQuestCharacterRuntimeResolver.ResolveActiveState(definition);
            if (activeState == null)
                return null;

            var resolved = definition.ShallowCopy();
            resolved.FillMissingValuesFrom(definition);

            if (!string.IsNullOrWhiteSpace(activeState.buildingAddress))
                resolved.buildingAddress = activeState.buildingAddress;
            if (activeState.position != null)
                resolved.position = activeState.position;
            if (activeState.schedule != null)
                resolved.schedule = activeState.schedule;

            var appearanceId = StreetQuestCharacterRuntimeResolver.ResolveActiveAppearanceId(definition, activeState);
            var appearance = definition.FindAppearance(appearanceId);
            if (appearance != null && !string.IsNullOrWhiteSpace(appearance.prefabName))
                resolved.prefabName = appearance.prefabName;

            return resolved;
        }

        private static void LogMapWorldResolution(string characterId, string reason)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(reason))
                return;

            LastMapWorldResolutionReasonsByCharacterId[characterId] = reason;
        }
    }
}
