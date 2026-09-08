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

[DefaultExecutionOrder(100)]
internal sealed class BigfootMonsterTruckDriverController : MonoBehaviour
{
    private const string SeatAnchorName = "BigfootDriverSeat";
    private const string SittingClipName = "SitDeliveryTruck";
    private const float SeatedScale = 0.94f;
    private const int MaximumAttempts = 20;

    private readonly List<UnityEngine.Object> ownedAssets = new();
    private VehicleController? vehicle;
    private ModContext? context;
    private Transform? seatAnchor;
    private GameObject? driverRoot;
    private Transform? hips;
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
        seatAnchor = FindTransform(controller.transform, SeatAnchorName);
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
                RemoveDriver();
                LogInfo("exited; centered seated-player model removed.");
            }
            else
            {
                LogInfo("occupied; preparing centered seated-player model.");
            }
        }

        if (!occupied)
            return;

        try
        {
            if (driverRoot == null)
            {
                if (attempts >= MaximumAttempts || Time.unscaledTime < nextAttempt)
                    return;
                attempts++;
                nextAttempt = Time.unscaledTime + 0.5f;
                CreateDriver();
            }

            poseTime = Mathf.Repeat(poseTime + Time.deltaTime, poseLength);
            pose.SetTime(poseTime);
            poseGraph.Evaluate(0f);
            AlignWithSeat();
        }
        catch (Exception exception)
        {
            RemoveDriver();
            var reason = exception.GetBaseException().Message;
            if (!string.Equals(reason, lastFailure, StringComparison.Ordinal) || attempts >= MaximumAttempts)
            {
                lastFailure = reason;
                context?.Logger.Warn(
                    $"BigfootMonsterTruck driver vehicle={vehicle.GetInstanceID()} " +
                    $"attempt={attempts}/{MaximumAttempts}: {reason}");
            }
        }
    }

    private void CreateDriver()
    {
        if (vehicle == null)
            throw new InvalidOperationException("Vehicle is unavailable.");
        seatAnchor ??= FindTransform(vehicle.transform, SeatAnchorName);
        if (seatAnchor == null)
            throw new InvalidOperationException("Centered driver-seat anchor is missing.");

        var character = PlayerHelper.PlayerController?.Character;
        var appearance = character?.appearanceSetter;
        if (character == null || appearance == null)
            throw new InvalidOperationException("Player appearance is not ready.");

        var sourceAnimator = typeof(AppearanceSetter).GetField(
            "animator",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(appearance) as Animator;
        if (sourceAnimator == null || sourceAnimator.avatar == null || !sourceAnimator.avatar.isHuman ||
            sourceAnimator.runtimeAnimatorController == null)
            throw new InvalidOperationException("Player humanoid animator/avatar is not ready.");

        AnimationClip? sittingClip = null;
        foreach (var clip in sourceAnimator.runtimeAnimatorController.animationClips)
            if (clip != null && string.Equals(clip.name, SittingClipName, StringComparison.Ordinal))
                sittingClip = clip;
        if (sittingClip == null || sittingClip.length <= 0f)
            throw new InvalidOperationException($"Native seated animation '{SittingClipName}' is unavailable.");

        var sourceRoot = character.transform;
        driverRoot = new GameObject("BigfootMonsterTruck_SeatedPlayer");
        driverRoot.SetActive(false);
        driverRoot.layer = sourceRoot.gameObject.layer;
        driverRoot.transform.SetParent(vehicle.transform, false);
        driverRoot.transform.localScale = Vector3.one * SeatedScale;

        var transforms = new Dictionary<Transform, Transform>();
        CopyTransforms(sourceRoot, driverRoot.transform, transforms);
        var lowerDetailRenderers = GetLowerDetailRenderers(appearance.transform);
        var rendererCount = 0;
        foreach (var source in appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!source.enabled || source.sharedMesh == null || source.forceRenderingOff ||
                source.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                lowerDetailRenderers.Contains(source) ||
                !IsActiveWithinCharacter(source.transform, sourceRoot))
                continue;
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

        driverRoot.SetActive(true);
        poseGraph = PlayableGraph.Create("Bigfoot Monster Truck seated player");
        poseGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        pose = AnimationClipPlayable.Create(poseGraph, sittingClip);
        pose.SetApplyFootIK(false);
        pose.SetApplyPlayableIK(false);
        AnimationPlayableOutput.Create(poseGraph, "Seated pose", animator).SetSourcePlayable(pose);
        poseLength = sittingClip.length;
        poseTime = 0f;
        poseGraph.Play();
        poseGraph.Evaluate(0f);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
            throw new InvalidOperationException("Seated avatar has no humanoid hips bone.");

        AlignWithSeat();
        LogInfo(
            $"created centered driver from current appearance; renderers={rendererCount}, " +
            $"seat={vehicle.transform.InverseTransformPoint(seatAnchor.position).ToString("F3")}.");
    }

    private void AlignWithSeat()
    {
        if (driverRoot == null || hips == null || seatAnchor == null || vehicle == null)
            return;
        driverRoot.transform.rotation = vehicle.transform.rotation;
        driverRoot.transform.position += seatAnchor.position - hips.position;
    }

    private void CopyRenderer(
        SkinnedMeshRenderer source,
        Dictionary<Transform, Transform> transforms)
    {
        var destination = transforms[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
        var mesh = Instantiate(source.sharedMesh);
        ownedAssets.Add(mesh);
        destination.sharedMesh = mesh;
        var bones = new Transform[source.bones.Length];
        for (var index = 0; index < bones.Length; index++)
        {
            if (source.bones[index] == null || !transforms.TryGetValue(source.bones[index], out bones[index]))
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

        var materials = new Material[source.sharedMaterials.Length];
        for (var index = 0; index < materials.Length; index++)
        {
            if (source.sharedMaterials[index] == null)
                throw new InvalidOperationException(
                    $"Appearance mesh '{source.name}' has a missing material.");
            materials[index] = new Material(source.sharedMaterials[index]);
            ownedAssets.Add(materials[index]);
        }
        destination.sharedMaterials = materials;
        for (var index = 0; index < mesh.blendShapeCount; index++)
            destination.SetBlendShapeWeight(index, source.GetBlendShapeWeight(index));
        destination.localBounds = source.localBounds;
        destination.updateWhenOffscreen = true;
        destination.quality = source.quality;
        destination.renderingLayerMask = source.renderingLayerMask;
        destination.shadowCastingMode = ShadowCastingMode.Off;
        destination.receiveShadows = source.receiveShadows;
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

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static bool IsActiveWithinCharacter(Transform child, Transform root)
    {
        for (var current = child; current != root; current = current.parent)
            if (current == null || !current.gameObject.activeSelf)
                return false;
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
            var highest = new HashSet<Renderer>(lods[0].renderers);
            for (var level = 1; level < lods.Length; level++)
                foreach (var renderer in lods[level].renderers)
                    if (renderer != null && !highest.Contains(renderer))
                        result.Add(renderer);
        }
        return result;
    }

    private void LogInfo(string message) =>
        context?.Logger.Info(
            $"BigfootMonsterTruck driver vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RemoveDriver()
    {
        if (poseGraph.IsValid())
            poseGraph.Destroy();
        if (driverRoot != null)
        {
            driverRoot.SetActive(false);
            Destroy(driverRoot);
        }
        driverRoot = null;
        hips = null;
        foreach (var asset in ownedAssets)
            if (asset != null)
                Destroy(asset);
        ownedAssets.Clear();
    }

    private void OnDisable()
    {
        RemoveDriver();
        occupied = false;
    }

    private void OnDestroy() => RemoveDriver();
}
