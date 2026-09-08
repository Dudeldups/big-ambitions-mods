#nullable enable
using System;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.AI;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsPlayerService
    {
        private const float GroundOffset = 0.05f;
        private readonly ModContext context;

        public DeveloperToolsPlayerService(ModContext context) => this.context = context;

        public Transform? PlayerTransform => PlayerHelper.PlayerController?.transform;

        public bool AddMoney(float amount, out string message)
        {
            if (SaveGameManager.Current == null || float.IsNaN(amount) || float.IsInfinity(amount) || Math.Abs(amount) < 0.01f)
            {
                message = "Enter a non-zero amount while a save is loaded.";
                return false;
            }

            GameManager.Command_ChangeMoney(amount);
            SaveGameManager.Current.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            message = "Added " + amount.ToString("$#,##0.##") + ".";
            context.Logger.Info("DeveloperTools: money changed through game cheat transaction; amount=" + amount + ".");
            return true;
        }

        public bool ResetNeeds(out string message)
        {
            var save = SaveGameManager.Current;
            if (save == null)
            {
                message = "No active save is loaded.";
                return false;
            }

            var before = "hunger=" + save.Hunger.ToString("0.##") + ", energy=" + save.Energy.ToString("0.##") + ", happiness=" + save.Happiness.ToString("0.##");
            save.Hunger = 100f;
            save.Energy = 100f;
            save.Happiness = 100f;
            save.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            message = "Hunger, energy and happiness reset to 100.";
            context.Logger.Info("DeveloperTools: player needs reset; before=" + before + ", after=100/100/100.");
            return true;
        }

        public bool Teleport(Vector3 requested, bool preferSafeGround, out Vector3 finalPosition, out string message)
        {
            finalPosition = requested;
            var player = PlayerHelper.PlayerController;
            if (player == null)
            {
                message = "Player is not available.";
                return false;
            }

            if (preferSafeGround)
                finalPosition = ResolveLandingPosition(requested);

            var root = player.transform;
            var characterControllers = root.GetComponentsInChildren<CharacterController>(true);
            var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
            var controllerStates = Array.ConvertAll(characterControllers, value => value != null && value.enabled);
            var agentStates = Array.ConvertAll(agents, value => value != null && value.enabled);

            try
            {
                foreach (var controller in characterControllers)
                    if (controller != null) controller.enabled = false;
                foreach (var agent in agents)
                {
                    if (agent == null || !agent.enabled) continue;
                    if (agent.isOnNavMesh) agent.ResetPath();
                    agent.enabled = false;
                }
                root.position = finalPosition;
                Physics.SyncTransforms();
            }
            finally
            {
                for (var index = 0; index < agents.Length; index++)
                {
                    var agent = agents[index];
                    if (agent == null) continue;
                    agent.enabled = agentStates[index];
                    if (agent.enabled && agent.isOnNavMesh)
                    {
                        agent.Warp(finalPosition);
                        agent.ResetPath();
                    }
                }
                for (var index = 0; index < characterControllers.Length; index++)
                    if (characterControllers[index] != null) characterControllers[index].enabled = controllerStates[index];
                Physics.SyncTransforms();
            }

            message = "Teleported player to " + FormatVector(finalPosition) + ".";
            context.Logger.Info("DeveloperTools: player teleported; requested=" + FormatVector(requested) + ", final=" + FormatVector(finalPosition) + ".");
            return true;
        }

        public static Vector3 ResolveLandingPosition(Vector3 requested)
        {
            if (NavMesh.SamplePosition(requested, out var navHit, 15f, NavMesh.AllAreas))
                return navHit.position + Vector3.up * GroundOffset;
            if (Physics.Raycast(requested + Vector3.up * 250f, Vector3.down, out var groundHit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return groundHit.point + Vector3.up * GroundOffset;
            return requested;
        }

        public static string FormatVector(Vector3 vector) =>
            "(" + vector.x.ToString("0.###") + ", " + vector.y.ToString("0.###") + ", " + vector.z.ToString("0.###") + ")";

        public static string FormatJson(Vector3 vector) =>
            "{ \"x\": " + vector.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", \"y\": " + vector.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
            ", \"z\": " + vector.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " }";
    }
}
