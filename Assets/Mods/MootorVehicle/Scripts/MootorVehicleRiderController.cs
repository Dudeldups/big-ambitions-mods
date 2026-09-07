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

namespace MootorVehicle
{
    [DefaultExecutionOrder(100)]
    internal sealed class MootorVehicleRiderController : MonoBehaviour
    {
        private const string RiderSeatName = "MootorVehicle_RiderSeat";
        private const string SittingClipName = "SitDeliveryTruck";
        private const float RiderScale = 0.94f;
        private const int MaximumAttempts = 20;

        private readonly List<UnityEngine.Object> ownedAssets = new();
        private VehicleController? vehicle;
        private ModContext? context;
        private GameObject? riderRoot;
        private Transform? hips;
        private Transform? riderSeat;
        private PlayableGraph poseGraph;
        private AnimationClipPlayable pose;
        private float poseLength;
        private float poseTime;
        private bool occupied;
        private int attempts;
        private float nextAttempt;
        private string? lastFailure;

        public void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
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
                lastFailure = null;

                if (!occupied)
                {
                    RemoveRider();
                    LogInfo("dismounted; visible rider removed.");
                }
                else
                {
                    LogInfo("mounted; preparing current player appearance.");
                }
            }

            if (!occupied)
                return;

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
                poseGraph.Evaluate(0f);
                AlignWithSeat();
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
            LogInfo(
                $"created visible rider renderers={rendererCount} scale={RiderScale:F2} " +
                $"seatLocal={riderSeat.localPosition} clip='{sittingClip.name}'.");
        }

        private void AlignWithSeat()
        {
            if (riderRoot == null || hips == null || riderSeat == null)
                return;

            riderRoot.transform.rotation = riderSeat.rotation;
            riderRoot.transform.position += riderSeat.position - hips.position;
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
