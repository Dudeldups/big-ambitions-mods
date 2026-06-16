#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.SaveSystem.Legacy;
using Helpers;
using UnityEngine;
using VehicleRuntimeTuner.Utils;

namespace VehicleRuntimeTuner.Vehicle
{
    public sealed class ActiveVehicleResolver
    {
        private string? lastActiveVehicleId;
        private ActiveVehicleInfo? cachedVehicle;

        public ActiveVehicleInfo? Resolve(bool forceRefresh = false)
        {
            var activeVehicleId = SaveGameManager.Current?.ActiveVehicleId;
            if (!forceRefresh &&
                cachedVehicle != null &&
                !string.IsNullOrWhiteSpace(activeVehicleId) &&
                string.Equals(lastActiveVehicleId, activeVehicleId, StringComparison.Ordinal) &&
                cachedVehicle.Root != null)
            {
                return cachedVehicle;
            }

            lastActiveVehicleId = activeVehicleId;
            cachedVehicle = ResolveById(activeVehicleId) ?? ResolveFallbackVehicle();
            return cachedVehicle;
        }

        private static ActiveVehicleInfo? ResolveById(string? vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                return null;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return null;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance == null ||
                    !string.Equals(vehicleController.vehicleInstance.id, vehicleId, StringComparison.Ordinal))
                {
                    continue;
                }

                return CreateInfoFromController(vehicleController, vehicleController.vehicleInstance, vehicleController.vehicleType);
            }

