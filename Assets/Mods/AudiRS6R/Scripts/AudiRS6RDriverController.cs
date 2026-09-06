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
internal sealed class AudiRS6RDriverController : MonoBehaviour
{
    private const string SteeringWheelName = "Animate_SteeringWheel_033";
    private const string SittingClipName = "SitDeliveryTruck";
    private const float SeatedScale = 0.94f;
    private const float HandHalfSpacing = 0.16f;
    // Pelvis position relative to the Audi's steering-wheel pivot, in vehicle axes.
    private static readonly Vector3 SeatOffset = new(0f, -0.28f, -0.48f);
    private const int MaximumAttempts = 20;
    private readonly List<UnityEngine.Object> ownedAssets = new();
    private VehicleController? vehicle;
    private ModContext? context;
    private GameObject? driverRoot;
    private Transform? hips;
    private Transform? steeringWheel;
    private SeatedArm? leftArm;
    private SeatedArm? rightArm;
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
                RemoveDriver();
                LogInfo("exited; seated model removed.");
            }
            else
            {
                LogInfo("occupied; preparing current player appearance.");
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

            // Evaluate only the native sitting clip, without player controller scripts,
            // animation events, navigation, colliders or animator state behaviours.
            poseTime = Mathf.Repeat(poseTime + Time.deltaTime, poseLength);
            pose.SetTime(poseTime);
            leftArm?.RestoreAnimationPose();
            rightArm?.RestoreAnimationPose();
            poseGraph.Evaluate(0f);
            AlignWithSeat();
            AlignHandsWithWheel();
        }
        catch (Exception ex)
        {
            RemoveDriver();
            var reason = ex.GetBaseException().Message;
            if (reason != lastFailure || attempts >= MaximumAttempts)
            {
                lastFailure = reason;
                context?.Logger.Warn($"AudiRS6R driver vehicle={vehicle.GetInstanceID()} " +
                                     $"attempt={attempts}/{MaximumAttempts}: {reason}");
            }
        }
    }

    private void CreateDriver()
    {
        var character = PlayerHelper.PlayerController?.Character;
        var appearance = character?.appearanceSetter;
        if (character == null || appearance == null)
            throw new InvalidOperationException("Player appearance is not ready.");

        var sourceAnimator = typeof(AppearanceSetter).GetField("animator",
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

        steeringWheel = null;
        foreach (var child in vehicle!.GetComponentsInChildren<Transform>(true))
            if (child.name == SteeringWheelName)
                steeringWheel = child;
        if (steeringWheel == null)
            throw new InvalidOperationException("Audi steering-wheel seat reference is missing.");

        var sourceRoot = character.transform;
        if (!sourceAnimator.transform.IsChildOf(sourceRoot) && sourceAnimator.transform != sourceRoot)
            throw new InvalidOperationException("Player animator is outside the character hierarchy.");

        driverRoot = new GameObject("AudiRS6R_SeatedPlayer");
        driverRoot.SetActive(false);
        driverRoot.layer = sourceRoot.gameObject.layer;
        driverRoot.transform.SetParent(vehicle.transform, false);
        var transforms = new Dictionary<Transform, Transform>();
        CopyTransforms(sourceRoot, driverRoot.transform, transforms);
        // EnterVehicle hides the real character by setting its root scale to zero.
        // Scale around the anchored hips, preserving the tested seat height.
        driverRoot.transform.localScale = Vector3.one * SeatedScale;
        driverRoot.transform.localRotation = Quaternion.identity;
        driverRoot.transform.localPosition = Vector3.zero;

        var rendererCount = 0;
        var suppressedCount = 0;
        var lowerDetailRenderers = GetLowerDetailRenderers(appearance.transform);
        foreach (var source in appearance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!source.enabled || source.sharedMesh == null || !IsActiveWithinCharacter(source.transform, sourceRoot))
                continue;
            // enabled/activeSelf do not include shadow-only, forced-hidden or LOD
            // visibility. Drawing those as ordinary meshes can overlap the body.
            if (source.forceRenderingOff || source.shadowCastingMode == ShadowCastingMode.ShadowsOnly ||
                lowerDetailRenderers.Contains(source))
            {
                suppressedCount++;
                LogInfo($"skipped mesh='{source.name}' forceRenderingOff={source.forceRenderingOff} " +
                        $"shadowMode={source.shadowCastingMode} lowerDetail={lowerDetailRenderers.Contains(source)}.");
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
        driverRoot.SetActive(true);
        poseGraph = PlayableGraph.Create("AudiRS6R seated player");
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
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftArm = CreateArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand);
        rightArm = CreateArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand);
        var originalLeftHand = VehiclePosition(leftHand);
        var originalRightHand = VehiclePosition(rightHand);
        AlignHandsWithWheel();
        LogInfo($"hand alignment halfSpacing={HandHalfSpacing:F3} " +
                $"leftBefore={originalLeftHand} leftAfter={VehiclePosition(leftHand)} " +
                $"rightBefore={originalRightHand} rightAfter={VehiclePosition(rightHand)}.");
        LogInfo($"created from current player appearance; renderers={rendererCount} " +
                $"suppressedRenderers={suppressedCount} scale={SeatedScale:F2} " +
                $"transforms={transforms.Count} clip='{sittingClip.name}' " +
                $"hips={VehiclePosition(hips)} head={VehiclePosition(head)} " +
                $"leftHand={VehiclePosition(leftHand)} rightHand={VehiclePosition(rightHand)} " +
                $"steeringWheel={VehiclePosition(steeringWheel)}.");
    }

    private void AlignWithSeat()
    {
        if (driverRoot == null || hips == null || steeringWheel == null || vehicle == null)
            return;
        driverRoot.transform.rotation = vehicle.transform.rotation;
        var seatPosition = steeringWheel.position + vehicle.transform.TransformVector(SeatOffset);
        driverRoot.transform.position += seatPosition - hips.position;
    }

    private SeatedArm? CreateArm(Animator animator, HumanBodyBones upperBone,
        HumanBodyBones lowerBone, HumanBodyBones handBone)
    {
        var upper = animator.GetBoneTransform(upperBone);
        var lower = animator.GetBoneTransform(lowerBone);
        var hand = animator.GetBoneTransform(handBone);
        if (upper != null && lower != null && hand != null)
            return new SeatedArm(upper, lower, hand);
        context?.Logger.Warn($"AudiRS6R driver vehicle={vehicle?.GetInstanceID()}: " +
                             $"cannot refine {handBone}; arm bones are missing. Keeping native pose.");
        return null;
    }

    private void AlignHandsWithWheel()
    {
        if (vehicle == null || steeringWheel == null)
            return;
        var centerX = vehicle.transform.InverseTransformPoint(steeringWheel.position).x;
        AlignHand(leftArm, centerX - HandHalfSpacing);
        AlignHand(rightArm, centerX + HandHalfSpacing);
    }

    private void AlignHand(SeatedArm? arm, float targetX)
    {
        if (arm == null || vehicle == null)
            return;
        var target = vehicle.transform.InverseTransformPoint(arm.Hand.position);
        target.x = targetX;
        arm.AimAt(vehicle.transform.TransformPoint(target), vehicle.transform.forward);
    }

    private sealed class SeatedArm
    {
        private readonly Transform upper;
        private readonly Transform lower;
        public readonly Transform Hand;
        private Quaternion upperPose;
        private Quaternion lowerPose;
        private Quaternion handPose;
        private bool hasAdjustment;

        public SeatedArm(Transform upper, Transform lower, Transform hand)
        {
            this.upper = upper;
            this.lower = lower;
            Hand = hand;
        }

        public void RestoreAnimationPose()
        {
            if (!hasAdjustment)
                return;
            upper.localRotation = upperPose;
            lower.localRotation = lowerPose;
            Hand.localRotation = handPose;
            hasAdjustment = false;
        }

        public void AimAt(Vector3 target, Vector3 fallbackDirection)
        {
            // Retain the native elbow bend and grip orientation. Only rotate bones;
            // do not stretch the mesh or move the shoulder/torso.
            if (!TrySolveElbow(upper.position, lower.position, Hand.position, target,
                    fallbackDirection, out var elbow, out var reachableTarget))
                return;
            upperPose = upper.localRotation;
            lowerPose = lower.localRotation;
            handPose = Hand.localRotation;
            hasAdjustment = true;
            var gripRotation = Hand.rotation;
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position,
                elbow - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(Hand.position - lower.position,
                reachableTarget - lower.position) * lower.rotation;
            Hand.rotation = gripRotation;
        }
    }

    private static bool TrySolveElbow(Vector3 shoulder, Vector3 elbow, Vector3 hand,
        Vector3 target, Vector3 fallbackDirection, out Vector3 solvedElbow, out Vector3 reachableTarget)
    {
        solvedElbow = elbow;
        reachableTarget = hand;
        var upperLength = Vector3.Distance(shoulder, elbow);
        var lowerLength = Vector3.Distance(elbow, hand);
        var reach = target - shoulder;
        if (upperLength < 0.0001f || lowerLength < 0.0001f || reach.sqrMagnitude < 0.000001f)
            return false;
        var direction = reach.normalized;
        var distance = Mathf.Clamp(reach.magnitude,
            Mathf.Abs(upperLength - lowerLength) + 0.0001f, upperLength + lowerLength - 0.0001f);
        var bend = Vector3.ProjectOnPlane(elbow - shoulder, direction);
        if (bend.sqrMagnitude < 0.000001f)
            bend = Vector3.ProjectOnPlane(fallbackDirection, direction);
        if (bend.sqrMagnitude < 0.000001f)
            return false;
        var along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                    (2f * distance);
        var across = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        solvedElbow = shoulder + direction * along + bend.normalized * across;
        reachableTarget = shoulder + direction * distance;
        return true;
    }

    private static bool IsActiveWithinCharacter(Transform child, Transform root)
    {
        // Ignore the hidden character root, but retain clothing/gender selection beneath it.
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
        // The visual copy has no LODGroup. Retain one complete, highest-detail
        // representation instead of drawing all of its LODs at the same time.
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

    private static void CopyTransforms(Transform source, Transform destination,
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

    private void CopyRenderer(SkinnedMeshRenderer source, Dictionary<Transform, Transform> transforms)
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
                throw new InvalidOperationException($"Appearance mesh '{source.name}' has an unmapped bone at {index}.");
        }
        destination.bones = bones;
        if (source.rootBone != null)
        {
            if (!transforms.TryGetValue(source.rootBone, out var rootBone))
                throw new InvalidOperationException($"Appearance mesh '{source.name}' has an unmapped root bone.");
            destination.rootBone = rootBone;
        }
        var sourceMaterials = source.sharedMaterials;
        var materials = new Material[sourceMaterials.Length];
        for (var index = 0; index < sourceMaterials.Length; index++)
        {
            if (sourceMaterials[index] == null)
                throw new InvalidOperationException($"Appearance mesh '{source.name}' has a missing material.");
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
        LogInfo($"copied mesh='{source.name}' vertices={mesh.vertexCount} " +
                $"blendShapes={mesh.blendShapeCount} materials={materials.Length} " +
                $"sourceShadowMode={source.shadowCastingMode} receiveShadows={source.receiveShadows} " +
                $"renderingLayerMask={source.renderingLayerMask}.");
    }

    private string VehiclePosition(Transform? target) =>
        target != null && vehicle != null ? vehicle.transform.InverseTransformPoint(target.position).ToString("F3") : "missing";

    private void LogInfo(string message) =>
        context?.Logger.Info($"AudiRS6R driver vehicle={vehicle?.GetInstanceID()}: {message}");

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
        leftArm = null;
        rightArm = null;
        foreach (var asset in ownedAssets)
            if (asset != null) Destroy(asset);
        ownedAssets.Clear();
    }

    private void OnDisable()
    {
        RemoveDriver();
        occupied = false;
    }

    private void OnDestroy() => RemoveDriver();
}
