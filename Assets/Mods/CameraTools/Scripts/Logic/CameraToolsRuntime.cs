#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UI.Notification;
using UnityEngine;

namespace CameraTools
{
    public sealed class CameraToolsRuntime : MonoBehaviour
    {
        private const float PitchStepPerMousePixel = 0.15f;
        private const float VehicleMinimumZoom = 6f;
        private const float VehicleZoomStepPerScrollTick = 4f;
        private const float VehicleForcedZoomStep = 20f;
        private const float GameplayMinimumZoom = 1.5f;
        private const float MapMinimumZoom = 1f;
        private const float VehicleSearchIntervalSeconds = 5f;
        private const string GameManagerTypeName = "GameManager";
        private const string CarControllerTypeName = "CarController";
        private const string VehicleControllerTypeName = "VehicleController";
        private const string PedestrianCamTypeName = "PedestrianCam";
        private const string CityMapCamTypeName = "CityMapCam";
        private const string CameraMouseDragTypeName = "CameraMouseDrag";
        private const string SaveGameManagerTypeName = "SaveGameManager";
        private static readonly string[] VehicleCameraMemberNames =
        {
            "vehicleCamera",
            "vehicleCameraReverse",
            "indoorVehicleCamera",
            "indoorVehicleCameraReverse"
        };
        private static readonly string[] VehicleControllerMemberNames =
        {
            "vehicleController",
            "_vehicleController",
            "VehicleController"
        };
        private static readonly string[] VehicleDistanceMemberNames =
        {
            "vehicleToCamDistance",
            "VehicleToCamDistance",
            "cameraDistance",
            "CameraDistance",
            "distance",
            "Distance"
        };
        private static readonly string[] VehicleMinDistanceMemberNames =
        {
            "minCameraDistance",
            "MinCameraDistance",
            "vehicleCameraMinDistance",
            "VehicleCameraMinDistance",
            "minDistance",
            "MinDistance"
        };
        private static readonly string[] VehicleMaxDistanceMemberNames =
        {
            "maxCameraDistance",
            "MaxCameraDistance",
            "vehicleCameraMaxDistance",
            "VehicleCameraMaxDistance",
            "maxDistance",
            "MaxDistance"
        };

        private Camera? activeMapCamera;
        private CameraState activeMapCameraState;
        private Component[]? cachedVehicleCameras;
        private MonoBehaviour? configuredGameplayController;
        private MonoBehaviour? configuredMapController;
        private ModContext? context;
        private float desiredVehicleDistance;
        private MonoBehaviour? gameManagerController;
        private MonoBehaviour? gameplayController;
        private bool hasInitializedMapDistanceForCurrentOpen;
        private bool hasManualGameplayPitch;
        private bool hasShownGameplayPitchHint;
        private bool hasShownMapStatusNotice;
        private bool isTrackingRightMousePitch;
        private int lastActiveVehicleCameraId;
        private float lastAppliedMapDistanceSetting;
        private float lastAppliedVehicleMaxZoom;
        private float lastRightMouseY;
        private float lastVehicleControllerSearchTime;
        private MonoBehaviour? mapController;
        private float manualGameplayPitch;
        private string? pendingMapNoticeDuplicateIdentifier;
        private string? pendingMapNoticeMessage;
        private CameraToolsSettings? settings;
        private bool showVehicleDebugOverlay;
        private bool wasCityMapOpen;
        private VehicleDebugState vehicleDebug = new VehicleDebugState();
        private VehicleTarget? vehicleTarget;
        private static Type? cameraMouseDragType;
        private static Type? carControllerType;
        private static Type? cityMapType;
        private static Type? cityMapCamType;
        private static Type? cinematachineVirtualCameraType;
        private static GUIStyle? debugOverlayStyle;
        private static Type? gameManagerType;
        private static Type? pedestrianCamType;
        private static Type? saveGameManagerType;
        private static Type? vehicleControllerType;