            return null;
        }

        private static ActiveVehicleInfo? ResolveFallbackVehicle()
        {
            var allBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            foreach (var behaviour in allBehaviours)
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                var typeName = type.FullName ?? type.Name;
                if (typeName.IndexOf("VehicleController", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var vehicleInstance = VehicleRuntimeTunerReflection.GetMemberValue(behaviour, "vehicleInstance");
                var vehicleInstanceId = VehicleRuntimeTunerReflection.GetMemberValue(vehicleInstance, "id") as string;
                if (string.IsNullOrWhiteSpace(vehicleInstanceId))
                    continue;

                var controlledByPlayer = VehicleRuntimeTunerReflection.TryGetBooleanMemberValue(behaviour, "controlledByPlayer");
                if (controlledByPlayer != true)
                    continue;

                var vehicleType = VehicleRuntimeTunerReflection.GetMemberValue(behaviour, "vehicleType");
                return CreateInfoFromController(behaviour, vehicleInstance, vehicleType);
            }

            return null;
        }

        public static ActiveVehicleInfo CreateInfoFromController(object vehicleController, object? vehicleInstance, object? vehicleType)
        {
            var behaviour = vehicleController as MonoBehaviour;
            var root = behaviour != null ? behaviour.gameObject : null;
            var rigidbody = root != null ? root.GetComponent<Rigidbody>() : null;
            var wheelColliders = root != null ? root.GetComponentsInChildren<WheelCollider>(true) : Array.Empty<WheelCollider>();
            var monoBehaviours = root != null ? root.GetComponentsInChildren<MonoBehaviour>(true) : Array.Empty<MonoBehaviour>();
            var allTransforms = root != null ? root.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
            ResolveWheelReferences(allTransforms, monoBehaviours, out var frontLeftVisual, out var frontRightVisual, out var rearLeftVisual, out var rearRightVisual,
                out var frontLeftController, out var frontRightController, out var rearLeftController, out var rearRightController);

            return new ActiveVehicleInfo
            {
                VehicleController = vehicleController,
                Root = root,
                VehicleInstance = vehicleInstance,
                VehicleInstanceId = VehicleRuntimeTunerReflection.GetMemberValue(vehicleInstance, "id") as string ?? string.Empty,
                VehicleTypeName = VehicleRuntimeTunerReflection.GetMemberValue(vehicleInstance, "vehicleTypeName") as string ?? string.Empty,
                Rigidbody = rigidbody,
                VehicleType = vehicleType as Vehicles.VehicleTypes.VehicleType,
                WheelColliders = wheelColliders,
                MonoBehaviours = monoBehaviours,
                FrontLeftWheelVisual = frontLeftVisual,
                FrontRightWheelVisual = frontRightVisual,
                RearLeftWheelVisual = rearLeftVisual,
                RearRightWheelVisual = rearRightVisual,
                FrontLeftWheelController = frontLeftController,
                FrontRightWheelController = frontRightController,
                RearLeftWheelController = rearLeftController,
                RearRightWheelController = rearRightController,
                BodyColliderTransform = allTransforms.FirstOrDefault(t => string.Equals(t.name, "BodyCollider", StringComparison.OrdinalIgnoreCase)),
                BodyTransform = allTransforms.FirstOrDefault(t => string.Equals(t.name, "Body", StringComparison.OrdinalIgnoreCase)),
                PaintTransform = allTransforms.FirstOrDefault(t => string.Equals(t.name, "Paint", StringComparison.OrdinalIgnoreCase))
            };
        }

        private static void ResolveWheelReferences(
            IReadOnlyList<Transform> allTransforms,
            IReadOnlyList<MonoBehaviour> monoBehaviours,
            out Transform? frontLeftVisual,
            out Transform? frontRightVisual,
            out Transform? rearLeftVisual,
            out Transform? rearRightVisual,
            out Transform? frontLeftController,
            out Transform? frontRightController,
            out Transform? rearLeftController,
            out Transform? rearRightController)
        {
            frontLeftVisual = null;
            frontRightVisual = null;
            rearLeftVisual = null;
            rearRightVisual = null;
            frontLeftController = null;
            frontRightController = null;
            rearLeftController = null;
            rearRightController = null;

            foreach (var behaviour in monoBehaviours)
            {
                if (behaviour == null ||
                    !VehicleRuntimeTunerReflection.HasMember(behaviour, "wheel") ||
                    !VehicleRuntimeTunerReflection.HasMember(behaviour, "spring"))
                    continue;

                var transform = behaviour.transform;
                switch (transform.name)
                {
                    case var _ when transform.name.IndexOf("FrontLeft", StringComparison.OrdinalIgnoreCase) >= 0:
                        frontLeftController ??= transform;
                        frontLeftVisual ??= TryResolveWheelVisualTransform(behaviour, allTransforms);
                        break;
                    case var _ when transform.name.IndexOf("FrontRight", StringComparison.OrdinalIgnoreCase) >= 0:
                        frontRightController ??= transform;
                        frontRightVisual ??= TryResolveWheelVisualTransform(behaviour, allTransforms);
                        break;
                    case var _ when transform.name.IndexOf("RearLeft", StringComparison.OrdinalIgnoreCase) >= 0:
                        rearLeftController ??= transform;
                        rearLeftVisual ??= TryResolveWheelVisualTransform(behaviour, allTransforms);
                        break;
                    case var _ when transform.name.IndexOf("RearRight", StringComparison.OrdinalIgnoreCase) >= 0:
                        rearRightController ??= transform;
                        rearRightVisual ??= TryResolveWheelVisualTransform(behaviour, allTransforms);
                        break;
                }
            }
        }

        private static Transform? TryResolveWheelVisualTransform(MonoBehaviour wheelController, IReadOnlyList<Transform> allTransforms)
        {
            if (!VehicleRuntimeTunerReflection.TryGetMemberValue(wheelController, "wheel", out var wheelStruct) || wheelStruct == null)
                return TryResolveWheelVisualFallback(wheelController.transform, allTransforms);

            var visual = VehicleRuntimeTunerReflection.GetMemberValue(wheelStruct, "visual") ??
                         VehicleRuntimeTunerReflection.GetMemberValue(wheelStruct, "visualTransform");
            var resolved = visual switch
            {
                Transform visualTransform => visualTransform,
                GameObject visualGameObject => visualGameObject.transform,
                Component visualComponent => visualComponent.transform,
                _ => null
            };

            return resolved != null ? resolved : TryResolveWheelVisualFallback(wheelController.transform, allTransforms);
        }

        private static Transform? TryResolveWheelVisualFallback(Transform controllerTransform, IReadOnlyList<Transform> allTransforms)
        {
            Transform? bestMatch = null;
            var bestScore = int.MinValue;

            foreach (var candidate in allTransforms)
            {
                if (candidate == null || ReferenceEquals(candidate, controllerTransform))
                    continue;

                var candidateName = candidate.name ?? string.Empty;
                if (candidateName.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (candidateName.IndexOf("controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidateName.IndexOf("collider", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var score = ScoreWheelVisualCandidate(controllerTransform, candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = candidate;
                }
            }

            return bestScore > 0 ? bestMatch : null;
        }

        private static int ScoreWheelVisualCandidate(Transform controllerTransform, Transform candidate)
        {
            var controllerName = controllerTransform.name ?? string.Empty;
            var candidateName = candidate.name ?? string.Empty;
            var candidatePath = VehicleRuntimeTunerReflection.GetGameObjectPath(candidate);
            var score = 0;

            if (candidatePath.IndexOf("/Wheels/", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 5;

            if (HasFrontToken(controllerName) == HasFrontToken(candidateName))
                score += 4;
            if (HasLeftToken(controllerName) == HasLeftToken(candidateName))
                score += 4;
            if (HasRightToken(controllerName) == HasRightToken(candidateName))
                score += 4;

            var xDistance = Mathf.Abs(controllerTransform.localPosition.x - candidate.localPosition.x);
            var zDistance = Mathf.Abs(controllerTransform.localPosition.z - candidate.localPosition.z);
            score += Mathf.RoundToInt(8f - Mathf.Min(8f, (xDistance + zDistance) * 10f));

            return score;
        }

        private static bool HasFrontToken(string value)
        {
            return value.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("fl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("fr", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasLeftToken(string value)
        {
            return value.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("fl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("rl", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRightToken(string value)
        {
            return value.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("fr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("rr", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
