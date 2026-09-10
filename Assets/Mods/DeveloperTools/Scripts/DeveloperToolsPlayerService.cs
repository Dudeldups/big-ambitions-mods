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

            save.Hunger = 100f;
            save.Energy = 100f;
            save.Happiness = 100f;
            save.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            message = "Hunger, energy and happiness reset to 100.";
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

            var activeVehicle = ResolveOccupiedVehicle();
            if (activeVehicle != null)
                return TeleportActiveVehicle(activeVehicle, requested, out finalPosition, out message);

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
            if (DeveloperToolsDiagnostics.Teleport)
                context.Logger.Info("DeveloperTools: teleported player to " + FormatVector(finalPosition) + ".");
            return true;
        }

        private bool TeleportActiveVehicle(
            VehicleController vehicle,
            Vector3 requested,
            out Vector3 finalPosition,
            out string message)
        {
            finalPosition = requested;
            try
            {
                var forward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up);
                var rotation = forward.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                    : vehicle.transform.rotation;
                var rigidbody = vehicle.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.velocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                }

                // Use the game's vehicle-specific grounding path. A pedestrian
                // navmesh point is not a valid chassis position and can bury an
                // occupied vehicle below the road surface.
                VehicleHelper.TeleportVehicleToGround(vehicle, requested + Vector3.up * 0.5f, rotation);
                Physics.SyncTransforms();
                finalPosition = vehicle.transform.position;
                message = "Teleported occupied vehicle to " + FormatVector(finalPosition) + ".";
                if (DeveloperToolsDiagnostics.Teleport)
                {
                    context.Logger.Info(
                        "DeveloperTools: teleported occupied vehicle; type=" +
                        (vehicle.vehicleInstance?.vehicleTypeName ?? "unknown") +
                        ", requested=" + FormatVector(requested) +
                        ", final=" + FormatVector(finalPosition) + ".");
                }
                return true;
            }
            catch (Exception exception)
            {
                message = "Vehicle teleport failed: " + exception.GetBaseException().Message;
                context.Logger.Warn("DeveloperTools: " + message);
                return false;
            }
        }

        private static VehicleController? ResolveOccupiedVehicle()
        {
            if (!PlayerHelper.IsUsingVehicle)
                return null;

            var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            if (selectedVehicle != null && selectedVehicle.controlledByPlayer)
                return selectedVehicle;

            var playerVehicles = VehicleHelper.AllPlayerVehicles;
            if (playerVehicles == null)
                return null;
            foreach (var vehicle in playerVehicles)
                if (vehicle != null && vehicle.controlledByPlayer)
                    return vehicle;
            return null;
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
