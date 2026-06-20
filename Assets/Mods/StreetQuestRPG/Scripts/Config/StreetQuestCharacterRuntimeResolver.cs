using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static class StreetQuestCharacterRuntimeResolver
    {
        private static readonly Dictionary<string, RuntimeCacheEntry> RuntimeCacheByCharacterId =
            new Dictionary<string, RuntimeCacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            RuntimeCacheByCharacterId.Clear();
        }

        public static StreetQuestCharacterDefinition ResolveRuntimeDefinition(StreetQuestCharacterDefinition definition)
        {
            if (definition == null)
                return null;

            var stateVersion = StreetQuestShared.GetQuestStateVersion();
            var hourKey = StreetQuestShared.TryGetCurrentGameHourKey(out var resolvedHourKey)
                ? resolvedHourKey
                : int.MinValue;
            if (!string.IsNullOrWhiteSpace(definition.id) &&
                RuntimeCacheByCharacterId.TryGetValue(definition.id, out var cachedEntry) &&
                cachedEntry != null &&
                cachedEntry.StateVersion == stateVersion &&
                cachedEntry.HourKey == hourKey &&
                cachedEntry.RuntimeDefinition != null)
            {
                return CloneDefinition(cachedEntry.RuntimeDefinition);
            }

            var resolved = CloneDefinition(definition);
            var activeState = ResolveActiveState(definition);
            if (activeState != null)
                ApplyStateOverrides(resolved, activeState);

            var activeAppearanceId = ResolveActiveAppearanceId(definition, activeState);
            var activeAppearance = definition.FindAppearance(activeAppearanceId);
            if (activeAppearance != null)
                ApplyAppearanceOverrides(resolved, activeAppearance);

            if (!StreetQuestShared.IsScheduleActive(resolved))
                resolved.enabled = false;

            if (!string.IsNullOrWhiteSpace(definition.id))
            {
                RuntimeCacheByCharacterId[definition.id] = new RuntimeCacheEntry
                {
                    StateVersion = stateVersion,
                    HourKey = hourKey,
                    RuntimeDefinition = CloneDefinition(resolved)
                };
            }

            return resolved;
        }

        public static StreetQuestCharacterDefinition ResolveRuntimeDefinitionWithoutScheduleGate(StreetQuestCharacterDefinition definition)
        {
            if (definition == null)
                return null;

            var resolved = CloneDefinition(definition);
            var activeState = ResolveActiveState(definition);
            if (activeState != null)
                ApplyStateOverrides(resolved, activeState);

            var activeAppearanceId = ResolveActiveAppearanceId(definition, activeState);
            var activeAppearance = definition.FindAppearance(activeAppearanceId);
            if (activeAppearance != null)
                ApplyAppearanceOverrides(resolved, activeAppearance);

            return resolved;
        }

        public static StreetQuestCharacterStateDefinition ResolveActiveState(StreetQuestCharacterDefinition definition)
        {
            if (definition?.states == null || definition.states.Length == 0)
                return null;

            var record = StreetQuestShared.GetQuestStateSnapshot();
            foreach (var state in definition.states.Where(value => value != null))
            {
                if (StateMatches(state, record, definition))
                    return state;
            }

            return null;
        }

        public static string ResolveActiveAppearanceId(
            StreetQuestCharacterDefinition character,
            StreetQuestCharacterStateDefinition activeState = null)
        {
            if (character == null)
                return null;

            if (!string.IsNullOrWhiteSpace(activeState?.appearanceId))
                return activeState.appearanceId;

            if (character.appearanceFlagMappings != null)
            {
                foreach (var mapping in character.appearanceFlagMappings)
                {
                    if (mapping == null ||
                        string.IsNullOrWhiteSpace(mapping.storyFlagId) ||
                        string.IsNullOrWhiteSpace(mapping.appearanceId))
                    {
                        continue;
                    }

                    if (StreetQuestShared.HasStoryFlag(mapping.storyFlagId))
                        return mapping.appearanceId;
                }
            }

            return character.defaultAppearanceId;
        }

        public static string BuildRuntimeStateSignature(StreetQuestCharacterDefinition definition)
        {
            var runtime = ResolveRuntimeDefinition(definition);
            if (runtime == null)
                return string.Empty;

            return string.Join("|", new[]
            {
                runtime.enabled.ToString(),
                runtime.useFixedSpawnPosition.ToString(),
                runtime.defaultAppearanceId ?? string.Empty,
                runtime.gender ?? string.Empty,
                runtime.ageInDays.ToString(),
                runtime.appearanceSeed.ToString(),
                SerializeSchedule(runtime.schedule),
                SerializeVector(runtime.position),
                SerializeVector(runtime.forward),
                SerializeVector(runtime.localPosition),
                SerializeVector(runtime.localEulerAngles),
                SerializeVector(runtime.localScale),
                SerializeVector(runtime.navTargetLocalOffset),
                SerializeVector(runtime.sellerPositionLocalOffset),
                SerializeVector(runtime.colliderCenterWithPrefab),
                SerializeVector(runtime.colliderSizeWithPrefab),
                SerializeVector(runtime.colliderCenterFallback),
                SerializeVector(runtime.colliderSizeFallback),
                SerializeVector(runtime.interactionRendererLocalPosition),
                SerializeVector(runtime.interactionRendererLocalScale),
                runtime.prefabName ?? string.Empty
            });
        }

        private static bool StateMatches(
            StreetQuestCharacterStateDefinition state,
            StreetQuestQuestStateRecord record,
            StreetQuestCharacterDefinition character)
        {
            if (state == null)
                return false;

            if (state.requireScheduleMatch)
            {
                var isScheduleActive = StreetQuestShared.IsScheduleActive(character?.schedule, character, true);
                if (isScheduleActive != state.requiredScheduleActive)
                    return false;
            }

            if (state.requiredStoryFlags != null &&
                state.requiredStoryFlags.Any(flagId =>
                    !string.IsNullOrWhiteSpace(flagId) &&
                    !StreetQuestShared.HasStoryFlag(flagId)))
            {
                return false;
            }

            if (state.forbiddenStoryFlags != null &&
                state.forbiddenStoryFlags.Any(flagId =>
                    !string.IsNullOrWhiteSpace(flagId) &&
                    StreetQuestShared.HasStoryFlag(flagId)))
            {
                return false;
            }

            if (record != null)
            {
                if (state.requiredCompletedQuestIds != null &&
                    state.requiredCompletedQuestIds.Any(questId =>
                        !string.IsNullOrWhiteSpace(questId) &&
                        !record.CompletedQuestIds.Contains(questId)))
                {
                    return false;
                }

                if (state.forbiddenCompletedQuestIds != null &&
                    state.forbiddenCompletedQuestIds.Any(questId =>
                        !string.IsNullOrWhiteSpace(questId) &&
                        record.CompletedQuestIds.Contains(questId)))
                {
                    return false;
                }

                if (state.requiredFavors != null &&
                    state.requiredFavors.Any(requirement =>
                        requirement != null &&
                        (record.GetFavor(requirement.CharacterId) < requirement.MinValue ||
                         record.GetFavor(requirement.CharacterId) > requirement.MaxValue)))
                {
                    return false;
                }
            }
            else if (state.requiredCompletedQuestIds?.Length > 0 || state.requiredFavors?.Length > 0)
            {
                return false;
            }

            return true;
        }

        private static void ApplyStateOverrides(
            StreetQuestCharacterDefinition resolved,
            StreetQuestCharacterStateDefinition state)
        {
            if (resolved == null || state == null)
                return;

            if (state.overrideEnabled)
                resolved.enabled = state.enabled;
            if (state.overrideUseFixedSpawnPosition)
                resolved.useFixedSpawnPosition = state.useFixedSpawnPosition;
            if (!string.IsNullOrWhiteSpace(state.appearanceId))
                resolved.defaultAppearanceId = state.appearanceId;
            if (state.schedule != null)
                resolved.schedule = state.schedule;
            if (state.position != null)
                resolved.position = state.position;
            if (state.forward != null)
                resolved.forward = state.forward;
            if (state.localPosition != null)
                resolved.localPosition = state.localPosition;
            if (state.localEulerAngles != null)
                resolved.localEulerAngles = state.localEulerAngles;
            if (state.localScale != null)
                resolved.localScale = state.localScale;
            if (state.navTargetLocalOffset != null)
                resolved.navTargetLocalOffset = state.navTargetLocalOffset;
            if (state.sellerPositionLocalOffset != null)
                resolved.sellerPositionLocalOffset = state.sellerPositionLocalOffset;
            if (state.colliderCenterWithPrefab != null)
                resolved.colliderCenterWithPrefab = state.colliderCenterWithPrefab;
            if (state.colliderSizeWithPrefab != null)
                resolved.colliderSizeWithPrefab = state.colliderSizeWithPrefab;
            if (state.colliderCenterFallback != null)
                resolved.colliderCenterFallback = state.colliderCenterFallback;
            if (state.colliderSizeFallback != null)
                resolved.colliderSizeFallback = state.colliderSizeFallback;
            if (state.interactionRendererLocalPosition != null)
                resolved.interactionRendererLocalPosition = state.interactionRendererLocalPosition;
            if (state.interactionRendererLocalScale != null)
                resolved.interactionRendererLocalScale = state.interactionRendererLocalScale;
        }

        private static void ApplyAppearanceOverrides(
            StreetQuestCharacterDefinition resolved,
            StreetQuestCharacterAppearanceDefinition activeAppearance)
        {
            if (resolved == null || activeAppearance == null)
                return;

            resolved.defaultAppearanceId = string.IsNullOrWhiteSpace(activeAppearance.id)
                ? resolved.defaultAppearanceId
                : activeAppearance.id;
            if (!string.IsNullOrWhiteSpace(activeAppearance.visualObjectName))
                resolved.visualObjectName = activeAppearance.visualObjectName;
            if (!string.IsNullOrWhiteSpace(activeAppearance.gender))
                resolved.gender = activeAppearance.gender;
            if (activeAppearance.ageInDays > 0)
                resolved.ageInDays = activeAppearance.ageInDays;
            if (activeAppearance.appearanceSeed != 0)
                resolved.appearanceSeed = activeAppearance.appearanceSeed;
            if (!string.IsNullOrWhiteSpace(activeAppearance.prefabName))
                resolved.prefabName = activeAppearance.prefabName;
            if (activeAppearance.localPosition != null)
                resolved.localPosition = activeAppearance.localPosition;
            if (activeAppearance.localEulerAngles != null)
                resolved.localEulerAngles = activeAppearance.localEulerAngles;
            if (activeAppearance.localScale != null)
                resolved.localScale = activeAppearance.localScale;
            if (activeAppearance.colliderCenterWithPrefab != null)
                resolved.colliderCenterWithPrefab = activeAppearance.colliderCenterWithPrefab;
            if (activeAppearance.colliderSizeWithPrefab != null)
                resolved.colliderSizeWithPrefab = activeAppearance.colliderSizeWithPrefab;
            if (activeAppearance.colliderCenterFallback != null)
                resolved.colliderCenterFallback = activeAppearance.colliderCenterFallback;
            if (activeAppearance.colliderSizeFallback != null)
                resolved.colliderSizeFallback = activeAppearance.colliderSizeFallback;
            if (activeAppearance.interactionRendererLocalPosition != null)
                resolved.interactionRendererLocalPosition = activeAppearance.interactionRendererLocalPosition;
            if (activeAppearance.interactionRendererLocalScale != null)
                resolved.interactionRendererLocalScale = activeAppearance.interactionRendererLocalScale;
            if (activeAppearance.hiddenChildObjectNames != null && activeAppearance.hiddenChildObjectNames.Length > 0)
                resolved.hiddenChildObjectNames = activeAppearance.hiddenChildObjectNames;
        }

        private static StreetQuestCharacterDefinition CloneDefinition(StreetQuestCharacterDefinition definition)
        {
            return new StreetQuestCharacterDefinition
            {
                id = definition.id,
                displayName = definition.displayName,
                nameKey = definition.nameKey,
                contactId = definition.contactId,
                dialogTypeKey = definition.dialogTypeKey,
                gameObjectName = definition.gameObjectName,
                visualObjectName = definition.visualObjectName,
                overlayHeaderKey = definition.overlayHeaderKey,
                ctaKey = definition.ctaKey,
                professionKey = definition.professionKey,
                defaultAppearanceId = definition.defaultAppearanceId,
                schedule = definition.schedule,
                gender = definition.gender,
                ageInDays = definition.ageInDays,
                appearanceSeed = definition.appearanceSeed,
                enabled = definition.enabled,
                interactable = definition.interactable,
                useFixedSpawnPosition = definition.useFixedSpawnPosition,
                prefabName = definition.prefabName,
                position = definition.position,
                forward = definition.forward,
                localPosition = definition.localPosition,
                localEulerAngles = definition.localEulerAngles,
                localScale = definition.localScale,
                navTargetLocalOffset = definition.navTargetLocalOffset,
                sellerPositionLocalOffset = definition.sellerPositionLocalOffset,
                colliderCenterWithPrefab = definition.colliderCenterWithPrefab,
                colliderSizeWithPrefab = definition.colliderSizeWithPrefab,
                colliderCenterFallback = definition.colliderCenterFallback,
                colliderSizeFallback = definition.colliderSizeFallback,
                interactionRendererLocalPosition = definition.interactionRendererLocalPosition,
                interactionRendererLocalScale = definition.interactionRendererLocalScale,
                hiddenChildObjectNames = definition.hiddenChildObjectNames,
                appearances = definition.appearances,
                appearanceFlagMappings = definition.appearanceFlagMappings,
                states = definition.states
            };
        }

        private static string SerializeVector(StreetQuestVector3Data value)
        {
            if (value == null)
                return string.Empty;

            var vector = value.ToVector3();
            return $"{vector.x:F3},{vector.y:F3},{vector.z:F3}";
        }

        private static string SerializeSchedule(StreetQuestCharacterScheduleDefinition schedule)
        {
            if (schedule == null)
                return string.Empty;

            return string.Join("|", new[]
            {
                schedule.mode ?? string.Empty,
                schedule.startHour.ToString(),
                schedule.endHour.ToString(),
                schedule.address ?? string.Empty,
                schedule.nearestBuildingMaxDistance.ToString("F2")
            });
        }

        private sealed class RuntimeCacheEntry
        {
            public int StateVersion;
            public int HourKey;
            public StreetQuestCharacterDefinition RuntimeDefinition;
        }
    }
}