        public static CameraToolsRuntime Initialize(ModContext context, CameraToolsSettings settings)
        {
            var runtime = FindObjectOfType<CameraToolsRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(CameraToolsRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<CameraToolsRuntime>();
            }

            runtime.context = context;
            runtime.settings = settings;
            runtime.cachedVehicleCameras = null;
            runtime.configuredGameplayController = null;
            runtime.configuredMapController = null;
            runtime.desiredVehicleDistance = float.NaN;
            runtime.gameManagerController = null;
            runtime.gameplayController = null;
            runtime.hasInitializedMapDistanceForCurrentOpen = false;
            runtime.hasManualGameplayPitch = false;
            runtime.hasShownGameplayPitchHint = false;
            runtime.hasShownMapStatusNotice = false;
            runtime.isTrackingRightMousePitch = false;
            runtime.lastActiveVehicleCameraId = 0;
            runtime.lastAppliedMapDistanceSetting = float.NaN;
            runtime.lastAppliedVehicleMaxZoom = float.NaN;
            runtime.lastRightMouseY = 0f;
            runtime.lastVehicleControllerSearchTime = float.NegativeInfinity;
            runtime.mapController = null;
            runtime.pendingMapNoticeDuplicateIdentifier = null;
            runtime.pendingMapNoticeMessage = null;
            runtime.showVehicleDebugOverlay = false;
            runtime.vehicleTarget = null;
            runtime.vehicleDebug = new VehicleDebugState();
            runtime.wasCityMapOpen = false;

            pedestrianCamType ??= FindType(PedestrianCamTypeName);
            cityMapType ??= FindType("CityMap");
            cityMapCamType ??= FindType(CityMapCamTypeName);
            carControllerType ??= FindType(CarControllerTypeName);
            vehicleControllerType ??= FindType(VehicleControllerTypeName);
            cameraMouseDragType ??= FindType(CameraMouseDragTypeName);
            cinematachineVirtualCameraType ??= FindType("CinemachineVirtualCamera");
            gameManagerType ??= FindType(GameManagerTypeName);
            saveGameManagerType ??= FindType(SaveGameManagerTypeName);

            CameraToolsFileLogger.Log("CameraTools runtime initialized.");
            return runtime;
        }

        public void Shutdown()
        {
            RestoreMapCameraState();
            CameraToolsFileLogger.Log("CameraTools runtime shutting down.");
            Destroy(gameObject);
        }

        private void OnEnable()
        {
            Camera.onPreCull += HandleCameraPreCull;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandleCameraPreCull;
        }

        private void LateUpdate()
        {
            if (settings == null)
                return;

            EnsureControllers();
            HandleVehicleDebugHotkeys();

            var cityMapOpen = IsCityMapOpen();
            var gameplayActive = IsGameplayActive();

            ApplyGameplayTweaks();
            ApplyVehicleTweaks(gameplayActive);
            ApplyMapTweaks(cityMapOpen);
            FlushPendingMapNotice(cityMapOpen);
        }

        private void OnGUI()
        {
            if (!showVehicleDebugOverlay)
                return;

            debugOverlayStyle ??= CreateDebugOverlayStyle();

            var lines = new[]
            {
                "CameraTools Vehicle Debug",
                "F8 toggle overlay, F9 zoom in, F10 zoom out",
                $"Scroll delta: {vehicleDebug.ScrollDelta:0.###}",
                $"Vehicle mode: {vehicleDebug.IsVehicleMode}",
                $"Inside vehicle: {vehicleDebug.IsInsideVehicle}",
                $"Active car found: {vehicleDebug.ActiveCarFound}",
                $"Car controller: {vehicleDebug.CarControllerTypeName}",
                $"Vehicle controller found: {vehicleDebug.VehicleControllerFound}",
                $"Vehicle controller: {vehicleDebug.VehicleControllerTypeName}",
                $"Distance member: {vehicleDebug.DistanceMemberName}",
                $"Current distance: {vehicleDebug.CurrentDistance:0.##}",
                $"Last applied distance: {vehicleDebug.LastAppliedDistance:0.##}",
                $"Value overwritten: {vehicleDebug.WasOverwritten}",
                $"Camera object found: {vehicleDebug.CameraObjectFound}",
                $"Cinemachine camera found: {vehicleDebug.CinemachineFound}"
            };

            var content = string.Join("\n", lines);
            GUI.Box(new Rect(12f, 12f, 420f, 280f), content, debugOverlayStyle);
        }

        private void EnsureControllers()
        {
            if (gameplayController == null || !gameplayController.isActiveAndEnabled)
            {
                gameplayController = FindFirstActiveController(pedestrianCamType, includeInactive: false);
                if (gameplayController != configuredGameplayController)
                    configuredGameplayController = null;
            }

            if (gameplayController != null && gameplayController != configuredGameplayController)
            {
                configuredGameplayController = gameplayController;
                ConfigureGameplayController();
            }

            if (IsCityMapOpen() && (mapController == null || !mapController.isActiveAndEnabled))
            {
                mapController = FindFirstActiveController(cityMapCamType, includeInactive: false) ??
                    FindFirstActiveController(cityMapCamType, includeInactive: true);
                if (mapController != configuredMapController)
                    configuredMapController = null;
            }

            if (mapController != null && mapController != configuredMapController)
            {
                configuredMapController = mapController;
                ConfigureMapController();
            }

            if (gameManagerController == null || !gameManagerController.isActiveAndEnabled)
            {
                gameManagerController = FindFirstActiveController(gameManagerType, includeInactive: false) ??
                    FindFirstActiveController(gameManagerType, includeInactive: true);
                cachedVehicleCameras = null;
            }
        }

