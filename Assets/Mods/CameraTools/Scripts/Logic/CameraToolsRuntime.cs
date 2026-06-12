#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using UI.Notification;
using UnityEngine;

namespace CameraTools
{
    public sealed class CameraToolsRuntime : MonoBehaviour
    {
        private const float PitchStepPerMousePixel = 0.15f;
        private const string GameManagerTypeName = "GameManager";
        private const string PedestrianCamTypeName = "CameraControllers.PedestrianCam";
        private const string CityMapCamTypeName = "CityMapCam";
        private const string SaveGameManagerTypeName = "SaveGameManager";
        private const float GameplayMinimumZoom = 1.5f;
        private const float MapMinimumZoom = 1f;
        private const float VehicleMinimumZoom = 6f;
        private const float VehicleControllerSearchIntervalSeconds = 2f;

        private Camera? activeMapCamera;
        private CameraState activeMapCameraState;
        private MonoBehaviour? configuredGameplayController;
        private MonoBehaviour? configuredMapController;
        private ModContext? context;
        private MonoBehaviour? gameplayController;
        private bool hasManualGameplayPitch;
        private bool hasShownGameplayPitchHint;
        private bool isTrackingRightMousePitch;
        private float lastRightMouseY;
        private MonoBehaviour? mapController;
        private float manualGameplayPitch;
        private MonoBehaviour? vehicleController;
        private float nextVehicleControllerSearchTime;
        private bool hasInitializedMapDistanceForCurrentOpen;
        private float lastAppliedMapDistanceSetting;
        private string? pendingMapNoticeDuplicateIdentifier;
        private string? pendingMapNoticeMessage;
        private CameraToolsSettings? settings;
        private bool wasCityMapOpen;
        private bool hasShownMapStatusNotice;
        private static Type? pedestrianCamType;
        private static Type? cityMapCamType;
        private static Type? gameManagerType;

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
            runtime.hasManualGameplayPitch = false;
            runtime.hasShownGameplayPitchHint = false;
            runtime.isTrackingRightMousePitch = false;
            runtime.vehicleController = null;
            runtime.nextVehicleControllerSearchTime = 0f;
            runtime.hasInitializedMapDistanceForCurrentOpen = false;
            runtime.lastAppliedMapDistanceSetting = float.NaN;
            runtime.pendingMapNoticeDuplicateIdentifier = null;
            runtime.pendingMapNoticeMessage = null;
            runtime.wasCityMapOpen = false;
            runtime.hasShownMapStatusNotice = false;
            runtime.configuredGameplayController = null;
            runtime.configuredMapController = null;
            pedestrianCamType ??= FindType(PedestrianCamTypeName);
            cityMapCamType ??= FindType(CityMapCamTypeName);
            gameManagerType ??= FindType(GameManagerTypeName);
            return runtime;
        }

        public void Shutdown()
        {
            RestoreMapCameraState();
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
            var cityMapOpen = IsCityMapOpen();

            ApplyGameplayTweaks();
            ApplyVehicleTweaks();
            ApplyMapTweaks(cityMapOpen);
            FlushPendingMapNotice(cityMapOpen);
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

            EnsureVehicleController();
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
                    if (behaviour == null)
                        continue;

                    return behaviour;
                }

