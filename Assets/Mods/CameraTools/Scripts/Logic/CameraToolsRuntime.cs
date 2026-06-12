#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace CameraTools
{
    public sealed class CameraToolsRuntime : MonoBehaviour
    {
        private const float PitchStepPerMousePixel = 0.15f;
        private const string PedestrianCamTypeName = "CameraControllers.PedestrianCam";
        private const string CityMapCamTypeName = "CityMapCam";
        private const string NotificationsTypeName = "UI.Notification.Notifications";
        private const string NotificationTypeEnumName = "UI.Notification.NotificationType";
        private const string SaveGameManagerTypeName = "SaveGameManager";
        private const float GameplayMinimumZoom = 1.5f;
        private const float MapMinimumZoom = 10f;

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
        private CameraToolsSettings? settings;
        private bool wasCityMapOpen;
        private bool hasShownMapStatusNotice;
        private static Type? pedestrianCamType;
        private static Type? cityMapCamType;
        private static Type? notificationsType;
        private static Type? notificationTypeEnumType;

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
            runtime.wasCityMapOpen = false;
            runtime.hasShownMapStatusNotice = false;
            runtime.configuredGameplayController = null;
            runtime.configuredMapController = null;
            pedestrianCamType ??= FindType(PedestrianCamTypeName);
            cityMapCamType ??= FindType(CityMapCamTypeName);
            notificationsType ??= FindType(NotificationsTypeName);
            notificationTypeEnumType ??= FindType(NotificationTypeEnumName);
            return runtime;
        }

        public void Shutdown()
        {
            RestoreMapCameraState();
            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            if (settings == null)
                return;

            EnsureControllers();

            ApplyGameplayTweaks();
            ApplyMapTweaks();
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
            SetSavedMapZoom(Mathf.Clamp(settings.MapDistance, bounds.x, bounds.y));
        }

        private void ApplyMapTweaks()
        {
            var cityMapOpen = IsCityMapOpen();
            if (cityMapOpen && !wasCityMapOpen)
                hasShownMapStatusNotice = false;
            wasCityMapOpen = cityMapOpen;

            if (settings == null || mapController == null || !settings.EnableMapTopDown)
            {
                RestoreMapCameraState();
                if (cityMapOpen && !hasShownMapStatusNotice)
                {
                    ShowInGameNotification("cameratools_map_notice_missing", "cameratools_map_notice_missing");
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
                ShowInGameNotification("cameratools_map_notice_found", "cameratools_map_notice_found");
                hasShownMapStatusNotice = true;
            }

            var distance = Mathf.Max(GetFloatField(mapController, "distance"), 1f);
            var currentAngle = GetFloatField(mapController, "_currentAngle");
            var pitch = Mathf.Clamp(settings.MapPitch, 75f, 90f);
            var pitchRadians = pitch * Mathf.Deg2Rad;
            var height = distance * Mathf.Sin(pitchRadians);
            var horizontalRadius = Mathf.Max(0.05f, distance * Mathf.Cos(pitchRadians));
            var orbit = Quaternion.Euler(0f, currentAngle, 0f) * new Vector3(0f, height, -horizontalRadius);
            var rootPosition = mapController.transform.position;
            var targetPosition = rootPosition + orbit;

            var vCamTransform = GetTransformField(mapController, "_vCam");
            if (vCamTransform != null)
            {
                vCamTransform.position = targetPosition;
                vCamTransform.rotation = Quaternion.LookRotation(rootPosition - targetPosition, Vector3.forward);
            }

            mapCamera.orthographic = true;
            mapCamera.orthographicSize = settings.MapOrthographicSize * (distance / Mathf.Max(settings.MapDistance, 1f));

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.orthographic = true;
                mainCamera.orthographicSize = mapCamera.orthographicSize;
                mainCamera.transform.position = targetPosition;
                mainCamera.transform.rotation = Quaternion.LookRotation(rootPosition - targetPosition, Vector3.forward);
            }
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

            return controller.GetComponentInChildren<Camera>(true) ?? Camera.main;
        }

        private static void ShowInGameNotification(string headerKey, string duplicateIdentifier)
        {
            if (notificationsType == null || notificationTypeEnumType == null)
                return;

            var showMethod = notificationsType.GetMethod(
                "Show",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    notificationTypeEnumType,
                    typeof(string),
                    typeof(Dictionary<string, string>),
                    typeof(float),
                    typeof(string),
                    typeof(Action),
                    typeof(bool),
                    typeof(bool)
                },
                null);

            if (showMethod == null)
                return;

            var infoValue = Enum.ToObject(notificationTypeEnumType, 3);
            showMethod.Invoke(
                null,
                new object?[] { infoValue, headerKey, null, 4f, duplicateIdentifier, null, false, false });
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
