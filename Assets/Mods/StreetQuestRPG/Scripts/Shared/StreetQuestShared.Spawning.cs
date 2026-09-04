using System;
using System.Linq;
using CustomNPCAPI;
using Helpers;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        public static bool EnsureSpawnedOutdoorQuestGiver()
        {
            var stopwatch = Stopwatch.StartNew();
            LogSpawnLifecycle("EnsureSpawnedOutdoorQuestGiver start");
            var hadConfiguredCharacters = false;
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null))
            {
                hadConfiguredCharacters = true;
                EnsureSpawnedCharacter(character);
            }

            PrewarmScheduledCharacterPool();
            stopwatch.Stop();
            LogSpawnLifecycle($"EnsureSpawnedOutdoorQuestGiver end durationMs={stopwatch.ElapsedMilliseconds} configured={hadConfiguredCharacters} spawnedCount={SpawnedCharacterRoots.Count}");
            return hadConfiguredCharacters;
        }

        public static void RefreshSpawnedCharacters()
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null))
                RefreshSpawnedCharacter(character);

            stopwatch.Stop();
            LogSpawnLifecycle($"RefreshSpawnedCharacters end durationMs={stopwatch.ElapsedMilliseconds} spawnedCount={SpawnedCharacterRoots.Count}");
        }

        internal static void ResetSpawnRuntimeState()
        {
            DestroySpawnedOutdoorQuestGiver();
            SpawnedCharacterRoots.Clear();
            SpawnedCharacterHandles.Clear();
            SpawnedCharacterStateSignatures.Clear();
            PreferredQuestGiverSpawnPosition = null;
            LogSpawnLifecycle("ResetSpawnRuntimeState completed");
        }

        internal static bool TryGetSpawnedCharacterRoot(string characterId, out GameObject root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            if (!SpawnedCharacterRoots.TryGetValue(characterId, out var existingRoot) || existingRoot == null)
                return false;

            root = existingRoot;
            return true;
        }

        private static bool EnsureSpawnedCharacter(
            StreetQuestCharacterDefinition character,
            bool ignoreScheduleGate = false,
            bool activateAfterSpawn = true)
        {
            var stopwatch = Stopwatch.StartNew();
            if (character == null || string.IsNullOrWhiteSpace(character.id))
                return false;

            var runtimeDefinition = ignoreScheduleGate
                ? StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinitionWithoutScheduleGate(character)
                : StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(character);
            return EnsureSpawnedCharacter(character, runtimeDefinition, activateAfterSpawn, stopwatch);
        }

        private static bool RefreshSpawnedCharacter(StreetQuestCharacterDefinition character)
        {
            var stopwatch = Stopwatch.StartNew();
            if (character == null || string.IsNullOrWhiteSpace(character.id))
                return false;

            StreetQuestCharacterDefinition runtimeDefinition;
            bool activateAfterSpawn;

            if (StreetQuestCharacterRuntimeResolver.HasAnySchedule(character))
            {
                runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinitionWithoutScheduleGate(character);
                activateAfterSpawn = runtimeDefinition != null && runtimeDefinition.enabled && IsScheduleActive(runtimeDefinition);
            }
            else
            {
                runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(character);
                activateAfterSpawn = true;
            }

            return EnsureSpawnedCharacter(character, runtimeDefinition, activateAfterSpawn, stopwatch);
        }

        private static bool EnsureSpawnedCharacter(
            StreetQuestCharacterDefinition character,
            StreetQuestCharacterDefinition runtimeDefinition,
            bool activateAfterSpawn,
            Stopwatch stopwatch)
        {
            if (runtimeDefinition == null)
            {
                if (PreserveTransientSpawnedCharacter(character?.id))
                {
                    stopwatch.Stop();
                    LogSpawnLifecycle($"EnsureSpawnedCharacter preserve character={character?.id ?? "<null>"} durationMs={stopwatch.ElapsedMilliseconds} reason=transient_walker_active");
                    return true;
                }

                stopwatch.Stop();
                DestroySpawnedCharacter(character?.id);
                return false;
            }

            if (!runtimeDefinition.enabled)
            {
                if (PreserveTransientSpawnedCharacter(character.id))
                {
                    stopwatch.Stop();
                    return true;
                }

                stopwatch.Stop();
                DestroySpawnedCharacter(character.id);
                return false;
            }

            var stateSignature = StreetQuestCharacterRuntimeResolver.BuildRuntimeStateSignature(runtimeDefinition, useResolvedDefinition: true);
            if (SpawnedCharacterRoots.TryGetValue(character.id, out var existingRoot) && existingRoot != null)
            {
                var hasExpectedHandle = SpawnedCharacterHandles.TryGetValue(character.id, out var existingHandle) &&
                                        existingHandle != null && !existingHandle.IsDisposed &&
                                        (!runtimeDefinition.interactable || existingHandle.Controller != null);
                if (SpawnedCharacterStateSignatures.TryGetValue(character.id, out var existingSignature) &&
                    string.Equals(existingSignature, stateSignature, StringComparison.Ordinal) && hasExpectedHandle)
                {
                    SetSpawnedCharacterVisibility(character.id, activateAfterSpawn);
                    stopwatch.Stop();
                    return true;
                }

                DestroySpawnedCharacter(character.id);
            }

            try
            {
                var spawnPosition = GetQuestGiverSpawnPosition(runtimeDefinition);
                if (!spawnPosition.HasValue)
                    return false;

                var playerController = PlayerHelper.PlayerController;
                var facingForward = runtimeDefinition.useFixedSpawnPosition
                    ? FlattenDirection(runtimeDefinition.ForwardOr(FixedForward))
                    : playerController != null ? FlattenDirection(playerController.transform.forward) : Vector3.forward;
                if (facingForward.sqrMagnitude < 0.001f)
                    facingForward = Vector3.forward;

                var rootName = string.IsNullOrWhiteSpace(runtimeDefinition.gameObjectName)
                    ? $"{SpawnedQuestGiverName}.{character.id}"
                    : runtimeDefinition.gameObjectName;
                var apiDefinition = StreetQuestCustomNpcAdapter.ToApiDefinition(runtimeDefinition, spawnPosition.Value, facingForward, rootName);
                var options = StreetQuestCustomNpcAdapter.BuildSpawnOptions(runtimeDefinition, activateAfterSpawn);
                var handle = CustomNpcApi.Spawn("StreetQuestRPG", apiDefinition, options);
                if (handle?.Root == null)
                    return false;

                SpawnedCharacterHandles[character.id] = handle;
                SpawnedCharacterRoots[character.id] = handle.Root;
                SpawnedCharacterStateSignatures[character.id] = stateSignature;

                EnsureCharacterSpeechBubble(handle.Root, runtimeDefinition);
                EnsureCharacterWalker(handle.Root, runtimeDefinition);
                SetSpawnedCharacterVisibility(character.id, activateAfterSpawn);

                stopwatch.Stop();
                LogSpawnLifecycle($"EnsureSpawnedCharacter spawnComplete character={character.id} durationMs={stopwatch.ElapsedMilliseconds} activate={activateAfterSpawn} interactable={runtimeDefinition.interactable}");
                return true;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                LogDebug($"EnsureSpawnedCharacter failed character={character.id} durationMs={stopwatch.ElapsedMilliseconds} exception={exception}");
                DestroySpawnedCharacter(character.id);
                return false;
            }
        }

        private static bool ForceRespawnApartmentVisitCharacter(string characterId, string reason)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var character = StreetQuestCharacterCatalog.All.FirstOrDefault(candidate =>
                candidate != null && string.Equals(candidate.id, characterId, StringComparison.OrdinalIgnoreCase));
            if (character == null)
                return false;

            PreferredQuestGiverSpawnPosition = null;
            StreetQuestCharacterRuntimeResolver.ClearCache();
            DestroySpawnedCharacter(characterId);

            var result = RefreshSpawnedCharacter(character);
            var spawned = SpawnedCharacterRoots.TryGetValue(characterId, out var root) && root != null;
            LogDebug($"ForceRespawnApartmentVisitCharacter character={characterId} reason={reason} result={result} spawned={spawned} position={(spawned ? FormatVector3(root.transform.position) : "<none>")} indoorAddress={GetCurrentIndoorBuildingAddressKey() ?? string.Empty}");
            return result;
        }

        private static void DestroySpawnedOutdoorQuestGiver()
        {
            foreach (var characterId in SpawnedCharacterRoots.Keys.ToList())
                DestroySpawnedCharacter(characterId);
        }

        private static void PrewarmScheduledCharacterPool()
        {
            foreach (var character in StreetQuestCharacterCatalog.All)
            {
                if (character == null || string.IsNullOrWhiteSpace(character.id) ||
                    !StreetQuestCharacterRuntimeResolver.HasAnySchedule(character) ||
                    SpawnedCharacterRoots.ContainsKey(character.id))
                    continue;

                var runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinitionWithoutScheduleGate(character);
                if (runtimeDefinition == null || !runtimeDefinition.enabled)
                    continue;

                EnsureSpawnedCharacter(character, ignoreScheduleGate: true, activateAfterSpawn: false);
            }
        }

        private static void SetSpawnedCharacterVisibility(string characterId, bool visible)
        {
            if (string.IsNullOrWhiteSpace(characterId) ||
                !SpawnedCharacterRoots.TryGetValue(characterId, out var root) || root == null ||
                !SpawnedCharacterHandles.TryGetValue(characterId, out var handle) || handle == null)
                return;

            var speechBubble = root.GetComponent<StreetQuestCharacterSpeechBubble>();
            if (speechBubble != null)
                speechBubble.OnVisibilityChanged(visible);

            var walker = root.GetComponent<StreetQuestCharacterWalker>();
            if (walker != null)
            {
                handle.SetInteractionEnabled(visible);
                walker.OnVisibilityChanged(visible);
                return;
            }

            handle.SetVisible(visible);
        }

        private static void EnsureCharacterWalker(GameObject root, StreetQuestCharacterDefinition definition)
        {
            if (root == null || definition == null)
                return;

            if ((definition.walkAwayWaypoints == null || definition.walkAwayWaypoints.Length == 0) &&
                (definition.walkInWaypoints == null || definition.walkInWaypoints.Length == 0))
                return;

            var walker = root.GetComponent<StreetQuestCharacterWalker>();
            if (walker == null)
                walker = root.AddComponent<StreetQuestCharacterWalker>();

            var spawnForward = FlattenDirection(definition.ForwardOr(FixedForward));
            if (spawnForward.sqrMagnitude < 0.001f)
                spawnForward = Vector3.forward;

            walker.Configure(
                definition.id,
                definition.PositionOr(root.transform.position),
                Quaternion.LookRotation(-spawnForward, Vector3.up),
                definition.WalkAwayWaypointsOrEmpty(),
                definition.walkAwaySpeed,
                definition.isRunning,
                definition.walkAwayStartedStoryFlags,
                definition.walkAwayCompletedStoryFlags,
                definition.despawnAfterWalkAway,
                definition.WalkInWaypointsOrEmpty(),
                definition.walkInSpeed,
                definition.walkInArrivalHour,
                definition.walkInArrivalMinute);
        }

        private static void EnsureCharacterSpeechBubble(GameObject root, StreetQuestCharacterDefinition definition)
        {
            if (root == null)
                return;

            var existingBubble = root.GetComponent<StreetQuestCharacterSpeechBubble>();
            if (definition == null || !definition.interactable || !definition.showSpeechBubble)
            {
                if (existingBubble != null)
                    UnityEngine.Object.Destroy(existingBubble);
                return;
            }

            if (existingBubble == null)
                existingBubble = root.AddComponent<StreetQuestCharacterSpeechBubble>();
            existingBubble.Configure(root.transform, definition);
        }

        private static void DestroySpawnedCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (SpawnedCharacterHandles.TryGetValue(characterId, out var handle) && handle != null)
                handle.Dispose();
            else if (SpawnedCharacterRoots.TryGetValue(characterId, out var root) && root != null)
                UnityEngine.Object.Destroy(root);

            SpawnedCharacterHandles.Remove(characterId);
            SpawnedCharacterRoots.Remove(characterId);
            SpawnedCharacterStateSignatures.Remove(characterId);
        }

        private static bool PreserveTransientSpawnedCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;
            if (!SpawnedCharacterRoots.TryGetValue(characterId, out var root) || root == null)
                return false;
            var walker = root.GetComponent<StreetQuestCharacterWalker>();
            return walker != null && walker.IsTransientlyActive;
        }

        private static Vector3? GetQuestGiverSpawnPosition(StreetQuestCharacterDefinition character)
        {
            if (character == null)
                return null;
            if (character.useFixedSpawnPosition)
                return character.PositionOr(FixedSpawnPosition);
            if (PreferredQuestGiverSpawnPosition.HasValue)
                return PreferredQuestGiverSpawnPosition.Value;

            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return null;
            var playerForward = FlattenDirection(playerController.transform.forward);
            if (playerForward.sqrMagnitude < 0.001f)
                playerForward = Vector3.forward;
            var spawnPosition = playerController.transform.position + playerForward.normalized * DefaultSpawnOffsetFromPlayer.z;
            PreferredQuestGiverSpawnPosition = spawnPosition;
            return spawnPosition;
        }

        private static Vector3 FlattenDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.normalized;
        }

        private static string FormatVector3(Vector3 value) => $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
    }
}
