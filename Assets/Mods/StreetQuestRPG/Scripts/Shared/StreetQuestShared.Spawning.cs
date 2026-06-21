using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        public static bool EnsureSpawnedOutdoorQuestGiver()
        {
            var stopwatch = Stopwatch.StartNew();
            LogDebug("EnsureSpawnedOutdoorQuestGiver start");
            RemoveLegacyQuestGiverCtaBehaviors();
            EnsureQuestGiverCtaBehaviorInstalled();
            var hadConfiguredCharacters = false;
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null))
            {
                hadConfiguredCharacters = true;
                EnsureSpawnedCharacter(character);
            }

            PrewarmScheduledCharacterPool();
            stopwatch.Stop();
            LogDebug($"EnsureSpawnedOutdoorQuestGiver end durationMs={stopwatch.ElapsedMilliseconds} configured={hadConfiguredCharacters} spawnedCount={SpawnedCharacterRoots.Count}");

            return hadConfiguredCharacters;
        }


        public static void RefreshSpawnedCharacters()
        {
            var stopwatch = Stopwatch.StartNew();
            if (!QuestGiverCtaInstalled)
                EnsureQuestGiverCtaBehaviorInstalled();

            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null))
                RefreshSpawnedCharacter(character);

            stopwatch.Stop();
            LogDebug($"RefreshSpawnedCharacters end durationMs={stopwatch.ElapsedMilliseconds} spawnedCount={SpawnedCharacterRoots.Count}");
        }


        internal static void ResetSpawnRuntimeState()
        {
            DestroySpawnedOutdoorQuestGiver();
            CharacterIdsByControllerInstanceId.Clear();
            SpawnedCharacterRoots.Clear();
            SpawnedCharacterControllers.Clear();
            SpawnedCharacterStateSignatures.Clear();
            CachedSellerStandControllerType = null;
            CachedItemsContainerTransform = null;
            PreferredQuestGiverSpawnPosition = null;
            QuestGiverCtaInstalled = false;
            LogDebug("ResetSpawnRuntimeState completed");
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
                activateAfterSpawn = runtimeDefinition != null &&
                                     runtimeDefinition.enabled &&
                                     IsScheduleActive(runtimeDefinition);
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
                stopwatch.Stop();
                LogDebug($"EnsureSpawnedCharacter skip character={character?.id ?? "<null>"} durationMs={stopwatch.ElapsedMilliseconds} reason=runtime_definition_null");
                DestroySpawnedCharacter(character.id);
                return false;
            }

            if (!runtimeDefinition.enabled)
            {
                stopwatch.Stop();
                LogDebug($"EnsureSpawnedCharacter skip character={character.id} durationMs={stopwatch.ElapsedMilliseconds} reason=runtime_disabled");
                DestroySpawnedCharacter(character.id);
                return false;
            }

            var stateSignature = StreetQuestCharacterRuntimeResolver.BuildRuntimeStateSignature(
                runtimeDefinition,
                useResolvedDefinition: true);
            if (SpawnedCharacterRoots.TryGetValue(character.id, out var existingRoot) &&
                existingRoot != null)
            {
                var hasExpectedController = !runtimeDefinition.interactable ||
                    (SpawnedCharacterControllers.TryGetValue(character.id, out var existingController) &&
                     existingController != null);
                if (!hasExpectedController)
                {
                    DestroySpawnedCharacter(character.id);
                }

                if (SpawnedCharacterStateSignatures.TryGetValue(character.id, out var existingSignature) &&
                    string.Equals(existingSignature, stateSignature, StringComparison.Ordinal) &&
                    hasExpectedController)
                {
                    SetSpawnedCharacterVisibility(character.id, activateAfterSpawn);
                    stopwatch.Stop();
                    LogDebug($"EnsureSpawnedCharacter reuse character={character.id} durationMs={stopwatch.ElapsedMilliseconds} activate={activateAfterSpawn}");
                    return true;
                }

                DestroySpawnedCharacter(character.id);
            }

            try
            {
                var sellerStandControllerType = ResolveSellerStandControllerType();
                if (sellerStandControllerType == null)
                {
                    LogDebug("EnsureSpawnedCharacter failed: SellerStandController type missing");
                    return false;
                }

                var spawnPosition = GetQuestGiverSpawnPosition(runtimeDefinition);
                if (!spawnPosition.HasValue)
                    return false;

                var playerController = PlayerHelper.PlayerController;
                var facingForward = runtimeDefinition.useFixedSpawnPosition
                    ? FlattenDirection(runtimeDefinition.ForwardOr(FixedForward))
                    : playerController != null
                        ? FlattenDirection(playerController.transform.forward)
                        : Vector3.forward;
                if (facingForward.sqrMagnitude < 0.001f)
                    facingForward = Vector3.forward;

                var rootName = string.IsNullOrWhiteSpace(runtimeDefinition.gameObjectName)
                    ? $"{SpawnedQuestGiverName}.{character.id}"
                    : runtimeDefinition.gameObjectName;
                var root = new GameObject(rootName);
                root.name = rootName;

                var itemsContainer = ResolveItemsContainerTransform();
                if (itemsContainer != null)
                    root.transform.SetParent(itemsContainer, false);

                root.transform.position = spawnPosition.Value;
                root.transform.rotation = Quaternion.LookRotation(-facingForward, Vector3.up);

                var hasVisual = StreetQuestCharacterCreator.TryAttachPrefabVisual(root.transform, runtimeDefinition, out var _);
                if (!hasVisual)
                    StreetQuestCharacterCreator.BuildFallbackStandVisual(root.transform, runtimeDefinition);

                Component sellerStandController = null;
                if (runtimeDefinition.interactable)
                {
                    var interactionRenderer = StreetQuestCharacterCreator.CreateInvisibleInteractionRendererProxy(
                        root.transform,
                        runtimeDefinition);
                    if (interactionRenderer == null)
                    {
                        DestroySpawnedCharacter(character.id);
                        return false;
                    }

                    StreetQuestCharacterCreator.AddInteractionCollider(root, runtimeDefinition, hasVisual);

                    var navTarget = new GameObject("NavMeshTarget").transform;
                    navTarget.SetParent(root.transform, false);
                    navTarget.localPosition = runtimeDefinition.NavTargetLocalOffsetOr(NavTargetLocalOffset);

                    sellerStandController = (Component)root.AddComponent(sellerStandControllerType);
                    SetMemberValue(sellerStandController, "primaryInteractionEnabled", true);
                    SetMemberValue(sellerStandController, "simpleOverlayType", 4);
                    SetMemberValue(sellerStandController, "detailedOverlayType", 1024);
                    SetMemberValue(
                        sellerStandController,
                        "customOverlayHeaderKey",
                        string.IsNullOrWhiteSpace(runtimeDefinition.overlayHeaderKey) ? SellerStandOverlayHeaderKey : runtimeDefinition.overlayHeaderKey);
                    SetMemberValue(sellerStandController, "blockOutline", true);
                    SetMemberValue(sellerStandController, "renderers", new[] { interactionRenderer });
                    SetMemberValue(sellerStandController, "navMeshTargets", new[] { navTarget });
                    SetMemberValue(sellerStandController, "itemsToSell", new[] { "ba:itemname_hotdog" });
                    if (!hasVisual)
                    {
                        var sellerPosition = new GameObject("SellerPosition").transform;
                        sellerPosition.SetParent(root.transform, false);
                        sellerPosition.localPosition = runtimeDefinition.SellerPositionLocalOffsetOr(SellerPositionLocalOffset);
                        SetMemberValue(sellerStandController, "sellerPosition", sellerPosition);
                    }

                    InvokeParameterlessMethod(sellerStandController, "Show");
                }

                SpawnedCharacterRoots[character.id] = root;
                SpawnedCharacterStateSignatures[character.id] = stateSignature;
                if (sellerStandController != null)
                {
                    SpawnedCharacterControllers[character.id] = sellerStandController;
                    CharacterIdsByControllerInstanceId[sellerStandController.GetInstanceID()] = character.id;
                }

                EnsureCharacterWalker(root, runtimeDefinition);
                SetSpawnedCharacterVisibility(character.id, activateAfterSpawn);

                stopwatch.Stop();
                LogDebug($"EnsureSpawnedCharacter spawned character={character.id} position={FormatVector3(root.transform.position)}");
                LogDebug($"EnsureSpawnedCharacter spawnComplete character={character.id} durationMs={stopwatch.ElapsedMilliseconds} activate={activateAfterSpawn} interactable={runtimeDefinition.interactable}");
                return true;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                LogDebug($"EnsureSpawnedCharacter failed for {character.id}: {exception}");
                LogDebug($"EnsureSpawnedCharacter failed character={character.id} durationMs={stopwatch.ElapsedMilliseconds}");
                Debug.LogWarning($"StreetQuestRPG: Failed to spawn quest giver '{character.id}'. {exception}");
                DestroySpawnedCharacter(character.id);
                return false;
            }
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
                if (character == null ||
                    string.IsNullOrWhiteSpace(character.id) ||
                    !StreetQuestCharacterRuntimeResolver.HasAnySchedule(character) ||
                    SpawnedCharacterRoots.ContainsKey(character.id))
                {
                    continue;
                }

                var runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinitionWithoutScheduleGate(character);
                if (runtimeDefinition == null || !runtimeDefinition.enabled)
                    continue;

                EnsureSpawnedCharacter(character, ignoreScheduleGate: true, activateAfterSpawn: false);
            }
        }


        private static void SetSpawnedCharacterVisibility(string characterId, bool visible)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (!SpawnedCharacterRoots.TryGetValue(characterId, out var root) || root == null)
                return;

            if (!root.activeSelf)
                root.SetActive(true);

            var walker = root.GetComponent<StreetQuestCharacterWalker>();
            if (walker != null)
            {
                if (SpawnedCharacterControllers.TryGetValue(characterId, out var walkerController) && walkerController != null)
                {
                    SetMemberValue(walkerController, "primaryInteractionEnabled", visible);
                    if (visible)
                        TryInvokeParameterlessMethod(walkerController, "Show");
                    else if (!TryInvokeParameterlessMethod(walkerController, "Hide"))
                        SetMemberValue(walkerController, "blockOutline", true);
                }

                walker.OnVisibilityChanged(visible);
                return;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }

            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                    collider.enabled = visible;
            }

            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null)
                    continue;

                animator.enabled = visible;
                if (visible)
                    animator.Update(0f);
            }

            if (SpawnedCharacterControllers.TryGetValue(characterId, out var controller) && controller != null)
            {
                SetMemberValue(controller, "primaryInteractionEnabled", visible);
                if (visible)
                    TryInvokeParameterlessMethod(controller, "Show");
                else if (!TryInvokeParameterlessMethod(controller, "Hide"))
                    SetMemberValue(controller, "blockOutline", true);
            }
        }


        private static void EnsureCharacterWalker(GameObject root, StreetQuestCharacterDefinition definition)
        {
            if (root == null || definition == null)
                return;

            if ((definition.walkAwayWaypoints == null || definition.walkAwayWaypoints.Length == 0) &&
                (definition.walkInWaypoints == null || definition.walkInWaypoints.Length == 0))
            {
                return;
            }

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
                definition.walkAwayCompletedStoryFlags,
                definition.despawnAfterWalkAway,
                definition.WalkInWaypointsOrEmpty(),
                definition.walkInSpeed,
                definition.walkInArrivalHour,
                definition.walkInArrivalMinute);
        }


        private static void DestroySpawnedCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (SpawnedCharacterControllers.TryGetValue(characterId, out var controller) && controller != null)
                CharacterIdsByControllerInstanceId.Remove(controller.GetInstanceID());

            if (SpawnedCharacterRoots.TryGetValue(characterId, out var root) && root != null)
                UnityEngine.Object.Destroy(root);

            SpawnedCharacterRoots.Remove(characterId);
            SpawnedCharacterControllers.Remove(characterId);
            SpawnedCharacterStateSignatures.Remove(characterId);
        }


        private static void EnsureQuestGiverCtaBehaviorInstalled()
        {
            if (QuestGiverCtaInstalled)
            {
                LogDebug("EnsureQuestGiverCtaBehaviorInstalled skipped: already installed");
                return;
            }

            var ctaBehaviorsField = typeof(CtaManager).GetField(
                "CtaBehaviors",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var ctaBehaviors = ctaBehaviorsField?.GetValue(null) as IList<ICtaBehavior>;
            if (ctaBehaviors == null)
            {
                LogDebug("EnsureQuestGiverCtaBehaviorInstalled failed: CtaBehaviors list missing");
                return;
            }

            if (ctaBehaviors.Any(x => x is StreetQuestGiverCtaBehavior))
            {
                QuestGiverCtaInstalled = true;
                LogDebug("EnsureQuestGiverCtaBehaviorInstalled found existing StreetQuestGiverCtaBehavior");
                return;
            }

            ctaBehaviors.Insert(0, new StreetQuestGiverCtaBehavior());
            QuestGiverCtaInstalled = true;
            LogDebug($"EnsureQuestGiverCtaBehaviorInstalled inserted custom behavior listCountAfter={ctaBehaviors.Count}");
        }


        private static void RemoveLegacyQuestGiverCtaBehaviors()
        {
            try
            {
                var ctaManagerType = FindType("CtaManager");
                var ctaBehaviorType = FindType("ICtaBehavior");
                var ctaBehaviorsField = ctaManagerType?.GetField(
                    "CtaBehaviors",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var ctaBehaviors = ctaBehaviorsField?.GetValue(null) as System.Collections.IList;
                if (ctaBehaviors == null)
                {
                    LogDebug("RemoveLegacyQuestGiverCtaBehaviors skipped: CtaBehaviors list missing");
                    return;
                }

                var removedCount = 0;
                for (var index = ctaBehaviors.Count - 1; index >= 0; index--)
                {
                    var behavior = ctaBehaviors[index];
                    if (behavior == null)
                        continue;

                    if (ctaBehaviorType != null && !ctaBehaviorType.IsInstanceOfType(behavior))
                        continue;

                    var typeName = behavior.GetType().FullName ?? string.Empty;
                    if (!typeName.Contains("StreetQuestGiverCtaBehavior", StringComparison.Ordinal) &&
                        !typeName.Contains("StreetQuestRPG", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ctaBehaviors.RemoveAt(index);
                    removedCount++;
                }

                LogDebug($"RemoveLegacyQuestGiverCtaBehaviors removed={removedCount} remaining={ctaBehaviors.Count}");
                QuestGiverCtaInstalled = false;
            }
            catch (Exception exception)
            {
                LogDebug($"RemoveLegacyQuestGiverCtaBehaviors failed: {exception}");
            }
        }


        internal static bool IsSpawnedQuestGiverController(object controller)
        {
            if (controller == null)
                return false;

            return controller is UnityEngine.Object unityObject &&
                   CharacterIdsByControllerInstanceId.ContainsKey(unityObject.GetInstanceID());
        }


        internal static string GetCharacterIdForController(object controller)
        {
            if (controller is not UnityEngine.Object unityObject)
                return null;

            return CharacterIdsByControllerInstanceId.TryGetValue(unityObject.GetInstanceID(), out var characterId)
                ? characterId
                : null;
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


        private static Transform ResolveItemsContainerTransform()
        {
            if (CachedItemsContainerTransform != null)
                return CachedItemsContainerTransform;

            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null)
                    continue;

                if (string.Equals(GetHierarchyPath(transform), "GameManager/ItemsContainer", StringComparison.OrdinalIgnoreCase))
                {
                    CachedItemsContainerTransform = transform;
                    return transform;
                }
            }

            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null)
                    continue;

                if (string.Equals(transform.name, "ItemsContainer", StringComparison.OrdinalIgnoreCase))
                {
                    CachedItemsContainerTransform = transform;
                    return transform;
                }
            }

            return null;
        }


        private static Type ResolveSellerStandControllerType()
        {
            if (CachedSellerStandControllerType != null)
                return CachedSellerStandControllerType;

            CachedSellerStandControllerType = FindType("SellerStandController");
            return CachedSellerStandControllerType;
        }


        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);

            return string.Join("/", names);
        }


        private static string FormatVector3(Vector3 value) =>
            $"({value.x:F2}, {value.y:F2}, {value.z:F2})";


        private sealed class StreetQuestGiverCtaBehavior : ICtaBehavior
        {
            public override bool ShouldShow(EntityController entityController) =>
                IsSpawnedQuestGiverController(entityController);

            public override (string, Action) GetCta(EntityController entityController)
            {
                var characterId = GetCharacterIdForController(entityController);
                var character = StreetQuestCharacterCatalog.Get(characterId);
                var ctaKey = string.IsNullOrWhiteSpace(character?.ctaKey) ? QuestGiverCtaKey : character.ctaKey;
                var ctaText = ctaKey.Localize(new Dictionary<string, string>
                {
                    { "npcname", ResolveCharacterDisplayName(characterId) }
                }).ToString();
                var dialogTypeKey = string.IsNullOrWhiteSpace(character?.dialogTypeKey) ? "streetquest_mack_dialog" : character.dialogTypeKey;
                var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(dialogTypeKey);
                return (ctaText, () => TryOpenQuestDialog(dialogType));
            }
        }
    }
}
