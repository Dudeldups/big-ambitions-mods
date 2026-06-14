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
    public sealed class CameraToolsRuntime : MonoBehaviour
    {
        private const float PitchStepPerMousePixel = 0.15f;
        private const float VehicleMinimumZoom = 6f;
        private const float VehicleZoomStepPerScrollTick = 4f;
        private const float VehicleForcedZoomStep = 20f;
        private const float GameplayMinimumZoom = 1.5f;
        private const float MapMinimumZoom = 120f;
        private const float MapScrollStepMultiplier = 7f;
        private const float MapScrollDeltaThreshold = 0.1f;
        private const float MapMinimumPitch = 45f;
        private const float MapMaximumPitch = 90f;
        private const float UiStateRefreshIntervalSeconds = 0.15f;
        private const float HiddenUiRefreshIntervalSeconds = 1f;
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
        private static readonly string[] HiddenUiIncludeKeywords =
        {
            "portrait",
            "avatar",
            "face",
            "objective",
            "objectives",
            "status",
            "topbar",
            "needs",
            "energy",
            "happiness",
            "hunger",
            "money",
            "day",
            "time",
            "bizphone",
            "phone",
            "smartphone",
            "vehicle",
            "car",
            "steering",
            "fuel",
            "gas",
            "autopark",
            "auto_park",
            "park",
            "sell",
            "rent",
            "rented",
            "lease",
            "owned",
            "owner",
            "icon",
            "marker",
            "mapicon",
            "worldicon",
            "circle",
            "help",
            "bug",
            "option",
            "building",
            "business",
            "location"
        };
        private static readonly string[] HiddenUiExcludeKeywords =
        {
            "menu",
            "dialog",
            "popup",
            "modal",
            "tooltip",
            "toast",
            "sleep",
            "bed",
            "timeskip",
            "time_skip",
            "skiptime",
            "fastforward",
            "fast_forward",
            "cancel",
            "dropdown",
            "scroll",
            "list",
            "phonecall",
            "notificationstack",
            "notificationitem"
        };

        private Camera? activeMapRenderCamera;
        private CameraState activeMapRenderCameraState;
        private Transform? activeMapVcamTransform;
        private Component? activeVehicleCameraRoot;
        private Component[]? cachedVehicleCameras;
        private readonly Dictionary<int, Vector3> cachedVehicleFollowOffsets = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, object[]> cachedVehiclePipelineComponents = new Dictionary<int, object[]>();
        private readonly Dictionary<int, object[]> cachedVehicleZoomComponents = new Dictionary<int, object[]>();
        private MonoBehaviour? configuredGameplayController;
        private MonoBehaviour? configuredMapController;
        private ModContext? context;
        private float desiredVehicleDistance;
        private MonoBehaviour? gameManagerController;
        private MonoBehaviour? gameplayController;
        private bool hasInitializedMapDistanceForCurrentOpen;
        private bool hasManualGameplayPitch;
        private bool hasManualMapPitch;
        private bool hasShownGameplayPitchHint;
        private bool isUiHidden;
        private bool isGameplayUiBlocked;
        private bool isTrackingMapRightMousePitch;
        private bool isTrackingRightMousePitch;
        private int lastActiveVehicleCameraId;
        private int lastConfiguredGameplayControllerId;
        private int lastConfiguredMapControllerId;
        private int lastAppliedGameplayMaxZoom;
        private int lastAppliedMapMaxZoom;
        private float lastAppliedMapDistanceSetting;
        private float desiredMapDistance;
        private float lastAppliedVehicleMaxZoom;
        private float lastRightMouseY;
        private float lastVehicleControllerSearchTime;
        private MonoBehaviour? mapController;
        private float manualMapPitch;
        private float manualGameplayPitch;
        private bool needsVehicleDistanceReapply;
        private float nextUiStateRefreshTime;
        private float nextHiddenUiRefreshTime;
        private PendingVcamDiagnostic? pendingVcamDiagnostic;
        private bool scenicViewEnabled;
        private RendererState[] scenicViewRendererStates = Array.Empty<RendererState>();
        private Component? scenicViewTargetRoot;
        private GameObjectActiveState[] hiddenUiStates = Array.Empty<GameObjectActiveState>();
        private CameraToolsSettings? settings;
        private bool showVehicleDebugOverlay;
        private bool wasCityMapOpen;
        private Vector3? lastAppliedGameplayOffset;
        private float mapDebugCurrentDistance;
        private float mapDebugDesiredDistance;
        private float mapDebugRawScrollDelta;
        private float mapDebugVanillaDelta;
        private string lastMapApplyLogSummary = string.Empty;
        private string lastMapLifecycleLogSummary = string.Empty;
        private string lastMapDebugLogSummary = string.Empty;
        private VehicleDebugState vehicleDebug = new VehicleDebugState();
        private VehicleTarget? vehicleTarget;
        private MonoBehaviour? cachedDialogUiController;
        private MonoBehaviour? cachedFullMenuController;
        private MonoBehaviour? cachedMiniMenuController;
        private static Type? cameraMouseDragType;
        private static Type? carControllerType;
        private static Type? cityMapType;
        private static Type? cityMapCamType;
        private static Type? cinemachineBrainType;
        private static Type? cinematachineVirtualCameraType;
        private static GUIStyle? debugOverlayStyle;
        private static Type? dialogUiType;
        private static Type? fullMenuType;
        private static Type? gameManagerType;
        private static Type? miniMenuType;
        private static Type? pedestrianCamType;
        private static Type? saveGameManagerType;
        private static Type? vehicleControllerType;
        private static readonly Dictionary<string, FieldInfo?> fieldCache = new Dictionary<string, FieldInfo?>();
        private static readonly Dictionary<string, PropertyInfo?> propertyCache = new Dictionary<string, PropertyInfo?>();
        private static readonly object memberCacheLock = new object();
        private static bool cameraToolsDebugEnabled;
        private static bool vehicleDebugLoggingEnabled;

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
            runtime.activeVehicleCameraRoot = null;
            runtime.cachedVehicleCameras = null;
            runtime.cachedVehicleFollowOffsets.Clear();
            runtime.cachedVehiclePipelineComponents.Clear();
            runtime.cachedVehicleZoomComponents.Clear();
            runtime.configuredGameplayController = null;
            runtime.configuredMapController = null;
            runtime.desiredVehicleDistance = float.NaN;
            runtime.gameManagerController = null;
            runtime.gameplayController = null;
            runtime.hasInitializedMapDistanceForCurrentOpen = false;
            runtime.hasManualGameplayPitch = false;
            runtime.hasManualMapPitch = false;
            runtime.hasShownGameplayPitchHint = false;
            runtime.isUiHidden = false;
            runtime.isTrackingMapRightMousePitch = false;
            runtime.isTrackingRightMousePitch = false;
            runtime.lastActiveVehicleCameraId = 0;
            runtime.lastAppliedGameplayMaxZoom = int.MinValue;
            runtime.lastAppliedMapMaxZoom = int.MinValue;
            runtime.lastAppliedMapDistanceSetting = float.NaN;
            runtime.desiredMapDistance = float.NaN;
            runtime.lastAppliedVehicleMaxZoom = float.NaN;
            runtime.lastConfiguredGameplayControllerId = 0;
            runtime.lastConfiguredMapControllerId = 0;
            runtime.lastAppliedGameplayOffset = null;
            runtime.lastRightMouseY = 0f;
            runtime.lastVehicleControllerSearchTime = float.NegativeInfinity;
            runtime.mapController = null;
            runtime.needsVehicleDistanceReapply = false;
            runtime.nextUiStateRefreshTime = 0f;
            runtime.nextHiddenUiRefreshTime = 0f;
            runtime.pendingVcamDiagnostic = null;
            runtime.scenicViewEnabled = false;
            runtime.scenicViewRendererStates = Array.Empty<RendererState>();
            runtime.scenicViewTargetRoot = null;
            runtime.hiddenUiStates = Array.Empty<GameObjectActiveState>();
            runtime.showVehicleDebugOverlay = settings.EnableCameraToolsDebug && settings.EnableVehicleDebugOverlay;
            runtime.vehicleTarget = null;
            runtime.vehicleDebug = new VehicleDebugState();
            runtime.wasCityMapOpen = false;
            runtime.cachedDialogUiController = null;
            runtime.cachedFullMenuController = null;
            runtime.cachedMiniMenuController = null;
            runtime.isGameplayUiBlocked = false;
            runtime.lastMapApplyLogSummary = string.Empty;
            runtime.lastMapLifecycleLogSummary = string.Empty;
            runtime.lastMapDebugLogSummary = string.Empty;
            cameraToolsDebugEnabled = settings.EnableCameraToolsDebug;
            vehicleDebugLoggingEnabled = settings.EnableCameraToolsDebug && settings.EnableVehicleDebugLogging;

            pedestrianCamType ??= FindType(PedestrianCamTypeName);
            cityMapType ??= FindType("CityMap");
            cityMapCamType ??= FindType(CityMapCamTypeName);
            carControllerType ??= FindType(CarControllerTypeName);
            vehicleControllerType ??= FindType(VehicleControllerTypeName);
            cameraMouseDragType ??= FindType(CameraMouseDragTypeName);
            cinemachineBrainType ??= FindType("CinemachineBrain");
            cinematachineVirtualCameraType ??= FindType("CinemachineVirtualCamera");
            miniMenuType ??= FindType("UI.MiniMenu.MiniMenu");
            fullMenuType ??= FindType("UI.Smartphone.FullMenu");
            dialogUiType ??= FindType("UI.Dialog.DialogUI");
            gameManagerType ??= FindType(GameManagerTypeName);
            saveGameManagerType ??= FindType(SaveGameManagerTypeName);

            LogVehicleDebug("CameraTools runtime initialized.");
            return runtime;
        }

        public void Shutdown()
        {
            RestoreScenicView();
            RestoreHiddenUi();
            RestoreMapCameraState();
            LogVehicleDebug("CameraTools runtime shutting down.");
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

            cameraToolsDebugEnabled = settings.EnableCameraToolsDebug;
            vehicleDebugLoggingEnabled = settings.EnableCameraToolsDebug && settings.EnableVehicleDebugLogging;
            if (!cameraToolsDebugEnabled)
                showVehicleDebugOverlay = false;

            var cityMapOpen = IsCityMapOpen();
            EnsureControllers(cityMapOpen);
            HandleScenicViewHotkey();
            RefreshScenicViewState();
            HandleHideUiHotkey();
            RefreshHiddenUiState();
            HandleVehicleDebugHotkeys();
            var gameplayActive = IsGameplayActive();

            if (!cityMapOpen)
                ApplyGameplayTweaks();
            if (!cityMapOpen)
                ApplyVehicleTweaks(gameplayActive);
            else
                ResetVehicleRuntimeState();
            ApplyMapTweaks(cityMapOpen);
            if (cameraToolsDebugEnabled)
                ProcessPendingVcamDiagnostic();
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

        private void EnsureControllers(bool cityMapOpen)
        {
            if (gameplayController == null || !gameplayController.isActiveAndEnabled)
            {
                gameplayController = FindFirstActiveController(pedestrianCamType, includeInactive: false);
                if (gameplayController != configuredGameplayController)
                    configuredGameplayController = null;
            }

            if (gameplayController != null && gameplayController != configuredGameplayController)
            {
                lastConfiguredGameplayControllerId = 0;
                lastAppliedGameplayOffset = null;
                ConfigureGameplayController();
                configuredGameplayController = gameplayController;
            }

            if (cityMapOpen && (mapController == null || !mapController.isActiveAndEnabled))
            {
                mapController = FindFirstActiveController(cityMapCamType, includeInactive: false) ??
                    FindFirstActiveController(cityMapCamType, includeInactive: true);
                if (mapController != configuredMapController)
                    configuredMapController = null;
            }

            if (mapController != null && mapController != configuredMapController)
            {
                lastConfiguredMapControllerId = 0;
                ConfigureMapController();
                configuredMapController = mapController;
            }

            if (gameManagerController == null || !gameManagerController.isActiveAndEnabled)
            {
                gameManagerController = FindFirstActiveController(gameManagerType, includeInactive: false) ??
                    FindFirstActiveController(gameManagerType, includeInactive: true);
                InvalidateVehicleCameraCaches();
            }
        }

        private static MonoBehaviour? FindFirstActiveController(Type? type, bool includeInactive)
        {
            if (type == null)
                return null;

            if (!includeInactive)
            {
                foreach (var obj in Object.FindObjectsOfType(type))
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

            var controllerId = gameplayController.GetInstanceID();
            if (lastAppliedGameplayMaxZoom == settings.GameplayMaxZoom &&
                controllerId == lastConfiguredGameplayControllerId)
                return;

            var bounds = GetVector2Member(gameplayController, "minMaxDistance");
            bounds.x = Mathf.Min(bounds.x, GameplayMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.GameplayMaxZoom);
            SetMemberValue(gameplayController, "minMaxDistance", bounds);
            SetMemberValue(gameplayController, "blockCameraZoom", false);
            lastAppliedGameplayMaxZoom = settings.GameplayMaxZoom;
            lastConfiguredGameplayControllerId = controllerId;

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
            if (!lastAppliedGameplayOffset.HasValue || lastAppliedGameplayOffset.Value != offset)
            {
                SetMemberValue(gameplayController, "offset", offset);
                lastAppliedGameplayOffset = offset;
            }
        }

        private void ConfigureMapController()
        {
            if (settings == null || mapController == null || !settings.EnableMapTopDown)
                return;

            var controllerId = mapController.GetInstanceID();
            var bounds = GetVector2Member(mapController, "minMaxDistance");
            if (lastAppliedMapMaxZoom != settings.MapDistance || controllerId != lastConfiguredMapControllerId)
            {
                bounds.x = Mathf.Min(bounds.x, MapMinimumZoom);
                bounds.y = Mathf.Max(bounds.y, settings.MapDistance);
                SetMemberValue(mapController, "minMaxDistance", bounds);
                lastAppliedMapMaxZoom = settings.MapDistance;
                lastConfiguredMapControllerId = controllerId;
            }

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

            if (cameraToolsDebugEnabled && wantsVehicleWork)
                ResolveVehicleTarget(forceSearch: false, allowExpensiveSearch: true);
            else if (cameraToolsDebugEnabled)
                UpdateVehicleDebugStateFromTarget();

            if (float.IsNaN(desiredVehicleDistance))
            {
                desiredVehicleDistance = ResolveCurrentVehicleCameraDistance(settings.VehicleMaxZoom);
                if (cameraToolsDebugEnabled)
                    vehicleDebug.CurrentDistance = desiredVehicleDistance;
            }

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

        private void HandleScenicViewHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.ScenicViewHotkey))
                return;

            scenicViewEnabled = !scenicViewEnabled;
            if (scenicViewEnabled)
            {
                ApplyScenicView();
                ShowPopup("Scenic view enabled.", "cameratools_scenic_view_enabled");
            }
            else
            {
                RestoreScenicView();
                ShowPopup("Scenic view disabled.", "cameratools_scenic_view_disabled");
            }
        }

        private void RefreshScenicViewState()
        {
            if (!scenicViewEnabled)
            {
                if (scenicViewRendererStates.Length > 0)
                    RestoreScenicView();

                return;
            }

            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            if (scenicViewTargetRoot == null || scenicViewTargetRoot != playerController)
            {
                ApplyScenicView();
                return;
            }

            var currentRenderers = playerController.GetComponentsInChildren<Renderer>(true);
            if (currentRenderers.Length != scenicViewRendererStates.Length)
            {
                ApplyScenicView();
                return;
            }

            foreach (var state in scenicViewRendererStates)
            {
                if (state.Renderer != null && state.Renderer.enabled)
                    state.Renderer.enabled = false;
            }
        }

        private void ApplyScenicView()
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            RestoreScenicView();

            var renderers = playerController.GetComponentsInChildren<Renderer>(true);
            var states = new List<RendererState>(renderers.Length);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                states.Add(new RendererState(renderer, renderer.enabled));
                renderer.enabled = false;
            }

            scenicViewTargetRoot = playerController;
            scenicViewRendererStates = states.ToArray();
        }

        private void RestoreScenicView()
        {
            foreach (var state in scenicViewRendererStates)
            {
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }

            scenicViewRendererStates = Array.Empty<RendererState>();
            scenicViewTargetRoot = null;
        }

        private void HandleHideUiHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.HideUiHotkey))
                return;

            isUiHidden = !isUiHidden;
            if (isUiHidden)
                ApplyHiddenUi();
            else
                RestoreHiddenUi();
        }

        private void RefreshHiddenUiState()
        {
            if (!isUiHidden)
            {
                if (hiddenUiStates.Length > 0)
                    RestoreHiddenUi();

                return;
            }

            var needsRefresh = hiddenUiStates.Length == 0;
            if (!needsRefresh)
            {
                foreach (var state in hiddenUiStates)
                {
                    if (state.Target == null)
                    {
                        needsRefresh = true;
                        break;
                    }

                    if (state.Target.activeSelf)
                        state.Target.SetActive(false);
                }
            }

            if (!needsRefresh || Time.unscaledTime < nextHiddenUiRefreshTime)
                return;

            ApplyHiddenUi();
        }

        private void ApplyHiddenUi()
        {
            RestoreHiddenUi();

            var targets = ResolveHiddenUiTargets();
            if (targets.Count == 0)
            {
                nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
                return;
            }

            var states = new List<GameObjectActiveState>(targets.Count);
            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                states.Add(new GameObjectActiveState(target, target.activeSelf));
                target.SetActive(false);
            }

            hiddenUiStates = states.ToArray();
            nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
        }

        private void RestoreHiddenUi()
        {
            foreach (var state in hiddenUiStates)
            {
                if (state.Target != null)
                    state.Target.SetActive(state.WasActive);
            }

            hiddenUiStates = Array.Empty<GameObjectActiveState>();
        }

        private static List<GameObject> ResolveHiddenUiTargets()
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();
            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rectTransform == null)
                    continue;

                var gameObject = rectTransform.gameObject;
                if (gameObject == null || gameObject.hideFlags != HideFlags.None || !gameObject.activeInHierarchy)
                    continue;

                if (!ShouldHideUiTransform(rectTransform))
                    continue;

                if (IsLikelyWorldMarker(rectTransform))
                {
                    TryAddHiddenUiTarget(targets, seen, ResolveWorldMarkerRoot(rectTransform).gameObject);
                    continue;
                }

                if (IsLikelyFixedHudRegion(rectTransform))
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    TryAddHiddenUiTarget(targets, seen, ResolveFixedHudRoot(rectTransform).gameObject);
                    continue;
                }

                TryAddHiddenUiTarget(targets, seen, gameObject);
            }

            return FilterNestedUiTargets(targets);
        }

        private static void TryAddHiddenUiTarget(List<GameObject> targets, HashSet<int> seen, GameObject? target)
        {
            if (target == null)
                return;

            var id = target.GetInstanceID();
            if (!seen.Add(id))
                return;

            targets.Add(target);
        }

        private static bool ShouldHideUiTransform(RectTransform transform)
        {
            if (transform.GetComponentInParent<Canvas>(true) == null)
                return false;

            var path = GetHierarchyPath(transform).ToLowerInvariant();
            if (path.IndexOf("bizphone", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            return ContainsAny(path, HiddenUiIncludeKeywords) ||
                IsLikelyFixedHudRegion(transform) ||
                IsLikelyWorldMarker(transform);
        }

        private static bool IsLikelyFixedHudRegion(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width > Screen.width * 0.85f || height > Screen.height * 0.7f)
                return false;

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            var normalizedX = centerX / Screen.width;
            var normalizedY = centerY / Screen.height;

            var isTopLeftHud = normalizedX <= 0.25f && normalizedY >= 0.7f;
            var isTopCenterHud = normalizedX >= 0.25f && normalizedX <= 0.75f && normalizedY >= 0.75f;
            var isTopRightHud = normalizedX >= 0.75f && normalizedY >= 0.7f;
            var isLeftSideHud = normalizedX <= 0.3f && normalizedY >= 0.25f && normalizedY <= 0.7f;
            var isBottomRightHud = normalizedX >= 0.55f && normalizedY <= 0.42f;
            var isUpperMiddleSupportPanel = normalizedX >= 0.2f && normalizedX <= 0.8f && normalizedY >= 0.5f && normalizedY <= 0.78f;
            var isVehicleActionPanel = normalizedX >= 0.2f && normalizedX <= 0.8f && normalizedY >= 0.68f && normalizedY <= 0.9f;

            return isTopLeftHud || isTopCenterHud || isTopRightHud || isLeftSideHud || isBottomRightHud || isUpperMiddleSupportPanel || isVehicleActionPanel;
        }

        private static bool IsLikelyWorldMarker(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width > Screen.width * 0.18f || height > Screen.height * 0.18f)
                return false;

            var hasUiGraphic = HasGraphicInMarkerHierarchy(rectTransform);

            return hasUiGraphic;
        }

        private static RectTransform ResolveWorldMarkerRoot(RectTransform rectTransform)
        {
            var best = rectTransform;
            var current = rectTransform;
            var climbCount = 0;
            while (current.parent is RectTransform parentRect &&
                parentRect.GetComponentInParent<Canvas>(true) != null &&
                !ContainsAny(GetHierarchyPath(parentRect).ToLowerInvariant(), HiddenUiExcludeKeywords) &&
                TryGetScreenRect(parentRect, out var minX, out var minY, out var maxX, out var maxY))
            {
                var width = maxX - minX;
                var height = maxY - minY;
                if (width > Screen.width * 0.18f || height > Screen.height * 0.18f)
                    break;

                best = parentRect;
                current = parentRect;
                climbCount++;
                if (climbCount >= 3)
                    break;
            }

            return best;
        }

        private static RectTransform ResolveFixedHudRoot(RectTransform rectTransform)
        {
            var best = rectTransform;
            var current = rectTransform;
            var climbCount = 0;
            while (current.parent is RectTransform parentRect &&
                parentRect.GetComponentInParent<Canvas>(true) != null &&
                !ContainsAny(GetHierarchyPath(parentRect).ToLowerInvariant(), HiddenUiExcludeKeywords) &&
                TryGetScreenRect(parentRect, out var minX, out var minY, out var maxX, out var maxY))
            {
                var width = maxX - minX;
                var height = maxY - minY;
                if (width > Screen.width * 0.9f || height > Screen.height * 0.45f)
                    break;

                best = parentRect;
                current = parentRect;
                climbCount++;
                if (climbCount >= 4)
                    break;
            }

            return best;
        }

        private static bool HasGraphicInMarkerHierarchy(RectTransform rectTransform)
        {
            if (rectTransform.GetComponent("Image") != null ||
                rectTransform.GetComponent("RawImage") != null ||
                rectTransform.GetComponent("TMP_Text") != null ||
                rectTransform.GetComponent("TextMeshProUGUI") != null)
                return true;

            foreach (Transform child in rectTransform)
            {
                if (child is not RectTransform childRect)
                    continue;

                if (!TryGetScreenRect(childRect, out _, out _, out var childMaxX, out var childMaxY))
                    continue;

                if (childMaxX <= 0f && childMaxY <= 0f)
                    continue;

                if (childRect.GetComponent("Image") != null ||
                    childRect.GetComponent("RawImage") != null ||
                    childRect.GetComponent("TMP_Text") != null ||
                    childRect.GetComponent("TextMeshProUGUI") != null)
                    return true;
            }

            return false;
        }

        private static bool TryGetScreenRect(RectTransform rectTransform, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = 0f;
            minY = 0f;
            maxX = 0f;
            maxY = 0f;
            if (Screen.width <= 0 || Screen.height <= 0)
                return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            minX = corners[0].x;
            minY = corners[0].y;
            maxX = corners[2].x;
            maxY = corners[2].y;
            return maxX - minX > 1f && maxY - minY > 1f;
        }

        private static List<GameObject> FilterNestedUiTargets(List<GameObject> targets)
        {
            var filtered = new List<GameObject>(targets.Count);
            for (var i = 0; i < targets.Count; i++)
            {
                var candidate = targets[i];
                if (candidate == null)
                    continue;

                var isChildOfSelectedTarget = false;
                for (var j = 0; j < targets.Count; j++)
                {
                    if (i == j)
                        continue;

                    var other = targets[j];
                    if (other == null)
                        continue;

                    if (candidate.transform.IsChildOf(other.transform))
                    {
                        isChildOfSelectedTarget = true;
                        break;
                    }
                }

                if (!isChildOfSelectedTarget)
                    filtered.Add(candidate);
            }

            return filtered;
        }

        private static bool ContainsAny(string source, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
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

        private bool IsGameplayInputBlockedByUi()
        {
            if (Time.unscaledTime < nextUiStateRefreshTime)
                return isGameplayUiBlocked;

            nextUiStateRefreshTime = Time.unscaledTime + UiStateRefreshIntervalSeconds;
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                if (eventSystem.IsPointerOverGameObject())
                    return isGameplayUiBlocked = true;

                var selectedGameObject = eventSystem.currentSelectedGameObject;
                if (selectedGameObject != null && selectedGameObject.activeInHierarchy)
                    return isGameplayUiBlocked = true;
            }

            if (IsCachedUiOpen(ref cachedMiniMenuController, miniMenuType, "IsOpen"))
                return isGameplayUiBlocked = true;

            if (IsCachedUiOpen(ref cachedFullMenuController, fullMenuType, "IsOpen"))
                return isGameplayUiBlocked = true;

            if (IsDialogPanelOpen())
                return isGameplayUiBlocked = true;

            isGameplayUiBlocked = false;
            return false;
        }

        private bool IsCachedUiOpen(ref MonoBehaviour? cachedController, Type? type, string propertyName)
        {
            if (cachedController == null || !cachedController.isActiveAndEnabled)
                cachedController = FindFirstActiveController(type, includeInactive: false);

            if (cachedController == null)
                return false;

            return TryGetBoolMember(cachedController, propertyName, out var isOpen) && isOpen;
        }

        private bool IsDialogPanelOpen()
        {
            if (dialogUiType == null)
                return false;

            if (cachedDialogUiController == null || !cachedDialogUiController.isActiveAndEnabled)
                cachedDialogUiController = FindFirstActiveController(dialogUiType, includeInactive: false);

            return cachedDialogUiController != null &&
                TryGetBoolMember(cachedDialogUiController, "isPanelOpen", out var isPanelOpen) &&
                isPanelOpen;
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
                ApplyPipelineDistance(pipelineComponent, distance, maxZoom);
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

        private void ApplyMapTweaks(bool cityMapOpen)
        {
            if (cameraToolsDebugEnabled)
            {
                var lifecycleSummary = $"Map state: cityMapOpen={cityMapOpen}, mapController={(mapController == null ? "null" : mapController.GetType().FullName)}, activeMapRenderCamera={(activeMapRenderCamera == null ? "null" : activeMapRenderCamera.name)}, activeMapVcam={(activeMapVcamTransform == null ? "null" : activeMapVcamTransform.name)}";
                if (lastMapLifecycleLogSummary != lifecycleSummary)
                {
                    lastMapLifecycleLogSummary = lifecycleSummary;
                    LogVehicleDebug(lifecycleSummary);
                }
            }

            if (cityMapOpen && !wasCityMapOpen)
            {
                hasInitializedMapDistanceForCurrentOpen = false;
                hasManualMapPitch = false;
                isTrackingMapRightMousePitch = false;
                lastConfiguredMapControllerId = 0;
                desiredMapDistance = float.NaN;
                if (cameraToolsDebugEnabled)
                    LogVehicleDebug("Map opened: resetting desiredMapDistance.");
            }

            if (!cityMapOpen)
            {
                hasInitializedMapDistanceForCurrentOpen = false;
                desiredMapDistance = float.NaN;
                isTrackingMapRightMousePitch = false;
                if (cameraToolsDebugEnabled && wasCityMapOpen)
                    LogVehicleDebug("Map closed: clearing desiredMapDistance.");
            }

            wasCityMapOpen = cityMapOpen;

            if (settings == null || mapController == null || !settings.EnableMapTopDown)
            {
                RestoreMapCameraState();
                if (cameraToolsDebugEnabled && cityMapOpen)
                    LogVehicleDebug($"Map early exit: settingsNull={settings == null}, mapControllerNull={mapController == null}, mapTopDownEnabled={(settings != null && settings.EnableMapTopDown)}");
                return;
            }

            ConfigureMapController();

            var mapVcamTransform = ResolveMapVcamTransform(mapController);
            var mapRenderCamera = GetLiveMainCamera();
            if (mapVcamTransform == null && mapRenderCamera == null)
            {
                if (cameraToolsDebugEnabled)
                    LogVehicleDebug("Map early exit: ResolveMapVcamTransform and render camera both returned null.");
                return;
            }

            if (activeMapRenderCamera != mapRenderCamera)
            {
                RestoreMapCameraState();
                activeMapRenderCamera = mapRenderCamera;
                if (mapRenderCamera != null)
                    activeMapRenderCameraState = new CameraState(mapRenderCamera);
            }

            activeMapVcamTransform = mapVcamTransform;

            var bounds = GetVector2Member(mapController, "minMaxDistance");
            var currentDistance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
            if (float.IsNaN(desiredMapDistance))
                desiredMapDistance = Mathf.Clamp(currentDistance, bounds.x, bounds.y);

            if (!hasManualMapPitch)
                manualMapPitch = Mathf.Clamp(settings.MapPitch, MapMinimumPitch, MapMaximumPitch);

            if (Input.GetMouseButtonDown(1))
            {
                isTrackingMapRightMousePitch = true;
                lastRightMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(1) && isTrackingMapRightMousePitch)
            {
                var currentMouseY = Input.mousePosition.y;
                var deltaY = currentMouseY - lastRightMouseY;
                lastRightMouseY = currentMouseY;

                if (Mathf.Abs(deltaY) > Mathf.Epsilon)
                {
                    manualMapPitch = Mathf.Clamp(manualMapPitch - deltaY * PitchStepPerMousePixel, MapMinimumPitch, MapMaximumPitch);
                    hasManualMapPitch = true;
                }
            }

            if (Input.GetMouseButtonUp(1))
                isTrackingMapRightMousePitch = false;

            var rawScrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(rawScrollDelta) <= Mathf.Epsilon)
                rawScrollDelta = Input.GetAxis("Mouse ScrollWheel") * 120f;

            var vanillaDelta = currentDistance - desiredMapDistance;
            if (Mathf.Abs(vanillaDelta) > MapScrollDeltaThreshold)
            {
                desiredMapDistance = Mathf.Clamp(
                    desiredMapDistance + (vanillaDelta * MapScrollStepMultiplier),
                    bounds.x,
                    bounds.y);
            }

            desiredMapDistance = Mathf.Clamp(desiredMapDistance, bounds.x, bounds.y);
            if (cameraToolsDebugEnabled)
            {
                mapDebugCurrentDistance = currentDistance;
                mapDebugDesiredDistance = desiredMapDistance;
                mapDebugRawScrollDelta = rawScrollDelta;
                mapDebugVanillaDelta = vanillaDelta;

                var mapSummary =
                    $"Map zoom input: currentDistance={mapDebugCurrentDistance:0.##}, desiredMapDistance={mapDebugDesiredDistance:0.##}, " +
                    $"vanillaDelta={mapDebugVanillaDelta:0.##}, rawScrollDelta={mapDebugRawScrollDelta:0.##}";
                if (lastMapDebugLogSummary != mapSummary &&
                    (Mathf.Abs(mapDebugVanillaDelta) > MapScrollDeltaThreshold || Mathf.Abs(mapDebugRawScrollDelta) > Mathf.Epsilon))
                {
                    lastMapDebugLogSummary = mapSummary;
                    LogVehicleDebug(mapSummary);
                }
            }
            ApplyMapCameraState();
        }

        private void ApplyMapCameraState()
        {
            if (settings == null || mapController == null || float.IsNaN(desiredMapDistance))
                return;

            var bounds = GetVector2Member(mapController, "minMaxDistance");
            var distance = Mathf.Clamp(desiredMapDistance, bounds.x, bounds.y);
            desiredMapDistance = distance;
            SetMemberValue(mapController, "distance", distance);
            SetSavedMapZoom(distance);
            if (cameraToolsDebugEnabled)
            {
                var readBackDistance = Mathf.Clamp(GetFloatMember(mapController, "distance"), bounds.x, bounds.y);
                var applySummary =
                    $"Map zoom apply: desiredMapDistance={desiredMapDistance:0.##}, readBackDistance={readBackDistance:0.##}, activeMapRenderCamera={(activeMapRenderCamera == null ? "null" : activeMapRenderCamera.name)}, activeMapVcam={(activeMapVcamTransform == null ? "null" : activeMapVcamTransform.name)}";
                if (lastMapApplyLogSummary != applySummary)
                {
                    lastMapApplyLogSummary = applySummary;
                    LogVehicleDebug(applySummary);
                }
            }

            var currentAngle = GetFloatMember(mapController, "_currentAngle");
            var pitch = Mathf.Clamp(hasManualMapPitch ? manualMapPitch : settings.MapPitch, MapMinimumPitch, MapMaximumPitch);
            var pitchRadians = pitch * Mathf.Deg2Rad;
            var height = distance * Mathf.Sin(pitchRadians);
            var horizontalRadius = Mathf.Max(0.05f, distance * Mathf.Cos(pitchRadians));
            var orbit = Quaternion.Euler(0f, currentAngle, 0f) * new Vector3(0f, height, -horizontalRadius);
            var rootPosition = mapController.transform.position;
            var targetPosition = rootPosition + orbit;
            var upAxis = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward;
            var targetRotation = Quaternion.LookRotation(rootPosition - targetPosition, upAxis);

            var vCamTransform = activeMapVcamTransform ?? ResolveMapVcamTransform(mapController);
            if (vCamTransform != null)
            {
                activeMapVcamTransform = vCamTransform;
                vCamTransform.position = targetPosition;
                vCamTransform.rotation = targetRotation;
            }

            if (activeMapRenderCamera != null)
            {
                activeMapRenderCamera.transform.position = targetPosition;
                activeMapRenderCamera.transform.rotation = targetRotation;
            }
        }

        private void RestoreMapCameraState()
        {
            if (activeMapRenderCamera != null)
            {
                activeMapRenderCameraState.Restore(activeMapRenderCamera);
                activeMapRenderCamera = null;
            }

            activeMapVcamTransform = null;
        }

        private Transform? ResolveMapVcamTransform(Component controller)
        {
            var vCamTransform = GetTransformMember(controller, "_vCam");
            if (vCamTransform != null)
                return vCamTransform;

            if (TryGetMemberValue(controller, "_vCam", out var vCamValue) && vCamValue != null)
            {
                if (vCamValue is Transform transform)
                    return transform;

                if (vCamValue is Component component)
                    return component.transform;

                if (vCamValue is GameObject gameObject)
                    return gameObject.transform;
            }

            if (gameManagerController != null &&
                TryGetMemberValue(gameManagerController, "citymapCamera", out var cityMapCameraValue) &&
                cityMapCameraValue != null)
            {
                if (cityMapCameraValue is Transform cityMapTransform)
                    return cityMapTransform;

                if (cityMapCameraValue is Component cityMapComponent)
                    return cityMapComponent.transform;

                if (cityMapCameraValue is GameObject cityMapObject)
                    return cityMapObject.transform;
            }

            return null;
        }

        private void HandleCameraPreCull(Camera camera)
        {
            if (settings == null)
                return;

            if (activeMapRenderCamera != null && camera == activeMapRenderCamera)
                ApplyMapCameraState();

            if (!cameraToolsDebugEnabled ||
                !needsVehicleDistanceReapply ||
                float.IsNaN(desiredVehicleDistance) ||
                cachedVehicleCameras == null ||
                !vehicleDebug.IsVehicleMode)
                return;

            foreach (var vehicleCamera in cachedVehicleCameras)
            {
                if (vehicleCamera == null || !vehicleCamera.gameObject.activeInHierarchy)
                    continue;

                var vehicleCameraComponent = vehicleCamera.GetComponent<Camera>();
                var nestedCamera = vehicleCamera.GetComponentInChildren<Camera>(true);
                if (vehicleCameraComponent != camera && nestedCamera != camera)
                    continue;

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
                LogVehicleDebug("Failed to show popup: " + exception.Message);
            }
        }

        private static void LogVehicleDebug(string message)
        {
            if (!vehicleDebugLoggingEnabled)
                return;

            CameraToolsFileLogger.Log(message);
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
                var field = GetCachedField(type, memberName);
                if (field != null)
                {
                    value = field.GetValue(target);
                    return true;
                }

                var property = GetCachedProperty(type, memberName);
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
                var field = GetCachedField(type, memberName);
                if (field != null)
                {
                    field.SetValue(target, ConvertValue(value, field.FieldType));
                    return true;
                }

                var property = GetCachedProperty(type, memberName);
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

        private static FieldInfo? GetCachedField(Type type, string memberName)
        {
            var key = type.AssemblyQualifiedName + "|F|" + memberName;
            lock (memberCacheLock)
            {
                if (fieldCache.TryGetValue(key, out var field))
                    return field;

                field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                fieldCache[key] = field;
                return field;
            }
        }

        private static PropertyInfo? GetCachedProperty(Type type, string memberName)
        {
            var key = type.AssemblyQualifiedName + "|P|" + memberName;
            lock (memberCacheLock)
            {
                if (propertyCache.TryGetValue(key, out var property))
                    return property;

                property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                propertyCache[key] = property;
                return property;
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

        private readonly struct RendererState
        {
            public RendererState(Renderer renderer, bool wasEnabled)
            {
                Renderer = renderer;
                WasEnabled = wasEnabled;
            }

            public Renderer Renderer { get; }

            public bool WasEnabled { get; }
        }

        private readonly struct GameObjectActiveState
        {
            public GameObjectActiveState(GameObject target, bool wasActive)
            {
                Target = target;
                WasActive = wasActive;
            }

            public GameObject Target { get; }

            public bool WasActive { get; }
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
