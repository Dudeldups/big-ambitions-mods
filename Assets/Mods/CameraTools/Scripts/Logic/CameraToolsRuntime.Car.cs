#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private void ApplyVehicleTweaks(bool gameplayActive)
        {
            if (settings == null)
                return;

            var rawScrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(rawScrollDelta) <= Mathf.Epsilon)
                rawScrollDelta = Input.GetAxis("Mouse ScrollWheel") * 120f;

            if (gameManagerController != null && (cachedVehicleCameras == null || cachedVehicleCameras.Length == 0))
                cachedVehicleCameras = ResolveVehicleCameras(gameManagerController);

            if (cachedVehicleCameras != null && cachedVehicleCameras.Length > 0 && !AreAllVehicleCamerasAlive(cachedVehicleCameras))
            {
                InvalidateVehicleCameraCaches();
                cachedVehicleCameras = gameManagerController == null ? Array.Empty<Component>() : ResolveVehicleCameras(gameManagerController);
            }

            activeVehicleCameraRoot = GetLiveVehicleCameraRoot(cachedVehicleCameras ?? Array.Empty<Component>());
            var hasActiveVehicleCamera = activeVehicleCameraRoot != null;
            var debugHotkeyPressed = cameraToolsDebugEnabled && (Input.GetKeyDown(KeyCode.F9) || Input.GetKeyDown(KeyCode.F10));
            var wantsVehicleWork =
                (cameraToolsDebugEnabled && showVehicleDebugOverlay) ||
                debugHotkeyPressed ||
                (Mathf.Abs(rawScrollDelta) > Mathf.Epsilon && hasActiveVehicleCamera);

            if (!gameplayActive)
            {
                ResetVehicleRuntimeState();
                return;
            }

            if (!hasActiveVehicleCamera)
            {
                ResetVehicleRuntimeState();
                return;
            }

            if (vehicleTarget == null || !IsVehicleTargetStillValid(vehicleTarget))
                ResolveVehicleTarget(forceSearch: false, allowExpensiveSearch: true);
            else if (cameraToolsDebugEnabled && wantsVehicleWork)
                ResolveVehicleTarget(forceSearch: false, allowExpensiveSearch: true);
            else if (cameraToolsDebugEnabled)
                UpdateVehicleDebugStateFromTarget();

            if (float.IsNaN(desiredVehicleDistance))
            {
                desiredVehicleDistance = ResolveCurrentVehicleCameraDistance(settings.VehicleMaxZoom);
                if (cameraToolsDebugEnabled)
                    vehicleDebug.CurrentDistance = desiredVehicleDistance;
            }

            var supportsVehiclePitch = IsVehiclePitchCameraRoot(activeVehicleCameraRoot);
            if (supportsVehiclePitch)
                HandleVehiclePitchInput(settings.VehicleMaxZoom);
            else
                ResetVehiclePitchTracking();

            var scrollDelta = ReadVehicleScrollDelta(rawScrollDelta);
            if (cameraToolsDebugEnabled)
                vehicleDebug.ScrollDelta = scrollDelta;
            if (Mathf.Abs(scrollDelta) > Mathf.Epsilon)
            {
                if (IsGameplayInputBlockedByUi())
                    return;

                var nextDistance = Mathf.Clamp(
                    desiredVehicleDistance - scrollDelta * VehicleZoomStepPerScrollTick,
                    VehicleMinimumZoom,
                    settings.VehicleMaxZoom);
                ApplyVehicleDistance(nextDistance, "scroll");
            }
            else if (needsVehicleDistanceReapply && cameraToolsDebugEnabled)
            {
                ReapplyVehicleDistanceIfNeeded();
            }

            ApplyVehicleZoomLimits(activeVehicleCameraRoot);
        }

        private void HandleVehiclePitchInput(float maxZoom)
        {
            var vehicleCameraRoot = activeVehicleCameraRoot;
            if (vehicleCameraRoot == null)
                return;

            EnsureVehicleCameraOrientationInitialized(vehicleCameraRoot);

            if (Input.GetMouseButtonDown(1))
            {
                isTrackingVehicleRightMousePitch = true;
                lastVehicleRightMouseX = Input.mousePosition.x;
                lastVehicleRightMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(1) && isTrackingVehicleRightMousePitch)
            {
                if (IsGameplayInputBlockedByUi())
                    return;

                var currentMouseX = Input.mousePosition.x;
                var currentMouseY = Input.mousePosition.y;
                var deltaX = currentMouseX - lastVehicleRightMouseX;
                var deltaY = currentMouseY - lastVehicleRightMouseY;
                lastVehicleRightMouseX = currentMouseX;
                lastVehicleRightMouseY = currentMouseY;

                if (Mathf.Abs(deltaX) > Mathf.Epsilon || Mathf.Abs(deltaY) > Mathf.Epsilon)
                {
                    if (Mathf.Abs(deltaX) > Mathf.Epsilon)
                    {
                        manualVehicleYaw = Mathf.Repeat(manualVehicleYaw + deltaX * VehicleYawStepPerMousePixel + 180f, 360f) - 180f;
                        hasManualVehicleYaw = true;
                    }

                    manualVehiclePitch = Mathf.Clamp(manualVehiclePitch - deltaY * PitchStepPerMousePixel, 1f, 89f);
                    hasManualVehiclePitch = true;
                    ApplyVehicleDistance(GetCurrentVehicleZoomDistance(maxZoom), "pitch-yaw");
                }
            }

            if (Input.GetMouseButtonUp(1))
                isTrackingVehicleRightMousePitch = false;
            else if (!Input.GetMouseButton(1) && ShouldAutoResetVehicleYaw())
                AutoResetVehicleYaw(maxZoom);

            if (Input.GetKeyDown(KeyCode.Home))
            {
                manualVehiclePitch = GetVehicleDefaultPitch(vehicleCameraRoot);
                manualVehicleYaw = 0f;
                hasManualVehiclePitch = true;
                hasManualVehicleYaw = false;
                ApplyVehicleDistance(GetCurrentVehicleZoomDistance(maxZoom), "pitch-reset");
            }
        }

        private void EnsureVehicleCameraOrientationInitialized(Component vehicleCameraRoot)
        {
            if (!hasManualVehiclePitch)
                manualVehiclePitch = GetVehicleCurrentPitch(vehicleCameraRoot);

            if (!hasManualVehicleYaw)
                manualVehicleYaw = 0f;
        }

        private float GetVehicleCurrentPitch(Component vehicleCameraRoot)
        {
            if (TryGetVehicleCameraOffsetDebugInfo(vehicleCameraRoot.gameObject, out var currentOffset, out _))
                return GetPitchDegreesFromOffset(currentOffset);

            return GetVehicleDefaultPitch(vehicleCameraRoot);
        }

        private float GetVehicleDefaultPitch(Component vehicleCameraRoot)
        {
            if (TryGetVehicleCameraOffsetDebugInfo(vehicleCameraRoot.gameObject, out _, out var originalOffset))
                return GetPitchDegreesFromOffset(originalOffset);

            return 45f;
        }

        private void ResetVehiclePitchTracking()
        {
            isTrackingVehicleRightMousePitch = false;
            lastVehicleRightMouseX = 0f;
            lastVehicleRightMouseY = 0f;
        }

        private bool ShouldAutoResetVehicleYaw()
        {
            return hasManualVehicleYaw && IsVehicleMoving();
        }

        private void AutoResetVehicleYaw(float maxZoom)
        {
            var nextYaw = Mathf.MoveTowardsAngle(
                manualVehicleYaw,
                0f,
                VehicleYawResetSpeedDegreesPerSecond * Time.unscaledDeltaTime);

            if (Mathf.Abs(Mathf.DeltaAngle(manualVehicleYaw, nextYaw)) <= Mathf.Epsilon)
                return;

            manualVehicleYaw = nextYaw;
            if (Mathf.Abs(Mathf.DeltaAngle(manualVehicleYaw, 0f)) <= 0.1f)
            {
                manualVehicleYaw = 0f;
                hasManualVehicleYaw = false;
            }
            else
            {
                hasManualVehicleYaw = true;
            }

            ApplyVehicleDistance(GetCurrentVehicleZoomDistance(maxZoom), "yaw-auto-reset");
        }

        private void HandleVehicleDebugHotkeys()
        {
            if (!cameraToolsDebugEnabled)
                return;

            if (Input.GetKeyDown(KeyCode.F8))
            {
                showVehicleDebugOverlay = !showVehicleDebugOverlay;
                LogVehicleDebug($"Vehicle debug overlay toggled: {showVehicleDebugOverlay}");
            }

            if (settings == null)
                return;

            if (Input.GetKeyDown(KeyCode.F9))
                ApplyVehicleDistance(
                    Mathf.Max(VehicleMinimumZoom, GetCurrentVehicleZoomDistance(settings.VehicleMaxZoom) - VehicleForcedZoomStep),
                    "hotkey-zoom-in");

            if (Input.GetKeyDown(KeyCode.F10))
                ApplyVehicleDistance(Mathf.Min(settings.VehicleMaxZoom, GetCurrentVehicleZoomDistance(settings.VehicleMaxZoom) + VehicleForcedZoomStep), "hotkey-zoom-out");

            if (Input.GetKeyDown(KeyCode.F11))
                DumpVehicleDiagnostics();

            if (Input.GetKeyDown(KeyCode.F12))
                ApplyVisualCameraDiagnostic();
        }

        private void ResolveVehicleTarget(bool forceSearch, bool allowExpensiveSearch)
        {
            if (!forceSearch && vehicleTarget != null && IsVehicleTargetStillValid(vehicleTarget))
            {
                UpdateVehicleDebugStateFromTarget();
                return;
            }

            if (!allowExpensiveSearch)
            {
                UpdateVehicleDebugStateFromTarget();
                return;
            }

            if (!forceSearch && Time.unscaledTime - lastVehicleControllerSearchTime < VehicleSearchIntervalSeconds)
            {
                UpdateVehicleDebugStateFromTarget();
                return;
            }

            lastVehicleControllerSearchTime = Time.unscaledTime;
            vehicleTarget = null;

            var resolvedCarController = ResolveActiveCarControllerCandidate();
            var resolvedVehicleController = ResolveVehicleControllerCandidate(resolvedCarController);

            vehicleTarget = new VehicleTarget(resolvedCarController, resolvedVehicleController);

            vehicleDebug.ActiveCarFound = resolvedCarController != null;
            vehicleDebug.CarControllerTypeName = resolvedCarController == null ? "none" : resolvedCarController.GetType().FullName ?? resolvedCarController.GetType().Name;
            vehicleDebug.VehicleControllerFound = resolvedVehicleController != null;
            vehicleDebug.VehicleControllerTypeName = resolvedVehicleController == null ? "none" : resolvedVehicleController.GetType().FullName ?? resolvedVehicleController.GetType().Name;

            var newSummary =
                $"carFound={vehicleDebug.ActiveCarFound}, carType={vehicleDebug.CarControllerTypeName}, " +
                $"vehicleControllerFound={vehicleDebug.VehicleControllerFound}, vehicleControllerType={vehicleDebug.VehicleControllerTypeName}";
            if (vehicleDebug.LastResolutionSummary != newSummary)
            {
                vehicleDebug.LastResolutionSummary = newSummary;
                LogVehicleDebug("Vehicle target resolved. " + newSummary);
            }
        }

        private MonoBehaviour? ResolveActiveCarControllerCandidate()
        {
            if (carControllerType != null)
            {
                var typedCandidate = FindBestVehicleControllerBehaviour(carControllerType);
                if (typedCandidate != null)
                    return typedCandidate;
            }

            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.gameObject == null || behaviour.gameObject.hideFlags != HideFlags.None)
                    continue;

                var type = behaviour.GetType();
                if (!TypeNameMatches(type, CarControllerTypeName))
                    continue;

                if (LooksLikePlayerControlledVehicle(behaviour))
                    return behaviour;
            }

            return null;
        }

        private static MonoBehaviour? FindBestVehicleControllerBehaviour(Type type)
        {
            MonoBehaviour? fallback = null;

            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                var behaviour = obj as MonoBehaviour;
                if (behaviour == null || behaviour.gameObject == null || behaviour.gameObject.hideFlags != HideFlags.None)
                    continue;

                if (TryGetBoolMember(behaviour, "controlledByPlayer", out var controlledByPlayer) && controlledByPlayer)
                    return behaviour;

                if (fallback == null && behaviour.isActiveAndEnabled)
                    fallback = behaviour;
            }

            return fallback;
        }

        private object? ResolveVehicleControllerCandidate(MonoBehaviour? activeCarController)
        {
            if (activeCarController != null)
            {
                foreach (var memberName in VehicleControllerMemberNames)
                {
                    if (TryGetMemberValue(activeCarController, memberName, out var memberValue) && memberValue != null)
                        return memberValue;
                }
            }

            if (vehicleControllerType != null)
            {
                var typedCandidate = FindBestVehicleControllerObject(vehicleControllerType);
                if (typedCandidate != null)
                    return typedCandidate;
            }

            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.gameObject == null || behaviour.gameObject.hideFlags != HideFlags.None)
                    continue;

                var type = behaviour.GetType();
                if (!TypeNameMatches(type, VehicleControllerTypeName))
                    continue;

                if (IsInsideVehicle(behaviour))
                    return behaviour;
            }

            return null;
        }

        private static object? FindBestVehicleControllerObject(Type type)
        {
            object? fallback = null;
            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                if (obj == null)
                    continue;

                if (IsInsideVehicle(obj))
                    return obj;

                fallback ??= obj;
            }

            return fallback;
        }

        private static bool LooksLikePlayerControlledVehicle(MonoBehaviour behaviour)
        {
            if (TryGetBoolMember(behaviour, "controlledByPlayer", out var controlledByPlayer) && controlledByPlayer)
                return true;

            foreach (var memberName in VehicleControllerMemberNames)
            {
                if (TryGetMemberValue(behaviour, memberName, out var memberValue) && memberValue != null && IsInsideVehicle(memberValue))
                    return true;
            }

            return false;
        }

        private static bool IsVehicleTargetStillValid(VehicleTarget target)
        {
            if (target.CarController is MonoBehaviour carController &&
                !carController.isActiveAndEnabled &&
                !IsInsideVehicle(target.VehicleController))
                return false;

            return target.VehicleController != null || target.CarController != null;
        }

        private void UpdateVehicleDebugStateFromTarget()
        {
            vehicleDebug.IsInsideVehicle = ComputeInsideVehicleState();
            vehicleDebug.IsVehicleMode = activeVehicleCameraRoot != null || vehicleDebug.IsInsideVehicle;
            vehicleDebug.CameraObjectFound = cachedVehicleCameras != null && cachedVehicleCameras.Length > 0;
            if (cameraToolsDebugEnabled && settings != null)
            {
                vehicleDebug.ActualDistance = ResolveCurrentVehicleCameraDistance(settings.VehicleMaxZoom);
                UpdateActiveVehicleOffsetDebugState();
            }
        }

        private float ReadVehicleScrollDelta(float scrollDelta)
        {
            if (Mathf.Abs(scrollDelta) > Mathf.Epsilon)
                LogVehicleDebug($"Vehicle scroll detected: delta={scrollDelta:0.###}, insideVehicle={vehicleDebug.IsInsideVehicle}");

            return scrollDelta;
        }

        private float ResolveCurrentVehicleCameraDistance(float maxZoom)
        {
            var activeCameraRoot = activeVehicleCameraRoot;
            if (activeCameraRoot != null &&
                TryGetVehicleCameraBodyDistance(activeCameraRoot.gameObject, out var distance, out var memberName))
            {
                if (cameraToolsDebugEnabled)
                    vehicleDebug.DistanceMemberName = memberName;
                var clampedDistance = Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom);
                if (cameraToolsDebugEnabled)
                    vehicleDebug.ActualDistance = clampedDistance;
                return clampedDistance;
            }

            if (activeCameraRoot != null &&
                TryGetVehicleCameraDistance(activeCameraRoot.gameObject, out var fallbackDistance))
                return Mathf.Clamp(fallbackDistance, VehicleMinimumZoom, maxZoom);

            if (cameraToolsDebugEnabled)
                vehicleDebug.DistanceMemberName = "not-found";
            return Mathf.Clamp(maxZoom, VehicleMinimumZoom, maxZoom);
        }

        private float GetCurrentVehicleZoomDistance(float maxZoom)
        {
            if (!float.IsNaN(desiredVehicleDistance))
                return desiredVehicleDistance;

            return ResolveCurrentVehicleCameraDistance(maxZoom);
        }

        private void UpdateActiveVehicleOffsetDebugState()
        {
            vehicleDebug.OriginalFollowOffset = "n/a";
            vehicleDebug.CurrentFollowOffset = "n/a";

            var vehicleCamera = activeVehicleCameraRoot;
            if (vehicleCamera == null)
                return;

            if (!TryGetVehicleCameraOffsetDebugInfo(vehicleCamera.gameObject, out var currentOffset, out var originalOffset))
                return;

            vehicleDebug.CurrentFollowOffset = currentOffset.ToString("F2");
            vehicleDebug.OriginalFollowOffset = originalOffset.ToString("F2");
        }

        private bool TryGetVehicleDistanceValue(out float value, out string memberName)
        {
            value = 0f;
            memberName = "none";

            if (TryGetActiveVehicleBodyDistance(out value, out memberName))
                return true;

            if (vehicleTarget?.VehicleController != null &&
                TryGetFirstFloatMember(vehicleTarget.VehicleController, VehicleDistanceMemberNames, out value, out memberName))
                return true;

            if (vehicleTarget?.CarController != null &&
                TryGetFirstFloatMember(vehicleTarget.CarController, VehicleDistanceMemberNames, out value, out memberName))
                return true;

            if (TryGetActiveVehicleCameraDistance(cachedVehicleCameras ?? Array.Empty<Component>(), out value))
            {
                memberName = "camera-component";
                return true;
            }

            return false;
        }

        private void ApplyVehicleDistance(float targetDistance, string reason)
        {
            if (settings == null)
                return;

            var clampedDistance = Mathf.Clamp(targetDistance, VehicleMinimumZoom, settings.VehicleMaxZoom);
            desiredVehicleDistance = clampedDistance;
            var cameraValueApplied = ApplyVehicleDistanceToCameras(clampedDistance, settings.VehicleMaxZoom);

            if (!cameraToolsDebugEnabled)
            {
                needsVehicleDistanceReapply = false;
                return;
            }

            if (vehicleTarget == null || !IsVehicleTargetStillValid(vehicleTarget))
                ResolveVehicleTarget(forceSearch: true, allowExpensiveSearch: true);

            UpdateVehicleDebugStateFromTarget();

            var oldDistance = ResolveCurrentVehicleCameraDistance(settings.VehicleMaxZoom);
            vehicleDebug.CurrentDistance = oldDistance;
            vehicleDebug.LastAppliedDistance = clampedDistance;
            vehicleDebug.CinemachineFound = cameraValueApplied;

            var readBackDistance = ResolveCurrentVehicleCameraDistance(settings.VehicleMaxZoom);
            vehicleDebug.WasOverwritten = Mathf.Abs(readBackDistance - clampedDistance) > VehicleOverwriteThreshold;
            needsVehicleDistanceReapply = vehicleDebug.WasOverwritten;

            var applySummary =
                $"Vehicle zoom apply ({reason}). insideVehicle={vehicleDebug.IsInsideVehicle}, activeCarFound={vehicleDebug.ActiveCarFound}, " +
                $"carType={vehicleDebug.CarControllerTypeName}, vehicleControllerFound={vehicleDebug.VehicleControllerFound}, " +
                $"distanceMember={vehicleDebug.DistanceMemberName}, oldDistance={oldDistance:0.##}, newDistance={clampedDistance:0.##}, " +
                $"cameraApplied={cameraValueApplied}, overwritten={vehicleDebug.WasOverwritten}";
            if (reason != "lateupdate-reapply" || vehicleDebug.LastApplySummary != applySummary)
            {
                vehicleDebug.LastApplySummary = applySummary;
                LogVehicleDebug(applySummary);
            }
        }

        private void ReapplyVehicleDistanceIfNeeded()
        {
            if (settings == null || float.IsNaN(desiredVehicleDistance))
                return;

            var currentDistance = ResolveCurrentVehicleCameraDistance(settings.VehicleMaxZoom);
            if (Mathf.Abs(currentDistance - desiredVehicleDistance) <= VehicleOverwriteThreshold)
            {
                needsVehicleDistanceReapply = false;
                return;
            }

            vehicleDebug.WasOverwritten = true;
            var overwriteSummary =
                $"Vehicle zoom overwritten. desired={desiredVehicleDistance:0.##}, actual={currentDistance:0.##}, " +
                $"distanceMember={vehicleDebug.DistanceMemberName}, insideVehicle={vehicleDebug.IsInsideVehicle}";
            if (vehicleDebug.LastOverwriteSummary != overwriteSummary)
            {
                vehicleDebug.LastOverwriteSummary = overwriteSummary;
                LogVehicleDebug(overwriteSummary);
            }
            ApplyVehicleDistance(desiredVehicleDistance, "lateupdate-reapply");
        }

        private void ApplyVehicleZoomLimits(Component? activeCameraRoot)
        {
            if (settings == null || activeCameraRoot == null)
                return;

            var activeVehicleCameraId = activeCameraRoot.gameObject.GetInstanceID();
            if (Mathf.Approximately(lastAppliedVehicleMaxZoom, settings.VehicleMaxZoom) &&
                activeVehicleCameraId == lastActiveVehicleCameraId)
                return;

            ApplyVehicleCameraZoomLimits(activeCameraRoot.gameObject, settings.VehicleMaxZoom);

            lastAppliedVehicleMaxZoom = settings.VehicleMaxZoom;
            lastActiveVehicleCameraId = activeVehicleCameraId;
        }

        private void InvalidateVehicleCameraCaches()
        {
            activeVehicleCameraRoot = null;
            cachedVehicleCameras = null;
            cachedVehicleFollowOffsets.Clear();
            cachedVehiclePipelineComponents.Clear();
            cachedVehicleZoomComponents.Clear();
            lastActiveVehicleCameraId = 0;
            lastAppliedVehicleMaxZoom = float.NaN;
        }

        private void ResetVehicleRuntimeState()
        {
            activeVehicleCameraRoot = null;
            desiredVehicleDistance = float.NaN;
            vehicleTarget = null;
            hasManualVehiclePitch = false;
            hasManualVehicleYaw = false;
            manualVehiclePitch = 0f;
            manualVehicleYaw = 0f;
            ResetVehiclePitchTracking();
            needsVehicleDistanceReapply = false;
            if (!cameraToolsDebugEnabled)
                return;

            vehicleDebug.IsVehicleMode = false;
            vehicleDebug.IsInsideVehicle = false;
            vehicleDebug.CameraObjectFound = false;
        }

        private bool ApplyVehicleDistanceToCameras(float distance, float maxZoom)
        {
            var foundCamera = false;
            var vehicleCamera = activeVehicleCameraRoot;
            if (vehicleCamera != null)
            {
                ApplyVehicleCameraDistance(vehicleCamera.gameObject, distance, maxZoom);
                foundCamera = true;
            }

            return foundCamera;
        }

        private static Component[] ResolveVehicleCameras(Component gameManager)
        {
            var results = new List<Component>();
            foreach (var memberName in VehicleCameraMemberNames)
            {
                if (!TryGetMemberValue(gameManager, memberName, out var value) || value == null)
                    continue;

                if (value is Component component)
                {
                    results.Add(component);
                    continue;
                }

                if (value is GameObject gameObject)
                {
                    var transform = gameObject.transform;
                    if (transform != null)
                        results.Add(transform);
                }
            }

            return results.ToArray();
        }

        private static bool AreAllVehicleCamerasAlive(Component[] vehicleCameras)
        {
            foreach (var vehicleCamera in vehicleCameras)
            {
                if (vehicleCamera == null)
                    return false;
            }

            return true;
        }

        private Component? GetLiveVehicleCameraRoot(Component[] vehicleCameras)
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            if (liveVirtualCamera == null)
                return null;

            foreach (var vehicleCamera in vehicleCameras)
            {
                if (vehicleCamera == null)
                    continue;

                if (liveVirtualCamera.transform.IsChildOf(vehicleCamera.transform) ||
                    vehicleCamera.transform.IsChildOf(liveVirtualCamera.transform))
                    return vehicleCamera;
            }

            return null;
        }

        private Component? GetLiveVirtualCameraComponent()
        {
            var brainType = cinemachineBrainType;
            var mainCamera = Camera.main;
            if (brainType == null || mainCamera == null)
                return null;

            var brain = mainCamera.GetComponent(brainType);
            if (brain == null)
                return null;

            if (!TryGetMemberValue(brain, "ActiveVirtualCamera", out var activeVirtualCamera) || activeVirtualCamera == null)
                return null;

            return activeVirtualCamera as Component;
        }

        private bool TryGetActiveVehicleCameraDistance(Component[] vehicleCameras, out float distance)
        {
            distance = 0f;
            var vehicleCamera = activeVehicleCameraRoot;
            return vehicleCamera != null &&
                TryGetVehicleCameraDistance(vehicleCamera.gameObject, out distance);
        }

        private bool TryGetActiveVehicleBodyDistance(out float distance, out string memberName)
        {
            distance = 0f;
            memberName = "none";

            var vehicleCamera = activeVehicleCameraRoot;
            return vehicleCamera != null &&
                TryGetVehicleCameraBodyDistance(vehicleCamera.gameObject, out distance, out memberName);
        }

        private bool TryGetVehicleCameraOffsetDebugInfo(GameObject cameraObject, out Vector3 currentOffset, out Vector3 originalOffset)
        {
            currentOffset = default;
            originalOffset = default;
            foreach (var pipelineComponent in GetCachedVehiclePipelineComponents(cameraObject))
            {
                if (pipelineComponent == null || !IsFollowOffsetComponent(pipelineComponent))
                    continue;

                if (!TryGetFollowOffset(pipelineComponent, out currentOffset))
                    continue;

                originalOffset = GetOrCacheOriginalFollowOffset(pipelineComponent, currentOffset);
                return true;
            }

            return false;
        }

        private bool TryGetVehicleCameraDistance(GameObject cameraObject, out float distance)
        {
            distance = 0f;
            foreach (var zoomComponent in GetCachedVehicleZoomComponents(cameraObject))
            {
                if (zoomComponent != null && TryGetFloatMember(zoomComponent, "distance", out distance))
                    return true;
            }

            foreach (var pipelineComponent in GetCachedVehiclePipelineComponents(cameraObject))
            {
                if (pipelineComponent == null)
                    continue;

                var typeName = pipelineComponent.GetType().Name;
                if ((typeName == "CinemachineFramingTransposer" && TryGetFloatMember(pipelineComponent, "m_CameraDistance", out distance)) ||
                    (typeName == "Cinemachine3rdPersonFollow" && TryGetFloatMember(pipelineComponent, "CameraDistance", out distance)))
                    return true;
            }

            return false;
        }

        private bool TryGetVehicleCameraBodyDistance(GameObject cameraObject, out float distance, out string memberName)
        {
            distance = 0f;
            memberName = "none";
            foreach (var pipelineComponent in GetCachedVehiclePipelineComponents(cameraObject))
            {
                if (pipelineComponent == null)
                    continue;

                if (TryGetPipelineComponentDistance(pipelineComponent, out distance, out memberName))
                    return true;
            }

            return false;
        }

        private void ApplyVehicleCameraZoomLimits(GameObject cameraObject, float maxZoom)
        {
            foreach (var zoomComponent in GetCachedVehicleZoomComponents(cameraObject))
            {
                if (zoomComponent == null)
                    continue;

                SetMemberValue(zoomComponent, "minDistance", VehicleMinimumZoom);
                SetMemberValue(zoomComponent, "maxDistance", maxZoom);
            }

            foreach (var pipelineComponent in GetCachedVehiclePipelineComponents(cameraObject))
            {
                if (pipelineComponent == null)
                    continue;
                ApplyPipelineZoomLimits(pipelineComponent, maxZoom);
            }
        }

        private void ApplyVehicleCameraDistance(GameObject cameraObject, float distance, float maxZoom)
        {
            var applyVehiclePitch = IsVehiclePitchCameraObject(cameraObject);
            var targetPitch = Mathf.Clamp(hasManualVehiclePitch ? manualVehiclePitch : GetVehicleDefaultPitchFromObject(cameraObject), 1f, 89f);
            var targetYaw = hasManualVehicleYaw ? manualVehicleYaw : 0f;
            foreach (var zoomComponent in GetCachedVehicleZoomComponents(cameraObject))
            {
                if (zoomComponent == null)
                    continue;

                SetMemberValue(zoomComponent, "minDistance", VehicleMinimumZoom);
                SetMemberValue(zoomComponent, "maxDistance", maxZoom);
                SetMemberValue(zoomComponent, "distance", Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom));
            }

            foreach (var pipelineComponent in GetCachedVehiclePipelineComponents(cameraObject))
            {
                if (pipelineComponent == null)
                    continue;
                ApplyPipelineZoomLimits(pipelineComponent, maxZoom);
                ApplyPipelineDistance(pipelineComponent, distance, maxZoom, applyVehiclePitch, targetPitch, targetYaw);
            }
        }

        private void ApplyPipelineZoomLimits(object pipelineComponent, float maxZoom)
        {
            var typeName = pipelineComponent.GetType().Name;
            if (typeName == "CinemachineFramingTransposer")
            {
                SetMemberValue(pipelineComponent, "m_MinimumDistance", VehicleMinimumZoom);
                SetMemberValue(pipelineComponent, "m_MaximumDistance", maxZoom);
            }
            else if (typeName == "Cinemachine3rdPersonFollow")
            {
                SetMemberValue(pipelineComponent, "CameraDistance", Mathf.Clamp(GetFloatMember(pipelineComponent, "CameraDistance"), VehicleMinimumZoom, maxZoom));
            }
            else if (typeName == "CinemachineTransposer" || typeName == "CinemachineOrbitalTransposer")
            {
                if (TryGetFollowOffset(pipelineComponent, out var followOffset))
                {
                    GetOrCacheOriginalFollowOffset(pipelineComponent, followOffset);
                }
            }
        }

        private void ApplyPipelineDistance(object pipelineComponent, float distance, float maxZoom, bool applyVehiclePitch, float targetPitch, float targetYaw)
        {
            var clampedDistance = Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom);
            var typeName = pipelineComponent.GetType().Name;
            if (typeName == "CinemachineFramingTransposer")
            {
                SetMemberValue(pipelineComponent, "m_CameraDistance", clampedDistance);
            }
            else if (typeName == "Cinemachine3rdPersonFollow")
            {
                SetMemberValue(pipelineComponent, "CameraDistance", clampedDistance);
            }
            else if (typeName == "CinemachineTransposer" || typeName == "CinemachineOrbitalTransposer")
            {
                if (TryGetFollowOffset(pipelineComponent, out var followOffset))
                {
                    var originalOffset = GetOrCacheOriginalFollowOffset(pipelineComponent, followOffset);
                    var originalMagnitude = originalOffset.magnitude;
                    if (originalMagnitude > Mathf.Epsilon)
                    {
                        var scaledOffset = applyVehiclePitch
                            ? BuildVehicleCameraOffset(originalOffset, clampedDistance, targetPitch, targetYaw)
                            : originalOffset * (clampedDistance / originalMagnitude);
                        SetFollowOffset(pipelineComponent, scaledOffset);
                    }
                }
            }
        }

        private static bool IsVehiclePitchCameraRoot(Component? vehicleCameraRoot)
        {
            return vehicleCameraRoot != null && IsVehiclePitchCameraObject(vehicleCameraRoot.gameObject);
        }

        private static bool IsVehiclePitchCameraObject(GameObject? cameraObject)
        {
            if (cameraObject == null)
                return false;

            var path = GetHierarchyPath(cameraObject.transform);
            return path.IndexOf("VehicleCamReverse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (path.IndexOf("VehicleCam", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("Indoor", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private float GetVehicleDefaultPitchFromObject(GameObject cameraObject)
        {
            if (TryGetVehicleCameraOffsetDebugInfo(cameraObject, out _, out var originalOffset))
                return GetPitchDegreesFromOffset(originalOffset);

            return 45f;
        }

        private static float GetPitchDegreesFromOffset(Vector3 offset)
        {
            var magnitude = offset.magnitude;
            if (magnitude <= Mathf.Epsilon)
                return 45f;

            return Mathf.Clamp(Mathf.Asin(Mathf.Clamp(offset.y / magnitude, -1f, 1f)) * Mathf.Rad2Deg, 1f, 89f);
        }

        private static Vector3 BuildVehicleCameraOffset(Vector3 originalOffset, float distance, float pitchDegrees, float yawDegrees)
        {
            var clampedPitch = Mathf.Clamp(pitchDegrees, 1f, 89f) * Mathf.Deg2Rad;
            var targetHeight = distance * Mathf.Sin(clampedPitch);
            var targetHorizontal = Mathf.Max(0f, distance * Mathf.Cos(clampedPitch));
            var originalHorizontal = Mathf.Sqrt(originalOffset.x * originalOffset.x + originalOffset.z * originalOffset.z);
            if (originalHorizontal <= Mathf.Epsilon)
                return new Vector3(0f, targetHeight, 0f);

            var baseHorizontalDirection = new Vector3(originalOffset.x, 0f, originalOffset.z) / originalHorizontal;
            var rotatedHorizontal = Quaternion.AngleAxis(yawDegrees, Vector3.up) * baseHorizontalDirection * targetHorizontal;
            return new Vector3(rotatedHorizontal.x, targetHeight, rotatedHorizontal.z);
        }

        private bool IsVehicleMoving()
        {
            if (TryGetVehicleSpeed(vehicleTarget?.VehicleController, out var speed))
                return speed > VehicleMovingSpeedThreshold;

            if (TryGetVehicleSpeed(vehicleTarget?.CarController, out speed))
                return speed > VehicleMovingSpeedThreshold;

            return false;
        }

        private static bool TryGetVehicleSpeed(object? target, out float speed)
        {
            speed = 0f;
            if (target == null)
                return false;

            if (TryGetFirstFloatMember(target, new[]
                {
                    "speed",
                    "Speed",
                    "currentSpeed",
                    "CurrentSpeed",
                    "vehicleSpeed",
                    "VehicleSpeed",
                    "forwardSpeed",
                    "ForwardSpeed",
                    "velocityMagnitude",
                    "VelocityMagnitude"
                }, out speed, out _))
                return true;

            foreach (var memberName in new[] { "velocity", "Velocity", "linearVelocity", "LinearVelocity" })
            {
                if (!TryGetMemberValue(target, memberName, out var value) || value == null)
                    continue;

                switch (value)
                {
                    case Vector3 vector3:
                        speed = vector3.magnitude;
                        return true;
                    case Vector2 vector2:
                        speed = vector2.magnitude;
                        return true;
                    case Rigidbody rigidbody:
                        speed = rigidbody.velocity.magnitude;
                        return true;
                }
            }

            foreach (var memberName in new[] { "rb", "RB", "_rb", "rigidbody", "Rigidbody" })
            {
                if (!TryGetMemberValue(target, memberName, out var value) || value is not Rigidbody rigidbody)
                    continue;

                speed = rigidbody.velocity.magnitude;
                return true;
            }

            return false;
        }

        private object[] GetCachedVehiclePipelineComponents(GameObject cameraObject)
        {
            if (cameraObject == null)
                return Array.Empty<object>();

            var key = cameraObject.GetInstanceID();
            if (cachedVehiclePipelineComponents.TryGetValue(key, out var cachedComponents))
                return cachedComponents;

            var results = new List<object>();
            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType != null)
            {
                foreach (var vcam in cameraObject.GetComponentsInChildren(virtualCameraType, true))
                {
                    if (vcam == null)
                        continue;

                    var pipeline = GetCinemachinePipeline(virtualCameraType, vcam);
                    if (pipeline == null)
                        continue;

                    foreach (var pipelineComponent in pipeline)
                    {
                        if (pipelineComponent != null)
                            results.Add(pipelineComponent);
                    }
                }
            }

            var components = results.ToArray();
            cachedVehiclePipelineComponents[key] = components;
            return components;
        }

        private object[] GetCachedVehicleZoomComponents(GameObject cameraObject)
        {
            if (cameraObject == null)
                return Array.Empty<object>();

            var key = cameraObject.GetInstanceID();
            if (cachedVehicleZoomComponents.TryGetValue(key, out var cachedComponents))
                return cachedComponents;

            var results = new List<object>();
            foreach (var behaviour in cameraObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("camera", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("zoom", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                results.Add(behaviour);
            }

            var components = results.ToArray();
            cachedVehicleZoomComponents[key] = components;
            return components;
        }

        private static bool TryGetPipelineComponentDistance(object pipelineComponent, out float distance, out string memberName)
        {
            distance = 0f;
            memberName = "none";
            var typeName = pipelineComponent.GetType().Name;

            if (typeName == "CinemachineFramingTransposer" &&
                TryGetFloatMember(pipelineComponent, "m_CameraDistance", out distance))
            {
                memberName = typeName + ".m_CameraDistance";
                return true;
            }

            if (typeName == "Cinemachine3rdPersonFollow" &&
                TryGetFloatMember(pipelineComponent, "CameraDistance", out distance))
            {
                memberName = typeName + ".CameraDistance";
                return true;
            }

            if ((typeName == "CinemachineTransposer" || typeName == "CinemachineOrbitalTransposer") &&
                TryGetFollowOffset(pipelineComponent, out var followOffset))
            {
                distance = followOffset.magnitude;
                memberName = typeName + ".FollowOffset.magnitude";
                return true;
            }

            return false;
        }

        private bool ComputeInsideVehicleState()
        {
            if (vehicleTarget?.CarController != null &&
                TryGetBoolMember(vehicleTarget.CarController, "controlledByPlayer", out var controlledByPlayer) &&
                controlledByPlayer)
                return true;

            if (!cameraToolsDebugEnabled)
                return vehicleTarget?.VehicleController != null && IsInsideVehicle(vehicleTarget.VehicleController);

            if (HasInterestingTruthyVehicleState(vehicleTarget?.CarController))
                return true;

            if (HasInterestingTruthyVehicleState(vehicleTarget?.VehicleController))
                return true;

            if (gameManagerController != null && HasVehicleReferenceOnGameManager(gameManagerController, vehicleTarget?.CarController))
                return true;

            return false;
        }

        private static bool IsInsideVehicle(object? target)
        {
            if (target == null)
                return false;

            if (TryGetBoolMember(target, "CameraInsideVehicle", out var cameraInsideVehicle))
                return cameraInsideVehicle;

            if (TryGetBoolMember(target, "controlledByPlayer", out var controlledByPlayer))
                return controlledByPlayer;

            return false;
        }

        private static bool HasInterestingTruthyVehicleState(object? target)
        {
            if (target == null)
                return false;

            foreach (var member in EnumerateInterestingMembers(target, VehicleStateKeywords))
            {
                if (member.Value is bool boolValue && boolValue)
                    return true;

                if (member.Value != null && !member.MemberType.IsValueType &&
                    (member.Name.IndexOf("driver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     member.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     member.Name.IndexOf("occup", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }

            return false;
        }

        private static bool HasVehicleReferenceOnGameManager(MonoBehaviour gameManager, MonoBehaviour? activeCarController)
        {
            foreach (var member in EnumerateInterestingMembers(gameManager, VehicleStateKeywords))
            {
                if (member.Value == null)
                    continue;

                if (activeCarController != null && ReferenceEquals(member.Value, activeCarController))
                    return true;

                if (member.Name.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    member.Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void DumpVehicleDiagnostics()
        {
            if (!cameraToolsDebugEnabled || !vehicleDebugLoggingEnabled)
                return;

            ResolveVehicleTarget(forceSearch: true, allowExpensiveSearch: true);
            UpdateVehicleDebugStateFromTarget();

            LogVehicleDebug("=== Vehicle diagnostics dump start ===");
            LogVehicleDebug($"insideVehicle={vehicleDebug.IsInsideVehicle}, vehicleMode={vehicleDebug.IsVehicleMode}, actualDistance={vehicleDebug.ActualDistance:0.##}, desiredDistance={desiredVehicleDistance:0.##}");

            DumpInterestingMembers("CarController state", vehicleTarget?.CarController, VehicleStateKeywords);
            DumpInterestingMembers("VehicleController state", vehicleTarget?.VehicleController, VehicleStateKeywords);
            DumpInterestingMembers("CarController camera members", vehicleTarget?.CarController, VehicleCameraKeywords);
            DumpInterestingMembers("VehicleController camera members", vehicleTarget?.VehicleController, VehicleCameraKeywords);

            if (gameManagerController != null)
                DumpInterestingMembers("GameManager vehicle refs", gameManagerController, VehicleStateKeywords);

            DumpActiveCameras();
            DumpVehicleCameraHierarchy();
            LogVehicleDebug("=== Vehicle diagnostics dump end ===");
        }

        private void DumpActiveCameras()
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
                LogVehicleDebug($"Camera.main: {GetHierarchyPath(mainCamera.transform)} fov={mainCamera.fieldOfView:0.##} enabled={mainCamera.enabled}");
            else
                LogVehicleDebug("Camera.main: none");

            foreach (var camera in Camera.allCameras)
            {
                if (camera == null)
                    continue;

                LogVehicleDebug($"Enabled Camera: path={GetHierarchyPath(camera.transform)}, enabled={camera.enabled}, fov={camera.fieldOfView:0.##}, depth={camera.depth:0.##}");
            }

            if (cinematachineVirtualCameraType == null)
                return;

            foreach (var vcam in Resources.FindObjectsOfTypeAll(cinematachineVirtualCameraType))
            {
                if (vcam == null)
                    continue;

                var component = vcam as Component;
                if (component == null || component.gameObject.hideFlags != HideFlags.None)
                    continue;

                var priority = GetFloatMember(component, "Priority");
                var follow = TryGetMemberValue(component, "Follow", out var followValue) ? followValue : null;
                var lookAt = TryGetMemberValue(component, "LookAt", out var lookAtValue) ? lookAtValue : null;
                LogVehicleDebug(
                    $"Cinemachine VCAM: path={GetHierarchyPath(component.transform)}, active={component.gameObject.activeInHierarchy}, enabled={((Behaviour)component).enabled}, " +
                    $"priority={priority:0.##}, follow={FormatMemberValue(follow)}, lookAt={FormatMemberValue(lookAt)}");
            }
        }

        private void DumpVehicleCameraHierarchy()
        {
            foreach (var vehicleCamera in cachedVehicleCameras ?? Array.Empty<Component>())
            {
                if (vehicleCamera == null)
                    continue;

                LogVehicleDebug($"Cached vehicle camera root: {GetHierarchyPath(vehicleCamera.transform)} active={vehicleCamera.gameObject.activeInHierarchy}");
                DumpDetailedVehicleVcam(vehicleCamera);
                DumpComponentsForHierarchy(vehicleCamera.transform);
            }

            if (Camera.main != null)
            {
                LogVehicleDebug("Camera.main hierarchy components:");
                DumpComponentsForHierarchy(Camera.main.transform);
            }
        }

        private void DumpComponentsForHierarchy(Transform root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;

                var typeName = component.GetType().FullName ?? component.GetType().Name;
                if (typeName.IndexOf("NWH", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("Cinemachine", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                LogVehicleDebug($"Hierarchy component: path={GetHierarchyPath(component.transform)}, type={typeName}, enabled={FormatEnabled(component)}");
            }
        }

        private void ApplyVisualCameraDiagnostic()
        {
            if (!cameraToolsDebugEnabled)
                return;

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var diagnosticFov = Mathf.Approximately(mainCamera.fieldOfView, 25f) ? 85f : 25f;
                mainCamera.fieldOfView = diagnosticFov;
                LogVehicleDebug($"F12 camera poke applied to Camera.main: path={GetHierarchyPath(mainCamera.transform)}, fov={diagnosticFov:0.##}");
            }

            foreach (var vehicleCamera in cachedVehicleCameras ?? Array.Empty<Component>())
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                ApplyVcamDiagnosticPoke(vehicleCamera);
            }
        }

        private void ApplyVcamDiagnosticPoke(Component vehicleCamera)
        {
            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return;

            var vcams = vehicleCamera.gameObject.GetComponentsInChildren(virtualCameraType, true);
            foreach (var vcam in vcams)
            {
                if (vcam == null)
                    continue;

                var component = vcam as Component;
                if (component == null)
                    continue;

                var beforeLens = TryReadLensFieldOfView(component);
                var afterLens = TryApplyLensFieldOfView(vcam, 25f);
                var bodySummary = ApplyActiveBodyDiagnosticPoke(vcam);
                LogVehicleDebug(
                    $"F12 vcam poke applied: path={GetHierarchyPath(component.transform)}, beforeFov={(beforeLens.HasValue ? beforeLens.Value.ToString("0.##") : "n/a")}, afterFov={(afterLens.HasValue ? afterLens.Value.ToString("0.##") : "n/a")}, body={bodySummary}");

                pendingVcamDiagnostic = new PendingVcamDiagnostic(component, bodySummary);
                break;
            }
        }

        private void ProcessPendingVcamDiagnostic()
        {
            if (!cameraToolsDebugEnabled)
            {
                pendingVcamDiagnostic = null;
                return;
            }

            if (pendingVcamDiagnostic == null)
                return;

            var diagnostic = pendingVcamDiagnostic.Value;
            if (diagnostic.VirtualCamera == null)
            {
                pendingVcamDiagnostic = null;
                return;
            }

            var lensFov = TryReadLensFieldOfView(diagnostic.VirtualCamera);
            var bodyReadback = ReadActiveBodyDiagnosticState(diagnostic.VirtualCamera);
            LogVehicleDebug(
                $"F12 vcam poke readback: path={GetHierarchyPath(diagnostic.VirtualCamera.transform)}, fov={(lensFov.HasValue ? lensFov.Value.ToString("0.##") : "n/a")}, body={bodyReadback}");
            pendingVcamDiagnostic = null;
        }

        private void DumpInterestingMembers(string label, object? target, string[] keywords)
        {
            if (target == null)
            {
                LogVehicleDebug($"{label}: none");
                return;
            }

            LogVehicleDebug($"{label}: type={target.GetType().FullName}");
            foreach (var member in EnumerateInterestingMembers(target, keywords))
            {
                LogVehicleDebug(
                    $"  {member.DeclaringType}.{member.Name} type={member.MemberType.Name} writable={member.Writable} value={FormatMemberValue(member.Value)}");
            }
        }

        private void DumpDetailedVehicleVcam(Component vehicleCamera)
        {
            LogVehicleDebug($"Vehicle vcam detail: root={GetHierarchyPath(vehicleCamera.transform)}");
            DumpInterestingMembers("Vehicle vcam root members", vehicleCamera, VehicleCameraKeywords);

            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return;

            foreach (var vcam in vehicleCamera.gameObject.GetComponentsInChildren(virtualCameraType, true))
            {
                if (vcam == null)
                    continue;

                var component = vcam as Component;
                if (component == null)
                    continue;

                var follow = TryGetMemberValue(vcam, "Follow", out var followValue) ? followValue : null;
                var lookAt = TryGetMemberValue(vcam, "LookAt", out var lookAtValue) ? lookAtValue : null;
                var priority = TryGetMemberValue(vcam, "Priority", out var priorityValue) ? priorityValue : null;
                LogVehicleDebug(
                    $"VCAM: path={GetHierarchyPath(component.transform)}, active={component.gameObject.activeInHierarchy}, enabled={FormatEnabled(component)}, " +
                    $"priority={FormatMemberValue(priority)}, follow={FormatMemberValue(follow)}, lookAt={FormatMemberValue(lookAt)}");

                foreach (var sameGoComponent in component.gameObject.GetComponents<Component>())
                {
                    if (sameGoComponent == null)
                        continue;

                    LogVehicleDebug($"  SameGO component: {sameGoComponent.GetType().FullName}");
                }

                var pipeline = GetCinemachinePipeline(virtualCameraType, vcam);
                if (pipeline == null)
                    continue;

                string bodyType = "none";
                string aimType = "none";
                foreach (var pipelineComponent in pipeline)
                {
                    if (pipelineComponent == null)
                        continue;

                    var typeName = pipelineComponent.GetType().Name;
                    if (bodyType == "none" && IsBodyComponentType(typeName))
                        bodyType = typeName;
                    if (aimType == "none" && IsAimComponentType(typeName))
                        aimType = typeName;

                    LogVehicleDebug($"  Pipeline component: {pipelineComponent.GetType().FullName}");
                    DumpInterestingMembers("  Pipeline members", pipelineComponent, VehicleCameraKeywords);
                }

                LogVehicleDebug($"  Body component type: {bodyType}");
                LogVehicleDebug($"  Aim component type: {aimType}");
            }
        }

        private static bool IsBodyComponentType(string typeName)
        {
            return typeName == "CinemachineTransposer" ||
                typeName == "CinemachineFramingTransposer" ||
                typeName == "Cinemachine3rdPersonFollow" ||
                typeName == "CinemachineHardLockToTarget" ||
                typeName == "CinemachineOrbitalTransposer";
        }

        private static bool IsAimComponentType(string typeName)
        {
            return typeName == "CinemachineComposer" ||
                typeName == "CinemachineHardLookAt" ||
                typeName == "CinemachinePOV";
        }

        private static float? TryApplyLensFieldOfView(object vcam, float targetFov)
        {
            try
            {
                var lensField = vcam.GetType().GetField("m_Lens", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lensField == null)
                    return null;

                var lensValue = lensField.GetValue(vcam);
                if (lensValue == null)
                    return null;

                var lensType = lensValue.GetType();
                var fovField = lensType.GetField("FieldOfView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fovField == null)
                    return null;

                fovField.SetValue(lensValue, targetFov);
                lensField.SetValue(vcam, lensValue);
                return targetFov;
            }
            catch
            {
                return null;
            }
        }

        private static float? TryReadLensFieldOfView(Component vcam)
        {
            try
            {
                var lensField = vcam.GetType().GetField("m_Lens", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lensField == null)
                    return null;

                var lensValue = lensField.GetValue(vcam);
                if (lensValue == null)
                    return null;

                var fovField = lensValue.GetType().GetField("FieldOfView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fovField == null)
                    return null;

                return Convert.ToSingle(fovField.GetValue(lensValue));
            }
            catch
            {
                return null;
            }
        }

        private static string ApplyActiveBodyDiagnosticPoke(object vcam)
        {
            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return "no-virtual-camera-type";

            var pipeline = GetCinemachinePipeline(virtualCameraType, vcam);
            if (pipeline == null)
                return "no-pipeline";

            foreach (var pipelineComponent in pipeline)
            {
                if (pipelineComponent == null)
                    continue;

                var typeName = pipelineComponent.GetType().Name;
                if (typeName == "Cinemachine3rdPersonFollow" &&
                    TryGetFloatMember(pipelineComponent, "CameraDistance", out var beforeDistance))
                {
                    SetMemberValue(pipelineComponent, "CameraDistance", 6f);
                    return $"{typeName}.CameraDistance {beforeDistance:0.##}->6";
                }

                if (typeName == "CinemachineFramingTransposer" &&
                    TryGetFloatMember(pipelineComponent, "m_CameraDistance", out var beforeFramingDistance))
                {
                    SetMemberValue(pipelineComponent, "m_CameraDistance", 6f);
                    return $"{typeName}.m_CameraDistance {beforeFramingDistance:0.##}->6";
                }

                if ((typeName == "CinemachineTransposer" || typeName == "CinemachineOrbitalTransposer") &&
                    TryGetFollowOffset(pipelineComponent, out var followOffset))
                {
                    var beforeOffset = followOffset;
                    var originalMagnitude = beforeOffset.magnitude;
                    var scale = originalMagnitude > Mathf.Epsilon ? 6f / originalMagnitude : 1f;
                    followOffset = beforeOffset * scale;
                    SetFollowOffset(pipelineComponent, followOffset);
                    return $"{typeName}.FollowOffset {beforeOffset}->{followOffset}";
                }
            }

            return "no-supported-body-component";
        }

        private static string ReadActiveBodyDiagnosticState(Component vcam)
        {
            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return "no-virtual-camera-type";

            var pipeline = GetCinemachinePipeline(virtualCameraType, vcam);
            if (pipeline == null)
                return "no-pipeline";

            foreach (var pipelineComponent in pipeline)
            {
                if (pipelineComponent == null)
                    continue;

                var typeName = pipelineComponent.GetType().Name;
                if (typeName == "Cinemachine3rdPersonFollow" &&
                    TryGetFloatMember(pipelineComponent, "CameraDistance", out var distance))
                    return $"{typeName}.CameraDistance={distance:0.##}";

                if (typeName == "CinemachineFramingTransposer" &&
                    TryGetFloatMember(pipelineComponent, "m_CameraDistance", out var framingDistance))
                    return $"{typeName}.m_CameraDistance={framingDistance:0.##}";

                if ((typeName == "CinemachineTransposer" || typeName == "CinemachineOrbitalTransposer") &&
                    TryGetFollowOffset(pipelineComponent, out var followOffset))
                    return $"{typeName}.FollowOffset={followOffset}";
            }

            return "no-supported-body-component";
        }

        private static IEnumerable<InterestingMember> EnumerateInterestingMembers(object target, string[] keywords)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var type = target.GetType();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!NameMatchesKeywords(field.Name, keywords) || !seen.Add("F:" + field.Name))
                    continue;

                yield return new InterestingMember(type.FullName ?? type.Name, field.Name, field.FieldType, true, SafeRead(() => field.GetValue(target)));
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!NameMatchesKeywords(property.Name, keywords) || !seen.Add("P:" + property.Name))
                    continue;

                yield return new InterestingMember(type.FullName ?? type.Name, property.Name, property.PropertyType, property.CanWrite, SafeRead(() => property.CanRead ? property.GetValue(target, null) : null));
            }
        }

        private static bool NameMatchesKeywords(string name, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private Vector3 GetOrCacheOriginalFollowOffset(object pipelineComponent, Vector3 currentOffset)
        {
            if (pipelineComponent is not Component component)
                return currentOffset;

            var key = component.GetInstanceID();
            if (cachedVehicleFollowOffsets.TryGetValue(key, out var cachedOffset))
                return cachedOffset;

            cachedVehicleFollowOffsets[key] = currentOffset;
            return currentOffset;
        }

        private static bool TryGetFollowOffset(object pipelineComponent, out Vector3 offset)
        {
            offset = default;
            if (TryGetMemberValue(pipelineComponent, "m_FollowOffset", out var offsetValue) && offsetValue is Vector3 privateOffset)
            {
                offset = privateOffset;
                return true;
            }

            if (TryGetMemberValue(pipelineComponent, "FollowOffset", out var publicOffsetValue) && publicOffsetValue is Vector3 publicOffset)
            {
                offset = publicOffset;
                return true;
            }

            return false;
        }

        private static bool SetFollowOffset(object pipelineComponent, Vector3 offset)
        {
            return SetMemberValue(pipelineComponent, "m_FollowOffset", offset) ||
                SetMemberValue(pipelineComponent, "FollowOffset", offset);
        }

        private static bool IsFollowOffsetComponent(object pipelineComponent)
        {
            var typeName = pipelineComponent.GetType().Name;
            return typeName == "CinemachineTransposer" || typeName == "CinemachineOrbitalTransposer";
        }

        private void OnGUI()
        {
            if (!cameraToolsDebugEnabled || !showVehicleDebugOverlay)
                return;

            debugOverlayStyle ??= CreateDebugOverlayStyle();

            var lines = new[]
            {
                "CameraTools Vehicle Debug",
                "F8 overlay, F9/F10 zoom, F11 dump, F12 camera poke",
                $"Scroll delta: {vehicleDebug.ScrollDelta:0.###}",
                $"Vehicle mode: {vehicleDebug.IsVehicleMode}",
                $"Inside vehicle: {vehicleDebug.IsInsideVehicle}",
                $"Active car found: {vehicleDebug.ActiveCarFound}",
                $"Car controller: {vehicleDebug.CarControllerTypeName}",
                $"Vehicle controller found: {vehicleDebug.VehicleControllerFound}",
                $"Vehicle controller: {vehicleDebug.VehicleControllerTypeName}",
                $"Distance member: {vehicleDebug.DistanceMemberName}",
                $"Current distance: {vehicleDebug.CurrentDistance:0.##}",
                $"Actual distance: {vehicleDebug.ActualDistance:0.##}",
                $"Original offset: {vehicleDebug.OriginalFollowOffset}",
                $"Current offset: {vehicleDebug.CurrentFollowOffset}",
                $"Last applied distance: {vehicleDebug.LastAppliedDistance:0.##}",
                $"Value overwritten: {vehicleDebug.WasOverwritten}",
                $"Camera object found: {vehicleDebug.CameraObjectFound}",
                $"Cinemachine camera found: {vehicleDebug.CinemachineFound}",
                $"Map current distance: {mapDebugCurrentDistance:0.##}",
                $"Map desired distance: {mapDebugDesiredDistance:0.##}",
                $"Map vanilla delta: {mapDebugVanillaDelta:0.##}",
                $"Map raw scroll: {mapDebugRawScrollDelta:0.##}"
            };

            var content = string.Join("\n", lines);
            GUI.Box(new Rect(12f, 12f, 420f, 280f), content, debugOverlayStyle);
        }

        private static GUIStyle CreateDebugOverlayStyle()
        {
            var style = new GUIStyle(GUI.skin.box);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = 14;
            style.normal.textColor = Color.white;
            return style;
        }

    }
}
