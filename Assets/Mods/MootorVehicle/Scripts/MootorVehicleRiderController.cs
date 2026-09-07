#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace MootorVehicle
{
    [DefaultExecutionOrder(100)]
    internal sealed class MootorVehicleRiderController : MonoBehaviour
    {
        private const string RiderSeatName = "MootorVehicle_RiderSeat";
        private const string SittingClipName = "SitDeliveryTruck";
        private const float RiderScale = 0.94f;
        private const int MaximumAttempts = 20;
        private const int MaximumEngineStartAttempts = 4;
        private const float DormantEngineGracePeriod = 0.35f;
        private const float EngineRestartDelay = 0.1f;
        private const float EngineStartRetryDelay = 0.75f;
        private const float MinimumHealthyEngineRpm = 100f;

        private static readonly Vector3 LeftHandOffset = new(-0.3f, 0f, 0.38f);
        private static readonly Vector3 RightHandOffset = new(0.3f, 0f, 0.38f);
        private static readonly Vector3 LeftElbowHintOffset = new(-0.46f, 0.08f, 0.18f);
        private static readonly Vector3 RightElbowHintOffset = new(0.46f, 0.08f, 0.18f);
        private static readonly Vector3 LeftFootOffset = new(-0.44f, -0.42f, 0.1f);
        private static readonly Vector3 RightFootOffset = new(0.44f, -0.42f, 0.1f);
        private static readonly Vector3 LeftKneeHintOffset = new(-0.64f, 0.1f, 0.4f);
        private static readonly Vector3 RightKneeHintOffset = new(0.64f, 0.1f, 0.4f);

        private readonly List<UnityEngine.Object> ownedAssets = new();
        private VehicleController? vehicle;
        private PhysicsVehicle? physicsVehicle;
        private Rigidbody? vehicleBody;
        private ModContext? context;
        private GameObject? riderRoot;
        private Transform? hips;
        private Transform? riderSeat;
        private PoseLimb? leftArm;
        private PoseLimb? rightArm;
        private PoseLimb? leftLeg;
        private PoseLimb? rightLeg;
        private PlayableGraph poseGraph;
        private AnimationClipPlayable pose;
        private float poseLength;
        private float poseTime;
        private bool occupied;
        private int attempts;
        private float nextAttempt;
        private float driveInputDetectedAt = -1f;
        private bool driveResponseLogged;
        private bool drivetrainLoggingFailed;
        private int engineStartAttempts;
        private float nextEngineStartAttempt;
        private bool engineStartConfirmedLogged;
        private bool engineStartFailureLogged;
        private bool engineRestartPending;
        private float dormantThrottleDetectedAt = -1f;
        private string? lastFailure;

        public void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
            physicsVehicle = controller.GetComponent<PhysicsVehicle>();
            vehicleBody = controller.GetComponent<Rigidbody>();
            context = modContext;
        }

        private void LateUpdate()
        {
            if (vehicle == null)
                return;

            var isOccupied = vehicle.controlledByPlayer;
            if (isOccupied != occupied)
            {
                occupied = isOccupied;
                attempts = 0;
                nextAttempt = 0f;
                driveInputDetectedAt = -1f;
                driveResponseLogged = false;
                drivetrainLoggingFailed = false;
                engineStartAttempts = 0;
                nextEngineStartAttempt = Time.unscaledTime + 0.15f;
                engineStartConfirmedLogged = false;
                engineStartFailureLogged = false;
                engineRestartPending = false;
                dormantThrottleDetectedAt = -1f;
                lastFailure = null;

                if (!occupied)
                {
                    RemoveRider();
                    LogInfo("dismounted; visible rider removed.");
                }
                else
                {
                    LogInfo("mounted; preparing current player appearance.");
                    LogDrivetrainState("mount");
                }
            }

            if (!occupied)
                return;

            UpdateEngineStart();
            UpdateDrivetrainDiagnostics();

            try
            {
                if (riderRoot == null)
                {
                    if (attempts >= MaximumAttempts || Time.unscaledTime < nextAttempt)
                        return;

                    attempts++;
                    nextAttempt = Time.unscaledTime + 0.5f;
                    CreateRider();
                }

                poseTime = Mathf.Repeat(poseTime + Time.deltaTime, poseLength);
                pose.SetTime(poseTime);
                leftArm?.RestoreAnimationPose();
                rightArm?.RestoreAnimationPose();
                leftLeg?.RestoreAnimationPose();
                rightLeg?.RestoreAnimationPose();
                poseGraph.Evaluate(0f);
                AlignWithSeat();
                ApplyCowRidingPose();
            }
            catch (Exception exception)
            {
                RemoveRider();
                var reason = exception.GetBaseException().Message;
                if (reason != lastFailure || attempts >= MaximumAttempts)
                {
                    lastFailure = reason;
                    context?.Logger.Warn(
                        $"Moo-tor Vehicle rider vehicle={vehicle.GetInstanceID()} " +
                        $"attempt={attempts}/{MaximumAttempts}: {reason}");
                }
            }
        }

        private void CreateRider()
        {
            var character = PlayerHelper.PlayerController?.Character;
            var appearance = character?.appearanceSetter;
            if (character == null || appearance == null)
                throw new InvalidOperationException("Player appearance is not ready.");

            var sourceAnimator = typeof(AppearanceSetter).GetField(
                "animator",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(appearance) as Animator;
            if (sourceAnimator == null ||
                sourceAnimator.avatar == null ||
                !sourceAnimator.avatar.isHuman ||
                sourceAnimator.runtimeAnimatorController == null)
            {
                throw new InvalidOperationException("Player humanoid animator/avatar is not ready.");
            }

            AnimationClip? sittingClip = null;
            foreach (var clip in sourceAnimator.runtimeAnimatorController.animationClips)
                if (clip != null && string.Equals(clip.name, SittingClipName, StringComparison.Ordinal))
                    sittingClip = clip;

            if (sittingClip == null || sittingClip.length <= 0f)
                throw new InvalidOperationException($"Native seated animation '{SittingClipName}' is unavailable.");

            riderSeat = null;
            foreach (var child in vehicle!.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, RiderSeatName, StringComparison.Ordinal))
                    riderSeat = child;

            if (riderSeat == null)
                throw new InvalidOperationException("Cow rider-seat reference is missing.");

            var sourceRoot = character.transform;
            if (!sourceAnimator.transform.IsChildOf(sourceRoot) && sourceAnimator.transform != sourceRoot)
                throw new InvalidOperationException("Player animator is outside the character hierarchy.");

            riderRoot = new GameObject("MootorVehicle_VisibleRider");
            riderRoot.SetActive(false);
            riderRoot.layer = sourceRoot.gameObject.layer;
            riderRoot.transform.SetParent(vehicle.transform, false);

            var transforms = new Dictionary<Transform, Transform>();
            CopyTransforms(sourceRoot, riderRoot.transform, transforms);
            riderRoot.transform.localScale = Vector3.one * RiderScale;
            riderRoot.transform.localRotation = Quaternion.identity;
            riderRoot.transform.localPosition = Vector3.zero;

            var rendererCount = 0;
            var lowerDetailRenderers = GetLowerDetailRenderers(appearance.transform);
            foreach (var source in appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!source.enabled || source.sharedMesh == null || !IsActiveWithinCharacter(source.transform, sourceRoot))
                    continue;

                if (source.forceRenderingOff ||
                    source.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                    lowerDetailRenderers.Contains(source))
                {
                    continue;
                }

                CopyRenderer(source, transforms);
                rendererCount++;
            }

            if (rendererCount == 0)
                throw new InvalidOperationException("Player has no visible skinned appearance meshes yet.");

            var animator = transforms[sourceAnimator.transform].gameObject.AddComponent<Animator>();
            animator.avatar = sourceAnimator.avatar;
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            riderRoot.SetActive(true);
            poseGraph = PlayableGraph.Create("Moo-tor Vehicle visible rider");
            poseGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            pose = AnimationClipPlayable.Create(poseGraph, sittingClip);
            pose.SetApplyFootIK(false);
            pose.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(poseGraph, "Cow-riding pose", animator).SetSourcePlayable(pose);
            poseLength = sittingClip.length;
            poseTime = 0f;
            poseGraph.Play();
            poseGraph.Evaluate(0f);

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
                throw new InvalidOperationException("Seated avatar has no humanoid hips bone.");

            AlignWithSeat();
            leftArm = CreateLimb(
                animator,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand);
            rightArm = CreateLimb(
                animator,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand);
            leftLeg = CreateLimb(
                animator,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot);
            rightLeg = CreateLimb(
                animator,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot);

            var poseBefore = PosePositions();
            ApplyCowRidingPose();
            LogInfo(
                $"created visible rider renderers={rendererCount} scale={RiderScale:F2} " +
                $"seatLocal={riderSeat.localPosition} clip='{sittingClip.name}'.");
            LogInfo($"cow-riding pose before={poseBefore} after={PosePositions()}.");
        }

        private void AlignWithSeat()
        {
            if (riderRoot == null || hips == null || riderSeat == null)
                return;

            riderRoot.transform.rotation = riderSeat.rotation;
            riderRoot.transform.position += riderSeat.position - hips.position;
        }

        private PoseLimb? CreateLimb(
            Animator animator,
            HumanBodyBones upperBone,
            HumanBodyBones lowerBone,
            HumanBodyBones endBone)
        {
            var upper = animator.GetBoneTransform(upperBone);
            var lower = animator.GetBoneTransform(lowerBone);
            var end = animator.GetBoneTransform(endBone);
            if (upper != null && lower != null && end != null)
                return new PoseLimb(upper, lower, end);

            context?.Logger.Warn(
                $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: " +
                $"cannot refine {endBone}; limb bones are missing. Keeping native pose.");
            return null;
        }

        private void ApplyCowRidingPose()
        {
            if (riderSeat == null)
                return;

            leftArm?.AimAt(
                riderSeat.TransformPoint(LeftHandOffset),
                riderSeat.TransformPoint(LeftElbowHintOffset));
            rightArm?.AimAt(
                riderSeat.TransformPoint(RightHandOffset),
                riderSeat.TransformPoint(RightElbowHintOffset));
            leftLeg?.AimAt(
                riderSeat.TransformPoint(LeftFootOffset),
                riderSeat.TransformPoint(LeftKneeHintOffset));
            rightLeg?.AimAt(
                riderSeat.TransformPoint(RightFootOffset),
                riderSeat.TransformPoint(RightKneeHintOffset));
        }

        private string PosePositions()
        {
            return
                $"hands=({VehiclePosition(leftArm?.End)},{VehiclePosition(rightArm?.End)}) " +
                $"knees=({VehiclePosition(leftLeg?.Middle)},{VehiclePosition(rightLeg?.Middle)}) " +
                $"feet=({VehiclePosition(leftLeg?.End)},{VehiclePosition(rightLeg?.End)})";
        }

        private string VehiclePosition(Transform? target)
        {
            return target != null && vehicle != null
                ? vehicle.transform.InverseTransformPoint(target.position).ToString("F3")
                : "missing";
        }

        private void UpdateEngineStart()
        {
            if (physicsVehicle == null)
                return;

            try
            {
                var engine = physicsVehicle.powertrain.engine;
                var rpm = engine.RPMPercent * engine.revLimiterRPM;
                if (rpm >= MinimumHealthyEngineRpm)
                {
                    if (engineStartAttempts > 0 && !engineStartConfirmedLogged)
                    {
                        engineStartConfirmedLogged = true;
                        LogInfo(
                            $"native engine start confirmed after {engineStartAttempts} request(s); " +
                            $"rpm={rpm:F0}.");
                    }
                    engineRestartPending = false;
                    dormantThrottleDetectedAt = -1f;
                    return;
                }

                if (engineRestartPending)
                {
                    if (Time.unscaledTime < nextEngineStartAttempt)
                        return;

                    engineRestartPending = false;
                    engineStartAttempts++;
                    nextEngineStartAttempt = Time.unscaledTime + EngineStartRetryDelay;
                    dormantThrottleDetectedAt = Time.unscaledTime;
                    engine.StartEngine();
                    LogInfo(
                        $"requested native engine restart attempt={engineStartAttempts}; " +
                        $"running={engine.IsRunning} rpm={rpm:F0}.");
                    return;
                }

                if (Mathf.Abs(physicsVehicle.input.Throttle) < 0.05f)
                {
                    dormantThrottleDetectedAt = -1f;
                    return;
                }

                if (dormantThrottleDetectedAt < 0f)
                {
                    dormantThrottleDetectedAt = Time.unscaledTime;
                    return;
                }

                if (Time.unscaledTime < nextEngineStartAttempt ||
                    Time.unscaledTime - dormantThrottleDetectedAt < DormantEngineGracePeriod)
                {
                    return;
                }

                if (engineStartAttempts >= MaximumEngineStartAttempts)
                {
                    if (!engineStartFailureLogged)
                    {
                        engineStartFailureLogged = true;
                        context?.Logger.Warn(
                            $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: native engine " +
                            $"did not start after {engineStartAttempts} requests while mounted.");
                    }
                    return;
                }

                engine.StopEngine();
                engineRestartPending = true;
                nextEngineStartAttempt = Time.unscaledTime + EngineRestartDelay;
                LogInfo(
                    $"reset dormant native engine before restart attempt={engineStartAttempts + 1}; " +
                    $"running={engine.IsRunning} rpm={rpm:F0}.");
            }
            catch (Exception exception)
            {
                if (engineStartFailureLogged)
                    return;
                engineStartFailureLogged = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: native engine start failed: " +
                    $"{exception.GetBaseException().Message}");
            }
        }

        private void UpdateDrivetrainDiagnostics()
        {
            if (driveResponseLogged || drivetrainLoggingFailed || physicsVehicle == null)
                return;

            try
            {
                if (Mathf.Abs(physicsVehicle.input.Throttle) < 0.05f)
                    return;

                if (driveInputDetectedAt < 0f)
                {
                    driveInputDetectedAt = Time.unscaledTime;
                    LogDrivetrainState("drive-input");
                    return;
                }

                if (Time.unscaledTime - driveInputDetectedAt < 0.75f)
                    return;

                driveResponseLogged = true;
                LogDrivetrainState("drive-response");
            }
            catch (Exception exception)
            {
                drivetrainLoggingFailed = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle drivetrain diagnostics vehicle={vehicle?.GetInstanceID()}: " +
                    $"{exception.GetBaseException().Message}");
            }
        }

        private void LogDrivetrainState(string phase)
        {
            if (physicsVehicle == null)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle drivetrain vehicle={vehicle?.GetInstanceID()} phase='{phase}': " +
                    "native NWH VehicleController is missing.");
                return;
            }

            var engine = physicsVehicle.powertrain.engine;
            var transmission = physicsVehicle.powertrain.transmission;
            var speedKmh = vehicleBody != null ? vehicleBody.velocity.magnitude * 3.6f : 0f;
            context?.Logger.Info(
                $"Moo-tor Vehicle drivetrain vehicle={vehicle?.GetInstanceID()} phase='{phase}' " +
                $"throttle={physicsVehicle.input.Throttle:F2} gear={transmission.Gear} running={engine.IsRunning} " +
                $"rpm={engine.RPMPercent * engine.revLimiterRPM:F0}/{engine.revLimiterRPM:F0} " +
                $"speed={speedKmh:F2}kmh.");
        }

        private static bool IsActiveWithinCharacter(Transform child, Transform root)
        {
            for (var current = child; current != root; current = current.parent)
            {
                if (current == null || !current.gameObject.activeSelf)
                    return false;
            }

            return true;
        }

        private sealed class PoseLimb
        {
            private readonly Transform upper;
            public readonly Transform Middle;
            public readonly Transform End;
            private Quaternion upperPose;
            private Quaternion middlePose;
            private Quaternion endPose;
            private bool hasAdjustment;

            public PoseLimb(Transform upper, Transform middle, Transform end)
            {
                this.upper = upper;
                Middle = middle;
                End = end;
            }

            public void RestoreAnimationPose()
            {
                if (!hasAdjustment)
                    return;

                upper.localRotation = upperPose;
                Middle.localRotation = middlePose;
                End.localRotation = endPose;
                hasAdjustment = false;
            }

            public void AimAt(Vector3 target, Vector3 bendHint)
            {
                if (!TrySolveJoint(
                        upper.position,
                        Middle.position,
                        End.position,
                        target,
                        bendHint,
                        out var solvedMiddle,
                        out var reachableTarget))
                {
                    return;
                }

                upperPose = upper.localRotation;
                middlePose = Middle.localRotation;
                endPose = End.localRotation;
                hasAdjustment = true;
                var endRotation = End.rotation;
                upper.rotation = Quaternion.FromToRotation(
                    Middle.position - upper.position,
                    solvedMiddle - upper.position) * upper.rotation;
                Middle.rotation = Quaternion.FromToRotation(
                    End.position - Middle.position,
                    reachableTarget - Middle.position) * Middle.rotation;
                End.rotation = endRotation;
            }
        }

        private static bool TrySolveJoint(
            Vector3 upper,
            Vector3 middle,
            Vector3 end,
            Vector3 target,
            Vector3 bendHint,
            out Vector3 solvedMiddle,
            out Vector3 reachableTarget)
        {
            solvedMiddle = middle;
            reachableTarget = end;
            var upperLength = Vector3.Distance(upper, middle);
            var lowerLength = Vector3.Distance(middle, end);
            var reach = target - upper;
            if (upperLength < 0.0001f || lowerLength < 0.0001f || reach.sqrMagnitude < 0.000001f)
                return false;

            var direction = reach.normalized;
            var distance = Mathf.Clamp(
                reach.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.0001f,
                upperLength + lowerLength - 0.0001f);
            var bend = Vector3.ProjectOnPlane(bendHint - upper, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(middle - upper, direction);
            if (bend.sqrMagnitude < 0.000001f)
                return false;

            var along =
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                (2f * distance);
            var across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            solvedMiddle = upper + direction * along + bend.normalized * across;
            reachableTarget = upper + direction * distance;
            return true;
        }

        private static HashSet<Renderer> GetLowerDetailRenderers(Transform root)
        {
            var result = new HashSet<Renderer>();
            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!group.enabled)
                    continue;

                var lods = group.GetLODs();
                if (lods.Length == 0)
                    continue;

                var highestDetail = new HashSet<Renderer>(lods[0].renderers);
                for (var level = 1; level < lods.Length; level++)
                    foreach (var renderer in lods[level].renderers)
                        if (renderer != null && !highestDetail.Contains(renderer))
                            result.Add(renderer);
            }

            return result;
        }

        private static void CopyTransforms(
            Transform source,
            Transform destination,
            Dictionary<Transform, Transform> transforms)
        {
            transforms.Add(source, destination);
            foreach (Transform child in source)
            {
                var copy = new GameObject(child.name);
                copy.SetActive(child.gameObject.activeSelf);
                copy.layer = child.gameObject.layer;
                copy.transform.SetParent(destination, false);
                copy.transform.localPosition = child.localPosition;
                copy.transform.localRotation = child.localRotation;
                copy.transform.localScale = child.localScale;
                CopyTransforms(child, copy.transform, transforms);
            }
        }

        private void CopyRenderer(
            SkinnedMeshRenderer source,
            Dictionary<Transform, Transform> transforms)
        {
            var destination = transforms[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
            var mesh = Instantiate(source.sharedMesh);
            ownedAssets.Add(mesh);
            destination.sharedMesh = mesh;

            var sourceBones = source.bones;
            var bones = new Transform[sourceBones.Length];
            for (var index = 0; index < sourceBones.Length; index++)
            {
                if (sourceBones[index] == null || !transforms.TryGetValue(sourceBones[index], out bones[index]))
                    throw new InvalidOperationException(
                        $"Appearance mesh '{source.name}' has an unmapped bone at {index}.");
            }

            destination.bones = bones;
            if (source.rootBone != null)
            {
                if (!transforms.TryGetValue(source.rootBone, out var rootBone))
                    throw new InvalidOperationException(
                        $"Appearance mesh '{source.name}' has an unmapped root bone.");
                destination.rootBone = rootBone;
            }

            var sourceMaterials = source.sharedMaterials;
            var materials = new Material[sourceMaterials.Length];
            for (var index = 0; index < sourceMaterials.Length; index++)
            {
                if (sourceMaterials[index] == null)
                    throw new InvalidOperationException(
                        $"Appearance mesh '{source.name}' has a missing material.");

                materials[index] = new Material(sourceMaterials[index]);
                ownedAssets.Add(materials[index]);
            }

            destination.sharedMaterials = materials;
            for (var index = 0; index < mesh.blendShapeCount; index++)
                destination.SetBlendShapeWeight(index, source.GetBlendShapeWeight(index));

            var properties = new MaterialPropertyBlock();
            source.GetPropertyBlock(properties);
            destination.SetPropertyBlock(properties);
            for (var index = 0; index < materials.Length; index++)
            {
                properties.Clear();
                source.GetPropertyBlock(properties, index);
                if (!properties.isEmpty)
                    destination.SetPropertyBlock(properties, index);
            }

            destination.localBounds = source.localBounds;
            destination.updateWhenOffscreen = true;
            destination.quality = source.quality;
            destination.renderingLayerMask = source.renderingLayerMask;
            destination.shadowCastingMode = ShadowCastingMode.Off;
            destination.receiveShadows = source.receiveShadows;
        }

        private void LogInfo(string message)
        {
            context?.Logger.Info(
                $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: {message}");
        }

        private void RemoveRider()
        {
            if (poseGraph.IsValid())
                poseGraph.Destroy();

            if (riderRoot != null)
            {
                riderRoot.SetActive(false);
                Destroy(riderRoot);
            }

            riderRoot = null;
            hips = null;
            riderSeat = null;
            leftArm = null;
            rightArm = null;
            leftLeg = null;
            rightLeg = null;

            foreach (var asset in ownedAssets)
                if (asset != null)
                    Destroy(asset);

            ownedAssets.Clear();
        }

        private void OnDisable()
        {
            RemoveRider();
            occupied = false;
        }

        private void OnDestroy()
        {
            RemoveRider();
        }
    }
}