                return null;
            }

            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                var behaviour = obj as MonoBehaviour;
                if (behaviour == null)
                    continue;

                var gameObject = behaviour.gameObject;
                if (gameObject == null)
                    continue;

                if (gameObject.hideFlags != HideFlags.None)
                    continue;

                return behaviour;
            }

            return null;
        }

        private void ConfigureGameplayController()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            var bounds = GetVector2Field(gameplayController, "minMaxDistance");
            bounds.x = Mathf.Min(bounds.x, GameplayMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.GameplayMaxZoom);
            SetField(gameplayController, "minMaxDistance", bounds);
            SetField(gameplayController, "blockCameraZoom", false);

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
            SetField(gameplayController, "offset", offset);
        }

        private void ConfigureMapController()
        {
            if (settings == null || mapController == null || !settings.EnableMapTopDown)
                return;

            var bounds = GetVector2Field(mapController, "minMaxDistance");
            bounds.x = Mathf.Min(bounds.x, MapMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.MapDistance);
            SetField(mapController, "minMaxDistance", bounds);

            var desiredDistance = Mathf.Clamp(settings.MapDistance, bounds.x, bounds.y);
            var shouldSeedDistance =
                !hasInitializedMapDistanceForCurrentOpen ||
                !Mathf.Approximately(lastAppliedMapDistanceSetting, desiredDistance);

            if (!shouldSeedDistance)
                return;

            var currentDistance = Mathf.Clamp(GetFloatField(mapController, "distance"), bounds.x, bounds.y);
            if (currentDistance < desiredDistance)
            {
                SetField(mapController, "distance", desiredDistance);
                currentDistance = desiredDistance;
            }

            SetSavedMapZoom(currentDistance);
            hasInitializedMapDistanceForCurrentOpen = true;
            lastAppliedMapDistanceSetting = desiredDistance;
        }

        private void EnsureVehicleController()
        {
            if (vehicleController != null)
            {
                if (vehicleController.isActiveAndEnabled)
                    return;

                vehicleController = null;
            }

            if (Time.unscaledTime < nextVehicleControllerSearchTime)
                return;

            nextVehicleControllerSearchTime = Time.unscaledTime + VehicleControllerSearchIntervalSeconds;
            vehicleController = FindVehicleController();
        }

        private void ApplyVehicleTweaks()
        {
            if (settings == null || vehicleController == null)
                return;

            var bounds = GetVector2Field(vehicleController, "minMaxDistance");
            if (bounds == default && !HasField(vehicleController, "minMaxDistance"))
                return;

            bounds.x = Mathf.Min(bounds.x, VehicleMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.VehicleMaxZoom);
            SetField(vehicleController, "minMaxDistance", bounds);

            if (HasField(vehicleController, "blockCameraZoom"))
                SetField(vehicleController, "blockCameraZoom", false);
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

            var bounds = GetVector2Field(mapController, "minMaxDistance");
            var distance = Mathf.Clamp(GetFloatField(mapController, "distance"), bounds.x, bounds.y);

            var currentAngle = GetFloatField(mapController, "_currentAngle");
            var pitch = Mathf.Clamp(settings.MapPitch, 75f, 90f);
            var pitchRadians = pitch * Mathf.Deg2Rad;
            var height = distance * Mathf.Sin(pitchRadians);
            var horizontalRadius = Mathf.Max(0.05f, distance * Mathf.Cos(pitchRadians));
            var orbit = Quaternion.Euler(0f, currentAngle, 0f) * new Vector3(0f, height, -horizontalRadius);
            var rootPosition = mapController.transform.position;
            var targetPosition = rootPosition + orbit;
            var upAxis = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward;
            var targetRotation = Quaternion.LookRotation(rootPosition - targetPosition, upAxis);

            var vCamTransform = GetTransformField(mapController, "_vCam");
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
            if (cityMapOpen || string.IsNullOrEmpty(pendingMapNoticeMessage) ||
                string.IsNullOrEmpty(pendingMapNoticeDuplicateIdentifier))
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
            var vCamTransform = GetTransformField(controller, "_vCam");
            if (vCamTransform != null)
                return vCamTransform.GetComponent<Camera>() ?? vCamTransform.GetComponentInChildren<Camera>(true);

            return controller.GetComponentInChildren<Camera>(true) ?? GetLiveMainCamera();
        }

        private static MonoBehaviour? FindVehicleController()
        {
            foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled)
                    continue;

                if (IsLikelyVehicleController(behaviour))
                    return behaviour;
            }

            return null;
        }

        private static bool IsLikelyVehicleController(MonoBehaviour behaviour)
        {
            var type = behaviour.GetType();
            var typeName = (type.FullName ?? type.Name).ToLowerInvariant();
            var hasVehicleName = typeName.Contains("vehicle") || typeName.Contains("car");
            var hasCameraField =
                HasField(behaviour, "vehicleCamera") ||
                HasField(behaviour, "vehicleCameraReverse") ||
                HasField(behaviour, "indoorVehicleCamera") ||
                HasField(behaviour, "indoorVehicleCameraReverse");

            if (!hasVehicleName && !hasCameraField)
                return false;

            return HasField(behaviour, "minMaxDistance");
        }

        private void HandleCameraPreCull(Camera camera)
        {
        }

        private static Camera? GetLiveMainCamera()
        {
            if (gameManagerType != null)
            {
                var getMainCameraMethod =
                    gameManagerType.GetMethod("GetMainCamera", BindingFlags.Public | BindingFlags.Static);
                if (getMainCameraMethod?.Invoke(null, null) is Camera gameManagerCamera)
                    return gameManagerCamera;
            }

            return Camera.main;
        }

        private static void SetSavedMapZoom(float zoom)
        {
            var type = FindType(SaveGameManagerTypeName);
            if (type == null)
                return;

            var currentProperty = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            var current = currentProperty?.GetValue(null);
            if (current == null)
                return;

            SetField(current, "cityMapZoom", zoom);
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
                Debug.LogWarning($"CameraTools: failed to show popup: {exception}");
            }
        }

        private static Transform? GetTransformField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetValue(target) as Transform;
        }

        private static Vector2 GetVector2Field(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field != null && field.FieldType == typeof(Vector2)
                ? (Vector2)field.GetValue(target)
                : default;
        }

        private static float GetFloatField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                return 0f;

            var value = field.GetValue(target);
            return value is float floatValue ? floatValue : 0f;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
                return;

            field.SetValue(target, value);
        }

        private static bool HasField(object target, string fieldName)
        {
            return target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) != null;
        }

        private static Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static bool IsCityMapOpen()
        {
            var cityMapType = FindType("CityMap");
            if (cityMapType == null)
                return false;

            var isOpenProperty = cityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return isOpenProperty?.GetValue(null) as bool? ?? false;
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
    }
}
