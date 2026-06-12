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
        private const float VehicleOverwriteThreshold = 0.5f;
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
        private static readonly string[] VehicleStateKeywords =
        {
            "player",
            "driver",
            "controlled",
            "active",
            "entered",
            "inside",
            "seated",
            "occupant",
            "controller",
            "camera",
            "vehicle"
        };
        private static readonly string[] VehicleCameraKeywords =
        {
            "cam",
            "camera",
            "distance",
            "zoom",
            "follow",
            "orbit",
            "chase",
            "view",
            "look",
            "third",
            "person",
            "vehicleToCam",
            "cameraDistance",
            "maxDistance",
            "minDistance"
        };

        private Camera? activeMapCamera;
        private CameraState activeMapCameraState;
        private Component[]? cachedVehicleCameras;
        private readonly Dictionary<int, Vector3> cachedVehicleFollowOffsets = new Dictionary<int, Vector3>();
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
        private PendingVcamDiagnostic? pendingVcamDiagnostic;
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
            runtime.cachedVehicleFollowOffsets.Clear();
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
            runtime.pendingVcamDiagnostic = null;
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
            ProcessPendingVcamDiagnostic();
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
            vehicleDebug.IsInsideVehicle = ComputeInsideVehicleState();
            vehicleDebug.IsVehicleMode = vehicleDebug.IsInsideVehicle || GetActiveVehicleCameraId(cachedVehicleCameras ?? Array.Empty<Component>()) != 0;
            vehicleDebug.CameraObjectFound = cachedVehicleCameras != null && cachedVehicleCameras.Length > 0;
            vehicleDebug.ActualDistance = settings == null ? 0f : ResolveCurrentVehicleDistance(settings.VehicleMaxZoom);
            UpdateActiveVehicleOffsetDebugState();
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
                var clampedDistance = Mathf.Clamp(distance, VehicleMinimumZoom, maxZoom);
                vehicleDebug.ActualDistance = clampedDistance;
                return clampedDistance;
            }

            vehicleDebug.DistanceMemberName = "not-found";
            return Mathf.Clamp(maxZoom, VehicleMinimumZoom, maxZoom);
        }

        private void UpdateActiveVehicleOffsetDebugState()
        {
            vehicleDebug.OriginalFollowOffset = "n/a";
            vehicleDebug.CurrentFollowOffset = "n/a";

            foreach (var vehicleCamera in cachedVehicleCameras ?? Array.Empty<Component>())
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                if (!TryGetVehicleCameraOffsetDebugInfo(vehicleCamera.gameObject, out var currentOffset, out var originalOffset))
                    continue;

                vehicleDebug.CurrentFollowOffset = currentOffset.ToString("F2");
                vehicleDebug.OriginalFollowOffset = originalOffset.ToString("F2");
                return;
            }
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
            vehicleDebug.WasOverwritten = Mathf.Abs(readBackDistance - clampedDistance) > VehicleOverwriteThreshold;

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
            if (Mathf.Abs(currentDistance - desiredVehicleDistance) <= VehicleOverwriteThreshold)
                return;

            vehicleDebug.WasOverwritten = true;
            var overwriteSummary =
                $"Vehicle zoom overwritten. desired={desiredVehicleDistance:0.##}, actual={currentDistance:0.##}, " +
                $"distanceMember={vehicleDebug.DistanceMemberName}, insideVehicle={vehicleDebug.IsInsideVehicle}";
            if (vehicleDebug.LastOverwriteSummary != overwriteSummary)
            {
                vehicleDebug.LastOverwriteSummary = overwriteSummary;
                CameraToolsFileLogger.Log(overwriteSummary);
            }
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

        private bool TryGetActiveVehicleBodyDistance(out float distance, out string memberName)
        {
            distance = 0f;
            memberName = "none";

            foreach (var vehicleCamera in cachedVehicleCameras ?? Array.Empty<Component>())
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                if (TryGetVehicleCameraBodyDistance(vehicleCamera.gameObject, out distance, out memberName))
                    return true;
            }

            return false;
        }

        private bool TryGetVehicleCameraOffsetDebugInfo(GameObject cameraObject, out Vector3 currentOffset, out Vector3 originalOffset)
        {
            currentOffset = default;
            originalOffset = default;

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
                    if (pipelineComponent == null || !IsFollowOffsetComponent(pipelineComponent))
                        continue;

                    if (!TryGetFollowOffset(pipelineComponent, out currentOffset))
                        continue;

                    originalOffset = GetOrCacheOriginalFollowOffset(pipelineComponent, currentOffset);
                    return true;
                }
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

        private static bool TryGetVehicleCameraBodyDistance(GameObject cameraObject, out float distance, out string memberName)
        {
            distance = 0f;
            memberName = "none";

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

                    if (TryGetPipelineComponentDistance(pipelineComponent, out distance, out memberName))
                        return true;
                }
            }

            return false;
        }

        private void ApplyVehicleCameraZoomLimits(GameObject cameraObject, float maxZoom)
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

                    ApplyPipelineZoomLimits(pipelineComponent, maxZoom);
                }
            }
        }

        private void ApplyVehicleCameraDistance(GameObject cameraObject, float distance, float maxZoom)
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

                    ApplyPipelineZoomLimits(pipelineComponent, maxZoom);
                    ApplyPipelineDistance(pipelineComponent, distance, maxZoom);
                }
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

        private void ApplyPipelineDistance(object pipelineComponent, float distance, float maxZoom)
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
                        var scaledOffset = originalOffset * (clampedDistance / originalMagnitude);
                        SetFollowOffset(pipelineComponent, scaledOffset);
                    }
                }
            }
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

        private bool ComputeInsideVehicleState()
        {
            if (vehicleTarget?.CarController != null &&
                TryGetBoolMember(vehicleTarget.CarController, "controlledByPlayer", out var controlledByPlayer) &&
                controlledByPlayer)
                return true;

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
            ResolveVehicleTarget(forceSearch: true, allowExpensiveSearch: true);
            UpdateVehicleDebugStateFromTarget();

            CameraToolsFileLogger.Log("=== Vehicle diagnostics dump start ===");
            CameraToolsFileLogger.Log($"insideVehicle={vehicleDebug.IsInsideVehicle}, vehicleMode={vehicleDebug.IsVehicleMode}, actualDistance={vehicleDebug.ActualDistance:0.##}, desiredDistance={desiredVehicleDistance:0.##}");

            DumpInterestingMembers("CarController state", vehicleTarget?.CarController, VehicleStateKeywords);
            DumpInterestingMembers("VehicleController state", vehicleTarget?.VehicleController, VehicleStateKeywords);
            DumpInterestingMembers("CarController camera members", vehicleTarget?.CarController, VehicleCameraKeywords);
            DumpInterestingMembers("VehicleController camera members", vehicleTarget?.VehicleController, VehicleCameraKeywords);

            if (gameManagerController != null)
                DumpInterestingMembers("GameManager vehicle refs", gameManagerController, VehicleStateKeywords);

            DumpActiveCameras();
            DumpVehicleCameraHierarchy();
            CameraToolsFileLogger.Log("=== Vehicle diagnostics dump end ===");
        }

        private void DumpActiveCameras()
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
                CameraToolsFileLogger.Log($"Camera.main: {GetHierarchyPath(mainCamera.transform)} fov={mainCamera.fieldOfView:0.##} enabled={mainCamera.enabled}");
            else
                CameraToolsFileLogger.Log("Camera.main: none");

            foreach (var camera in Camera.allCameras)
            {
                if (camera == null)
                    continue;

                CameraToolsFileLogger.Log($"Enabled Camera: path={GetHierarchyPath(camera.transform)}, enabled={camera.enabled}, fov={camera.fieldOfView:0.##}, depth={camera.depth:0.##}");
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
                CameraToolsFileLogger.Log(
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

                CameraToolsFileLogger.Log($"Cached vehicle camera root: {GetHierarchyPath(vehicleCamera.transform)} active={vehicleCamera.gameObject.activeInHierarchy}");
                DumpDetailedVehicleVcam(vehicleCamera);
                DumpComponentsForHierarchy(vehicleCamera.transform);
            }

            if (Camera.main != null)
            {
                CameraToolsFileLogger.Log("Camera.main hierarchy components:");
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

                CameraToolsFileLogger.Log($"Hierarchy component: path={GetHierarchyPath(component.transform)}, type={typeName}, enabled={FormatEnabled(component)}");
            }
        }

        private void ApplyVisualCameraDiagnostic()
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                var diagnosticFov = Mathf.Approximately(mainCamera.fieldOfView, 25f) ? 85f : 25f;
                mainCamera.fieldOfView = diagnosticFov;
                CameraToolsFileLogger.Log($"F12 camera poke applied to Camera.main: path={GetHierarchyPath(mainCamera.transform)}, fov={diagnosticFov:0.##}");
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
                CameraToolsFileLogger.Log(
                    $"F12 vcam poke applied: path={GetHierarchyPath(component.transform)}, beforeFov={(beforeLens.HasValue ? beforeLens.Value.ToString("0.##") : "n/a")}, afterFov={(afterLens.HasValue ? afterLens.Value.ToString("0.##") : "n/a")}, body={bodySummary}");

                pendingVcamDiagnostic = new PendingVcamDiagnostic(component, bodySummary);
                break;
            }
        }

        private void ProcessPendingVcamDiagnostic()
        {
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
            CameraToolsFileLogger.Log(
                $"F12 vcam poke readback: path={GetHierarchyPath(diagnostic.VirtualCamera.transform)}, fov={(lensFov.HasValue ? lensFov.Value.ToString("0.##") : "n/a")}, body={bodyReadback}");
            pendingVcamDiagnostic = null;
        }

        private void DumpInterestingMembers(string label, object? target, string[] keywords)
        {
            if (target == null)
            {
                CameraToolsFileLogger.Log($"{label}: none");
                return;
            }

            CameraToolsFileLogger.Log($"{label}: type={target.GetType().FullName}");
            foreach (var member in EnumerateInterestingMembers(target, keywords))
            {
                CameraToolsFileLogger.Log(
                    $"  {member.DeclaringType}.{member.Name} type={member.MemberType.Name} writable={member.Writable} value={FormatMemberValue(member.Value)}");
            }
        }

        private void DumpDetailedVehicleVcam(Component vehicleCamera)
        {
            CameraToolsFileLogger.Log($"Vehicle vcam detail: root={GetHierarchyPath(vehicleCamera.transform)}");
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
                CameraToolsFileLogger.Log(
                    $"VCAM: path={GetHierarchyPath(component.transform)}, active={component.gameObject.activeInHierarchy}, enabled={FormatEnabled(component)}, " +
                    $"priority={FormatMemberValue(priority)}, follow={FormatMemberValue(follow)}, lookAt={FormatMemberValue(lookAt)}");

                foreach (var sameGoComponent in component.gameObject.GetComponents<Component>())
                {
                    if (sameGoComponent == null)
                        continue;

                    CameraToolsFileLogger.Log($"  SameGO component: {sameGoComponent.GetType().FullName}");
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

                    CameraToolsFileLogger.Log($"  Pipeline component: {pipelineComponent.GetType().FullName}");
                    DumpInterestingMembers("  Pipeline members", pipelineComponent, VehicleCameraKeywords);
                }

                CameraToolsFileLogger.Log($"  Body component type: {bodyType}");
                CameraToolsFileLogger.Log($"  Aim component type: {aimType}");
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

        private static object? SafeRead(Func<object?> reader)
        {
            try
            {
                return reader();
            }
            catch (Exception exception)
            {
                return "<read failed: " + exception.GetType().Name + ">";
            }
        }

        private static string FormatMemberValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is Component component)
                return GetHierarchyPath(component.transform);

            if (value is GameObject gameObject)
                return GetHierarchyPath(gameObject.transform);

            return value.ToString() ?? value.GetType().Name;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string FormatEnabled(Component component)
        {
            return component is Behaviour behaviour ? behaviour.enabled.ToString() : "n/a";
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
            public float ActualDistance;
            public bool CameraObjectFound;
            public string CarControllerTypeName = "none";
            public bool CinemachineFound;
            public float CurrentDistance;
            public string CurrentFollowOffset = "n/a";
            public string DistanceMemberName = "none";
            public bool IsInsideVehicle;
            public bool IsVehicleMode;
            public float LastAppliedDistance;
            public string LastApplySummary = string.Empty;
            public string LastOverwriteSummary = string.Empty;
            public string OriginalFollowOffset = "n/a";
            public string LastResolutionSummary = string.Empty;
            public float ScrollDelta;
            public bool VehicleControllerFound;
            public string VehicleControllerTypeName = "none";
            public bool WasOverwritten;
        }

        private readonly struct InterestingMember
        {
            public InterestingMember(string declaringType, string name, Type memberType, bool writable, object? value)
            {
                DeclaringType = declaringType;
                Name = name;
                MemberType = memberType;
                Writable = writable;
                Value = value;
            }

            public string DeclaringType { get; }
            public string Name { get; }
            public Type MemberType { get; }
            public object? Value { get; }
            public bool Writable { get; }
        }

        private readonly struct PendingVcamDiagnostic
        {
            public PendingVcamDiagnostic(Component virtualCamera, string summary)
            {
                VirtualCamera = virtualCamera;
                Summary = summary;
            }

            public string Summary { get; }
            public Component VirtualCamera { get; }
        }
    }
}