        private static MonoBehaviour? FindFirstActiveController(Type? type, bool includeInactive)
        {
            if (type == null)
                return null;

            if (!includeInactive)
            {
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
                {
                    var behaviour = obj as MonoBehaviour;
                    if (behaviour != null)
                        return behaviour;
                }

                return null;
            }

            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                var behaviour = obj as MonoBehaviour;
                if (behaviour == null || behaviour.gameObject == null || behaviour.gameObject.hideFlags != HideFlags.None)
                    continue;

                return behaviour;
            }

            return null;
        }

        private void ConfigureGameplayController()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            var bounds = GetVector2Member(gameplayController, "minMaxDistance");
            bounds.x = Mathf.Min(bounds.x, GameplayMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.GameplayMaxZoom);
            SetMemberValue(gameplayController, "minMaxDistance", bounds);
            SetMemberValue(gameplayController, "blockCameraZoom", false);

            if (!hasManualGameplayPitch)
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, settings.GameplayMinPitch, settings.GameplayMaxPitch);

            ApplyGameplayOffset(manualGameplayPitch);
        }

        private void ApplyGameplayTweaks()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            ConfigureGameplayController();

            var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);

            if (!hasShownGameplayPitchHint && context != null)
            {
                context.Logger.Info("CameraTools: hold right mouse and move up or down to tilt the gameplay camera.");
                hasShownGameplayPitchHint = true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                isTrackingRightMousePitch = true;
                lastRightMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(1) && isTrackingRightMousePitch)
            {
                var currentMouseY = Input.mousePosition.y;
                var deltaY = currentMouseY - lastRightMouseY;
                lastRightMouseY = currentMouseY;

                if (Mathf.Abs(deltaY) > Mathf.Epsilon)
                {
                    manualGameplayPitch = Mathf.Clamp(manualGameplayPitch - deltaY * PitchStepPerMousePixel, minPitch, maxPitch);
                    hasManualGameplayPitch = true;
                }
            }

            if (Input.GetMouseButtonUp(1))
                isTrackingRightMousePitch = false;

            if (Input.GetKeyDown(KeyCode.Home))
            {
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, minPitch, maxPitch);
                hasManualGameplayPitch = true;
            }

            ApplyGameplayOffset(hasManualGameplayPitch ? manualGameplayPitch : settings.GameplayDefaultPitch);
        }

        private void ApplyGameplayOffset(float pitchDegrees)
        {
            if (gameplayController == null)
                return;

            var radians = Mathf.Deg2Rad * Mathf.Clamp(pitchDegrees, 1f, 89f);
            var offset = new Vector3(0f, Mathf.Sin(radians), -Mathf.Cos(radians));
            SetMemberValue(gameplayController, "offset", offset);
        }

        private void ConfigureMapController()
        {
            if (settings == null || mapController == null || !settings.EnableMapTopDown)
                return;

            var bounds = GetVector2Member(mapController, "minMaxDistance");
            bounds.x = Mathf.Min(bounds.x, MapMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.MapDistance);
            SetMemberValue(mapController, "minMaxDistance", bounds);

            var desiredDistance = Mathf.Clamp(settings.MapDistance, bounds.x, bounds.y);
            var shouldSeedDistance =
                !hasInitializedMapDistanceForCurrentOpen ||
                !Mathf.Approximately(lastAppliedMapDistanceSetting, desiredDistance);

            if (!shouldSeedDistance)
                return;

            var currentDistance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
            if (currentDistance < desiredDistance)
            {
                SetMemberValue(mapController, "distance", desiredDistance);
                currentDistance = desiredDistance;
            }

            SetSavedMapZoom(currentDistance);
            hasInitializedMapDistanceForCurrentOpen = true;
            lastAppliedMapDistanceSetting = desiredDistance;
        }

        private void ApplyVehicleTweaks(bool gameplayActive)
        {
            if (settings == null)
                return;

            var wantsVehicleWork =
                showVehicleDebugOverlay ||
                Mathf.Abs(Input.mouseScrollDelta.y) > Mathf.Epsilon ||
                Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > Mathf.Epsilon ||
                Input.GetKeyDown(KeyCode.F9) ||
                Input.GetKeyDown(KeyCode.F10) ||
                (vehicleTarget != null && IsVehicleTargetStillValid(vehicleTarget)) ||
                vehicleDebug.IsVehicleMode;

            if (!gameplayActive)
            {
                vehicleDebug.IsVehicleMode = false;
                vehicleDebug.IsInsideVehicle = false;
                vehicleDebug.CameraObjectFound = false;
                desiredVehicleDistance = float.NaN;
                return;
            }

            if (wantsVehicleWork)
                ResolveVehicleTarget(forceSearch: false, allowExpensiveSearch: true);
            else
                UpdateVehicleDebugStateFromTarget();

            if (gameManagerController != null && (cachedVehicleCameras == null || cachedVehicleCameras.Length == 0))
                cachedVehicleCameras = ResolveVehicleCameras(gameManagerController);

            if (cachedVehicleCameras != null && cachedVehicleCameras.Length > 0 && !AreAllVehicleCamerasAlive(cachedVehicleCameras))
                cachedVehicleCameras = gameManagerController == null ? Array.Empty<Component>() : ResolveVehicleCameras(gameManagerController);

            if (!vehicleDebug.IsVehicleMode)
            {
                desiredVehicleDistance = float.NaN;
                return;
            }

            if (float.IsNaN(desiredVehicleDistance))
            {
                desiredVehicleDistance = ResolveCurrentVehicleDistance(settings.VehicleMaxZoom);
                vehicleDebug.CurrentDistance = desiredVehicleDistance;
            }

            var scrollDelta = ReadVehicleScrollDelta();
            vehicleDebug.ScrollDelta = scrollDelta;
            if (Mathf.Abs(scrollDelta) > Mathf.Epsilon)
            {
                var nextDistance = Mathf.Clamp(
                    desiredVehicleDistance - scrollDelta * VehicleZoomStepPerScrollTick,
                    VehicleMinimumZoom,
                    settings.VehicleMaxZoom);
                ApplyVehicleDistance(nextDistance, "scroll");
            }
            else
            {
                ReapplyVehicleDistanceIfNeeded();
            }

            ApplyVehicleZoomLimits();
        }

        private void HandleVehicleDebugHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                showVehicleDebugOverlay = !showVehicleDebugOverlay;
                CameraToolsFileLogger.Log($"Vehicle debug overlay toggled: {showVehicleDebugOverlay}");
            }

            if (settings == null)
                return;

            if (Input.GetKeyDown(KeyCode.F9))
                ApplyVehicleDistance(
                    Mathf.Max(VehicleMinimumZoom, ResolveCurrentVehicleDistance(settings.VehicleMaxZoom) - VehicleForcedZoomStep),
                    "hotkey-zoom-in");

            if (Input.GetKeyDown(KeyCode.F10))
                ApplyVehicleDistance(Mathf.Min(settings.VehicleMaxZoom, ResolveCurrentVehicleDistance(settings.VehicleMaxZoom) + VehicleForcedZoomStep), "hotkey-zoom-out");
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
                CameraToolsFileLogger.Log("Vehicle target resolved. " + newSummary);
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
            vehicleDebug.IsInsideVehicle = IsInsideVehicle(vehicleTarget?.VehicleController);
            vehicleDebug.IsVehicleMode = vehicleDebug.IsInsideVehicle || GetActiveVehicleCameraId(cachedVehicleCameras ?? Array.Empty<Component>()) != 0;
            vehicleDebug.CameraObjectFound = cachedVehicleCameras != null && cachedVehicleCameras.Length > 0;
        }

        private float ReadVehicleScrollDelta()
        {
            var scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) <= Mathf.Epsilon)
                scrollDelta = Input.GetAxis("Mouse ScrollWheel") * 120f;

            if (Mathf.Abs(scrollDelta) > Mathf.Epsilon)
                CameraToolsFileLogger.Log($"Vehicle scroll detected: delta={scrollDelta:0.###}, insideVehicle={vehicleDebug.IsInsideVehicle}");

            return scrollDelta;
        }

        private float ResolveCurrentVehicleDistance(float maxZoom)
        {
            if (TryGetVehicleDistanceValue(out var distance, out var memberName))
            {
                vehicleDebug.DistanceMemberName = memberName;
                return Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom);
            }

            vehicleDebug.DistanceMemberName = "not-found";
            return Mathf.Clamp(maxZoom, VehicleMinimumZoom, maxZoom);
        }

        private bool TryGetVehicleDistanceValue(out float value, out string memberName)
        {
            value = 0f;
            memberName = "none";

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

            ResolveVehicleTarget(forceSearch: true, allowExpensiveSearch: true);
            UpdateVehicleDebugStateFromTarget();

            var clampedDistance = Mathf.Clamp(targetDistance, VehicleMinimumZoom, settings.VehicleMaxZoom);
            var oldDistance = ResolveCurrentVehicleDistance(settings.VehicleMaxZoom);
            desiredVehicleDistance = clampedDistance;

            var sourceValueApplied = ApplyVehicleDistanceToSource(clampedDistance, settings.VehicleMaxZoom);
            var cameraValueApplied = ApplyVehicleDistanceToCameras(clampedDistance, settings.VehicleMaxZoom);

            vehicleDebug.CurrentDistance = oldDistance;
            vehicleDebug.LastAppliedDistance = clampedDistance;
            vehicleDebug.CinemachineFound = cameraValueApplied;

            var readBackDistance = ResolveCurrentVehicleDistance(settings.VehicleMaxZoom);
            vehicleDebug.WasOverwritten = !Mathf.Approximately(readBackDistance, clampedDistance);

            var applySummary =
                $"Vehicle zoom apply ({reason}). insideVehicle={vehicleDebug.IsInsideVehicle}, activeCarFound={vehicleDebug.ActiveCarFound}, " +
                $"carType={vehicleDebug.CarControllerTypeName}, vehicleControllerFound={vehicleDebug.VehicleControllerFound}, " +
                $"distanceMember={vehicleDebug.DistanceMemberName}, oldDistance={oldDistance:0.##}, newDistance={clampedDistance:0.##}, " +
                $"sourceApplied={sourceValueApplied}, cameraApplied={cameraValueApplied}, overwritten={vehicleDebug.WasOverwritten}";
            if (reason != "lateupdate-reapply" || vehicleDebug.LastApplySummary != applySummary)
            {
                vehicleDebug.LastApplySummary = applySummary;
                CameraToolsFileLogger.Log(applySummary);
            }
        }

        private void ReapplyVehicleDistanceIfNeeded()
        {
            if (settings == null || float.IsNaN(desiredVehicleDistance))
                return;

            var currentDistance = ResolveCurrentVehicleDistance(settings.VehicleMaxZoom);
            if (Mathf.Approximately(currentDistance, desiredVehicleDistance))
                return;

            vehicleDebug.WasOverwritten = true;
            ApplyVehicleDistance(desiredVehicleDistance, "lateupdate-reapply");
        }

        private bool ApplyVehicleDistanceToSource(float distance, float maxZoom)
        {
            var applied = false;
            if (vehicleTarget?.VehicleController != null)
            {
                applied |= SetFirstFloatMember(vehicleTarget.VehicleController, VehicleDistanceMemberNames, distance);
                applied |= SetFirstFloatMember(vehicleTarget.VehicleController, VehicleMinDistanceMemberNames, VehicleMinimumZoom);
                applied |= SetFirstFloatMember(vehicleTarget.VehicleController, VehicleMaxDistanceMemberNames, maxZoom);
            }

            if (vehicleTarget?.CarController != null)
            {
                applied |= SetFirstFloatMember(vehicleTarget.CarController, VehicleDistanceMemberNames, distance);
                applied |= SetFirstFloatMember(vehicleTarget.CarController, VehicleMinDistanceMemberNames, VehicleMinimumZoom);
                applied |= SetFirstFloatMember(vehicleTarget.CarController, VehicleMaxDistanceMemberNames, maxZoom);
            }

            return applied;
        }

        private void ApplyVehicleZoomLimits()
        {
            if (settings == null || cachedVehicleCameras == null || cachedVehicleCameras.Length == 0)
                return;

            var activeVehicleCameraId = GetActiveVehicleCameraId(cachedVehicleCameras);
            if (Mathf.Approximately(lastAppliedVehicleMaxZoom, settings.VehicleMaxZoom) &&
                activeVehicleCameraId == lastActiveVehicleCameraId)
                return;

            foreach (var vehicleCamera in cachedVehicleCameras)
            {
                if (vehicleCamera == null)
                    continue;

                ApplyVehicleCameraZoomLimits(vehicleCamera.gameObject, settings.VehicleMaxZoom);
            }

            lastAppliedVehicleMaxZoom = settings.VehicleMaxZoom;
            lastActiveVehicleCameraId = activeVehicleCameraId;
        }

        private bool ApplyVehicleDistanceToCameras(float distance, float maxZoom)
        {
            var foundCamera = false;
            foreach (var vehicleCamera in cachedVehicleCameras ?? Array.Empty<Component>())
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

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

        private static int GetActiveVehicleCameraId(Component[] vehicleCameras)
        {
            foreach (var vehicleCamera in vehicleCameras)
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                return vehicleCamera.gameObject.GetInstanceID();
            }

            return 0;
        }

        private static bool TryGetActiveVehicleCameraDistance(Component[] vehicleCameras, out float distance)
        {
            distance = 0f;
            foreach (var vehicleCamera in vehicleCameras)
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                if (TryGetVehicleCameraDistance(vehicleCamera.gameObject, out distance))
                    return true;
            }

            return false;
        }

        private static bool TryGetVehicleCameraDistance(GameObject cameraObject, out float distance)
        {
            distance = 0f;

            if (cameraMouseDragType != null)
            {
                foreach (var zoomComponent in cameraObject.GetComponentsInChildren(cameraMouseDragType, true))
                {
                    if (zoomComponent != null && TryGetFloatMember(zoomComponent, "distance", out distance))
                        return true;
                }
            }

            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return false;

            foreach (var virtualCamera in cameraObject.GetComponentsInChildren(virtualCameraType, true))
            {
                if (virtualCamera == null)
                    continue;

                var pipeline = GetCinemachinePipeline(virtualCameraType, virtualCamera);
                if (pipeline == null)
                    continue;

                foreach (var pipelineComponent in pipeline)
                {
                    if (pipelineComponent == null)
                        continue;

                    var typeName = pipelineComponent.GetType().Name;
                    if ((typeName == "CinemachineFramingTransposer" && TryGetFloatMember(pipelineComponent, "m_CameraDistance", out distance)) ||
                        (typeName == "Cinemachine3rdPersonFollow" && TryGetFloatMember(pipelineComponent, "CameraDistance", out distance)))
                        return true;
                }
            }

            return false;
        }

        private static void ApplyVehicleCameraZoomLimits(GameObject cameraObject, float maxZoom)
        {
            if (cameraMouseDragType != null)
            {
                foreach (var zoomComponent in cameraObject.GetComponentsInChildren(cameraMouseDragType, true))
                {
                    if (zoomComponent == null)
                        continue;

                    SetMemberValue(zoomComponent, "minDistance", VehicleMinimumZoom);
                    SetMemberValue(zoomComponent, "maxDistance", maxZoom);
                }
            }

            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return;

            foreach (var virtualCamera in cameraObject.GetComponentsInChildren(virtualCameraType, true))
            {
                if (virtualCamera == null)
                    continue;

                var pipeline = GetCinemachinePipeline(virtualCameraType, virtualCamera);
                if (pipeline == null)
                    continue;

                foreach (var pipelineComponent in pipeline)
                {
                    if (pipelineComponent == null)
                        continue;

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
                }
            }
        }

        private static void ApplyVehicleCameraDistance(GameObject cameraObject, float distance, float maxZoom)
        {
            if (cameraMouseDragType != null)
            {
                foreach (var zoomComponent in cameraObject.GetComponentsInChildren(cameraMouseDragType, true))
                {
                    if (zoomComponent == null)
                        continue;

                    SetMemberValue(zoomComponent, "minDistance", VehicleMinimumZoom);
                    SetMemberValue(zoomComponent, "maxDistance", maxZoom);
                    SetMemberValue(zoomComponent, "distance", Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom));
                }
            }

            var virtualCameraType = cinematachineVirtualCameraType;
            if (virtualCameraType == null)
                return;

            foreach (var virtualCamera in cameraObject.GetComponentsInChildren(virtualCameraType, true))
            {
                if (virtualCamera == null)
                    continue;

                var pipeline = GetCinemachinePipeline(virtualCameraType, virtualCamera);
                if (pipeline == null)
                    continue;

                foreach (var pipelineComponent in pipeline)
                {
                    if (pipelineComponent == null)
                        continue;

                    var typeName = pipelineComponent.GetType().Name;
                    if (typeName == "CinemachineFramingTransposer")
                    {
                        SetMemberValue(pipelineComponent, "m_MinimumDistance", VehicleMinimumZoom);
                        SetMemberValue(pipelineComponent, "m_MaximumDistance", maxZoom);
                        SetMemberValue(pipelineComponent, "m_CameraDistance", Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom));
                    }
                    else if (typeName == "Cinemachine3rdPersonFollow")
                    {
                        SetMemberValue(pipelineComponent, "CameraDistance", Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom));
                    }
                }
            }
        }

        private void ApplyMapTweaks(bool cityMapOpen)
        {
            if (cityMapOpen && !wasCityMapOpen)
            {
                hasShownMapStatusNotice = false;
                hasInitializedMapDistanceForCurrentOpen = false;
            }

            if (!cityMapOpen)
                hasInitializedMapDistanceForCurrentOpen = false;

            wasCityMapOpen = cityMapOpen;

            if (settings == null || mapController == null || !settings.EnableMapTopDown)
            {
                RestoreMapCameraState();
                if (cityMapOpen && !hasShownMapStatusNotice)
                {
                    QueueMapNotice(
                        "CameraTools could not find CityMapCam while the map was open.",
                        "cameratools_map_notice_missing");
                    hasShownMapStatusNotice = true;
                }
                return;
            }

            ConfigureMapController();

            var mapCamera = ResolveMapCamera(mapController);
            if (mapCamera == null)
                return;

            if (activeMapCamera != mapCamera)
            {
                RestoreMapCameraState();
                activeMapCamera = mapCamera;
                activeMapCameraState = new CameraState(mapCamera);
            }

            if (!hasShownMapStatusNotice)
            {
                QueueMapNotice(
                    "CameraTools found CityMapCam and applied map changes.",
                    "cameratools_map_notice_found");
                hasShownMapStatusNotice = true;
            }

            var bounds = GetVector2Member(mapController, "minMaxDistance");
            var distance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
            var currentAngle = GetFloatMember(mapController, "_currentAngle");
            var pitch = Mathf.Clamp(settings.MapPitch, 75f, 90f);
            var pitchRadians = pitch * Mathf.Deg2Rad;
            var height = distance * Mathf.Sin(pitchRadians);
            var horizontalRadius = Mathf.Max(0.05f, distance * Mathf.Cos(pitchRadians));
            var orbit = Quaternion.Euler(0f, currentAngle, 0f) * new Vector3(0f, height, -horizontalRadius);
            var rootPosition = mapController.transform.position;
            var targetPosition = rootPosition + orbit;
            var upAxis = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward;
            var targetRotation = Quaternion.LookRotation(rootPosition - targetPosition, upAxis);

            var vCamTransform = GetTransformMember(mapController, "_vCam");
            if (vCamTransform != null)
            {
                vCamTransform.position = targetPosition;
                vCamTransform.rotation = targetRotation;
            }

            mapCamera.transform.position = targetPosition;
            mapCamera.transform.rotation = targetRotation;
        }

        private void QueueMapNotice(string message, string duplicateIdentifier)
        {
            pendingMapNoticeMessage = message;
            pendingMapNoticeDuplicateIdentifier = duplicateIdentifier;
        }

        private void FlushPendingMapNotice(bool cityMapOpen)
        {
            if (cityMapOpen || string.IsNullOrEmpty(pendingMapNoticeMessage) || string.IsNullOrEmpty(pendingMapNoticeDuplicateIdentifier))
                return;

            ShowPopup(pendingMapNoticeMessage, pendingMapNoticeDuplicateIdentifier);
            pendingMapNoticeMessage = null;
            pendingMapNoticeDuplicateIdentifier = null;
        }

        private void RestoreMapCameraState()
        {
            if (activeMapCamera == null)
                return;

            activeMapCameraState.Restore(activeMapCamera);
            activeMapCamera = null;
        }

        private static Camera? ResolveMapCamera(Component controller)
        {
            var vCamTransform = GetTransformMember(controller, "_vCam");
            if (vCamTransform != null)
                return vCamTransform.GetComponent<Camera>() ?? vCamTransform.GetComponentInChildren<Camera>(true);

            return controller.GetComponentInChildren<Camera>(true) ?? GetLiveMainCamera();
        }

        private void HandleCameraPreCull(Camera camera)
        {
            if (settings == null || float.IsNaN(desiredVehicleDistance) || cachedVehicleCameras == null || !vehicleDebug.IsVehicleMode)
                return;

            foreach (var vehicleCamera in cachedVehicleCameras)
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                var vehicleCameraComponent = vehicleCamera.GetComponent<Camera>();
                var nestedCamera = vehicleCamera.GetComponentInChildren<Camera>(true);
                if (vehicleCameraComponent != camera && nestedCamera != camera)
                    continue;

                ApplyVehicleDistanceToSource(desiredVehicleDistance, settings.VehicleMaxZoom);
                ApplyVehicleCameraDistance(vehicleCamera.gameObject, desiredVehicleDistance, settings.VehicleMaxZoom);
            }
        }

        private static Camera? GetLiveMainCamera()
        {
            if (gameManagerType != null)
            {
                var getMainCameraMethod = gameManagerType.GetMethod("GetMainCamera", BindingFlags.Public | BindingFlags.Static);
                if (getMainCameraMethod?.Invoke(null, null) is Camera gameManagerCamera)
                    return gameManagerCamera;
            }

            return Camera.main;
        }

        private static void SetSavedMapZoom(float zoom)
        {
            var type = saveGameManagerType;
            if (type == null)
                return;

            var currentProperty = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            var current = currentProperty?.GetValue(null);
            if (current == null)
                return;

            SetMemberValue(current, "cityMapZoom", zoom);
        }

        private static void ShowPopup(string message, string? duplicateIdentifier = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    message,
                    null,
                    6f,
                    duplicateIdentifier,
                    null,
                    false,
                    false);
            }
            catch (Exception exception)
            {
                CameraToolsFileLogger.Log("Failed to show popup: " + exception.Message);
            }
        }

        private static GUIStyle CreateDebugOverlayStyle()
        {
            var style = new GUIStyle(GUI.skin.box);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = 14;
            style.normal.textColor = Color.white;
            return style;
        }

        private static Array? GetCinemachinePipeline(Type virtualCameraType, object virtualCamera)
        {
            var getPipelineMethod = virtualCameraType.GetMethod("GetComponentPipeline", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return getPipelineMethod?.Invoke(virtualCamera, null) as Array;
        }

        private static Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[]? types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                if (types == null)
                    continue;

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    if (TypeNameMatches(type, typeName))
                        return type;
                }
            }

            return null;
        }

        private static bool TypeNameMatches(Type type, string typeName)
        {
            if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                string.Equals(type.Name, typeName, StringComparison.Ordinal))
                return true;

            return type.FullName != null &&
                type.FullName.EndsWith("." + typeName, StringComparison.Ordinal);
        }

        private static bool IsCityMapOpen()
        {
            var cityMapType = CameraToolsRuntime.cityMapType;
            if (cityMapType == null)
                return false;

            var isOpenProperty = cityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return isOpenProperty?.GetValue(null) as bool? ?? false;
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

        private bool IsGameplayActive()
        {
            if (gameplayController != null && gameplayController.isActiveAndEnabled)
                return true;

            if (gameManagerController != null && gameManagerController.isActiveAndEnabled)
                return true;

            return false;
        }

        private static Transform? GetTransformMember(object target, string memberName)
        {
            if (!TryGetMemberValue(target, memberName, out var value))
                return null;

            return value as Transform;
        }

        private static Vector2 GetVector2Member(object target, string memberName)
        {
            if (!TryGetMemberValue(target, memberName, out var value) || value is not Vector2 vector)
                return default;

            return vector;
        }

        private static float GetFloatMember(object target, string memberName)
        {
            return TryGetFloatMember(target, memberName, out var value) ? value : 0f;
        }

        private static bool TryGetFloatMember(object target, string memberName, out float value)
        {
            value = 0f;
            if (!TryGetMemberValue(target, memberName, out var memberValue) || memberValue == null)
                return false;

            switch (memberValue)
            {
                case float floatValue:
                    value = floatValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetBoolMember(object target, string memberName, out bool value)
        {
            value = false;
            if (!TryGetMemberValue(target, memberName, out var memberValue) || memberValue == null || memberValue is not bool boolValue)
                return false;

            value = boolValue;
            return true;
        }

        private static bool TryGetFirstFloatMember(object target, string[] memberNames, out float value, out string foundMemberName)
        {
            foreach (var memberName in memberNames)
            {
                if (!TryGetFloatMember(target, memberName, out value))
                    continue;

                foundMemberName = memberName;
                return true;
            }

            value = 0f;
            foundMemberName = "none";
            return false;
        }

        private static bool SetFirstFloatMember(object target, string[] memberNames, float value)
        {
            foreach (var memberName in memberNames)
            {
                if (SetMemberValue(target, memberName, value))
                    return true;
            }

            return false;
        }

        private static bool TryGetMemberValue(object target, string memberName, out object? value)
        {
            value = null;
            if (target == null)
                return false;

            try
            {
                var type = target.GetType();
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    value = field.GetValue(target);
                    return true;
                }

                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property == null || !property.CanRead)
                    return false;

                value = property.GetValue(target, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetMemberValue(object target, string memberName, object value)
        {
            if (target == null)
                return false;

            try
            {
                var type = target.GetType();
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    field.SetValue(target, ConvertValue(value, field.FieldType));
                    return true;
                }

                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property == null || !property.CanWrite)
                    return false;

                property.SetValue(target, ConvertValue(value, property.PropertyType), null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (targetType.IsInstanceOfType(value))
                return value;

            if (targetType == typeof(float))
                return Convert.ToSingle(value);

            if (targetType == typeof(int))
                return Convert.ToInt32(value);

            if (targetType == typeof(bool))
                return Convert.ToBoolean(value);

            return value;
        }

        private readonly struct CameraState
        {
            private readonly float fieldOfView;
            private readonly bool orthographic;
            private readonly float orthographicSize;

            public CameraState(Camera camera)
            {
                orthographic = camera.orthographic;
                orthographicSize = camera.orthographicSize;
                fieldOfView = camera.fieldOfView;
            }

            public void Restore(Camera camera)
            {
                camera.orthographic = orthographic;
                camera.orthographicSize = orthographicSize;
                camera.fieldOfView = fieldOfView;
            }
        }

        private sealed class VehicleTarget
        {
            public VehicleTarget(MonoBehaviour? carController, object? vehicleController)
            {
                CarController = carController;
                VehicleController = vehicleController;
            }

            public MonoBehaviour? CarController { get; }

            public object? VehicleController { get; }
        }

        private sealed class VehicleDebugState
        {
            public bool ActiveCarFound;
            public bool CameraObjectFound;
            public string CarControllerTypeName = "none";
            public bool CinemachineFound;
            public float CurrentDistance;
            public string DistanceMemberName = "none";
            public bool IsInsideVehicle;
            public bool IsVehicleMode;
            public float LastAppliedDistance;
            public string LastApplySummary = string.Empty;
            public string LastResolutionSummary = string.Empty;
            public float ScrollDelta;
            public bool VehicleControllerFound;
            public string VehicleControllerTypeName = "none";
            public bool WasOverwritten;
        }
    }
}
