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
            "filter",
            "filters",
            "search",
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








































































        private static void LogVehicleDebug(string message)
        {
            if (!vehicleDebugLoggingEnabled)
                return;

            CameraToolsFileLogger.Log(message);
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
