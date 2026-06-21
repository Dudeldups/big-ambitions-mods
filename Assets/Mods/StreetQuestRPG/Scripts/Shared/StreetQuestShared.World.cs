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
        private const float DebugTeleportNpcDistance = 2f;
        private const float DebugTeleportNavMeshProbeRadius = 4f;
        private const float DebugTeleportGroundOffset = 0.05f;

        public static Vector3 GetPlayerWorldPosition()
        {
            return PlayerHelper.PlayerController != null
                ? PlayerHelper.PlayerController.transform.position
                : PlayerHelper.GetPosition();
        }


        public static void RecordCharacterInteraction(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            RecordKnownCharacter(characterId);
        }

        public static bool CompleteTalkObjectiveInteraction(
            StreetQuestQuestDefinition quest,
            StreetQuestQuestObjectiveDefinition objective)
        {
            var stopwatch = Stopwatch.StartNew();
            if (quest == null || objective == null || objective.ObjectiveType != StreetQuestQuestObjectiveType.TalkToCharacter)
                return false;

            RecordKnownCharacter(objective.CharacterId);
            MarkObjectiveToken(objective.GetTrackingToken(quest.Id));
            AddStoryFlags(objective.CompletedStoryFlags);
            stopwatch.Stop();
            LogDebug($"CompleteTalkObjectiveInteraction end quest={quest.Id} character={objective.CharacterId} durationMs={stopwatch.ElapsedMilliseconds}");
            return true;
        }


        public static void TickWorldObjectives()
        {
            var playerPosition = PlayerHelper.PlayerController != null
                ? PlayerHelper.PlayerController.transform.position
                : PlayerHelper.GetPosition();

            var quests = new List<StreetQuestQuestDefinition>();
            var mainQuest = GetCurrentMainQuest();
            if (mainQuest != null)
                quests.Add(mainQuest);
            quests.AddRange(GetActiveSideQuests());

            foreach (var quest in quests.Where(value =>
                         value != null &&
                         GetQuestProgress(value.Id) == StreetQuestQuestProgressState.Active))
            {
                foreach (var objective in quest.Objectives.Where(value => value != null))
                {
                    if (objective.ObjectiveType != StreetQuestQuestObjectiveType.VisitLocation || objective.worldPosition == null)
                        continue;

                    if (Vector3.Distance(playerPosition, objective.worldPosition.ToVector3()) > objective.Radius)
                        continue;

                    MarkObjectiveToken(objective.GetTrackingToken(quest.Id));
                }
            }
        }


        public static bool IsObjectiveSatisfiedForDebug(
            StreetQuestQuestDefinition quest,
            StreetQuestQuestObjectiveDefinition objective)
        {
            return IsObjectiveSatisfied(quest, objective);
        }


        public static bool TeleportPlayerToCharacter(string characterId)
        {
            var character = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(
                StreetQuestCharacterCatalog.Get(characterId));
            var playerController = PlayerHelper.PlayerController;
            if (character == null || playerController == null)
                return false;

            var characterPosition = character.PositionOr(Vector3.zero);
            var characterForward = FlattenDirection(character.ForwardOr(Vector3.forward));
            if (TryGetSpawnedCharacterRoot(characterId, out var spawnedRoot) && spawnedRoot != null)
            {
                characterPosition = spawnedRoot.transform.position;
                var spawnedForward = FlattenDirection(spawnedRoot.transform.forward);
                if (spawnedForward.sqrMagnitude >= 0.001f)
                    characterForward = spawnedForward;
            }

            if (characterForward.sqrMagnitude < 0.001f)
                characterForward = Vector3.forward;

            var targetPosition = characterPosition + (characterForward.normalized * DebugTeleportNpcDistance);
            targetPosition.y = characterPosition.y + DebugTeleportGroundOffset;
            var targetRotation = Quaternion.LookRotation(-characterForward.normalized, Vector3.up);

            return TeleportPlayerToExactPosition(playerController, targetPosition, targetRotation, applyRotation: true, $"character={characterId}");
        }


        public static bool TeleportPlayerToWorldPosition(Vector3 worldPosition)
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return false;

            return TeleportPlayerToExactPosition(
                playerController,
                worldPosition + new Vector3(0f, 0f, 1.5f),
                Quaternion.identity,
                applyRotation: false,
                $"world={FormatVector3(worldPosition)}");
        }


        private static bool TeleportPlayerToExactPosition(
            Component playerController,
            Vector3 requestedPosition,
            Quaternion requestedRotation,
            bool applyRotation,
            string source)
        {
            if (playerController == null)
                return false;

            var playerTransform = playerController.transform;
            var finalPosition = ResolveDebugTeleportLandingPosition(requestedPosition);
            var characterControllers = playerTransform.GetComponentsInChildren<CharacterController>(true);
            var navMeshAgents = playerTransform.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true);
            var rigidbodies = playerTransform.GetComponentsInChildren<Rigidbody>(true);
            var characterControllerStates = characterControllers.Select(value => value != null && value.enabled).ToArray();
            var navMeshAgentStates = navMeshAgents.Select(value => value != null && value.enabled).ToArray();

            try
            {
                foreach (var rigidbody in rigidbodies)
                {
                    if (rigidbody == null)
                        continue;

                    rigidbody.velocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                }

                foreach (var characterController in characterControllers)
                {
                    if (characterController != null)
                        characterController.enabled = false;
                }

                for (var i = 0; i < navMeshAgents.Length; i++)
                {
                    var agent = navMeshAgents[i];
                    if (agent == null || !agent.enabled)
                        continue;

                    if (agent.isOnNavMesh)
                        agent.ResetPath();

                    agent.enabled = false;
                }

                if (applyRotation)
                    playerTransform.SetPositionAndRotation(finalPosition, requestedRotation);
                else
                    playerTransform.position = finalPosition;

                Physics.SyncTransforms();
            }
            finally
            {
                for (var i = 0; i < navMeshAgents.Length; i++)
                {
                    var agent = navMeshAgents[i];
                    if (agent == null)
                        continue;

                    agent.enabled = navMeshAgentStates.Length > i && navMeshAgentStates[i];
                    if (!agent.enabled)
                        continue;

                    if (agent.isOnNavMesh)
                    {
                        agent.Warp(finalPosition);
                        agent.ResetPath();
                    }
                }

                for (var i = 0; i < characterControllers.Length; i++)
                {
                    var characterController = characterControllers[i];
                    if (characterController != null && characterControllerStates.Length > i)
                        characterController.enabled = characterControllerStates[i];
                }

                Physics.SyncTransforms();
            }

            LogDebug(
                $"DebugTeleport source={source} requested={FormatVector3(requestedPosition)} final={FormatVector3(finalPosition)} " +
                $"navAgents={navMeshAgents.Length} characterControllers={characterControllers.Length}");
            return true;
        }


        private static Vector3 ResolveDebugTeleportLandingPosition(Vector3 requestedPosition)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(
                    requestedPosition,
                    out var navMeshHit,
                    DebugTeleportNavMeshProbeRadius,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                var position = navMeshHit.position;
                position.y += DebugTeleportGroundOffset;
                return position;
            }

            return requestedPosition;
        }
    }
}
