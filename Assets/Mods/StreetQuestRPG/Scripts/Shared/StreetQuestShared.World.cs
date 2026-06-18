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

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
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

            var quest = GetCurrentQuest();
            if (quest == null)
                return;

            var objective = quest.Objectives.FirstOrDefault(value =>
                value != null &&
                value.ObjectiveType == StreetQuestQuestObjectiveType.TalkToCharacter &&
                string.Equals(value.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
            if (objective == null)
                return;

            MarkObjectiveToken(objective.GetTrackingToken(quest.Id));
        }


        public static void TickWorldObjectives()
        {
            var quest = GetCurrentQuest();
            if (quest == null || GetQuestProgress(quest.Id) != StreetQuestQuestProgressState.Active)
                return;

            var playerPosition = PlayerHelper.PlayerController != null
                ? PlayerHelper.PlayerController.transform.position
                : PlayerHelper.GetPosition();

            foreach (var objective in quest.Objectives.Where(value => value != null))
            {
                if (objective.ObjectiveType != StreetQuestQuestObjectiveType.VisitLocation || objective.worldPosition == null)
                    continue;

                if (Vector3.Distance(playerPosition, objective.worldPosition.ToVector3()) > objective.Radius)
                    continue;

                MarkObjectiveToken(objective.GetTrackingToken(quest.Id));
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

            var targetPosition = character.PositionOr(Vector3.zero) + new Vector3(0f, 0f, 2f);
            var targetForward = FlattenDirection(character.ForwardOr(Vector3.forward));
            if (targetForward.sqrMagnitude < 0.001f)
                targetForward = Vector3.forward;

            var characterController = playerController.GetComponent<CharacterController>();
            var wasEnabled = characterController != null && characterController.enabled;
            if (wasEnabled)
                characterController.enabled = false;

            playerController.transform.position = targetPosition;
            playerController.transform.rotation = Quaternion.LookRotation(-targetForward, Vector3.up);

            if (wasEnabled)
                characterController.enabled = true;

            return true;
        }


        public static bool TeleportPlayerToWorldPosition(Vector3 worldPosition)
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return false;

            var characterController = playerController.GetComponent<CharacterController>();
            var wasEnabled = characterController != null && characterController.enabled;
            if (wasEnabled)
                characterController.enabled = false;

            playerController.transform.position = worldPosition + new Vector3(0f, 0f, 1.5f);

            if (wasEnabled)
                characterController.enabled = true;

            return true;
        }
    }
}
