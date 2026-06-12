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
        private const float ControllerRefreshInterval = 2f;
        private const float PitchStepPerScrollTick = 3f;
        private const string PedestrianCamTypeName = "CameraControllers.PedestrianCam";
        private const string CityMapCamTypeName = "CityMapCam";
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
        private MonoBehaviour? mapController;
        private float manualGameplayPitch;
        private float nextControllerRefreshAt;
        private CameraToolsSettings? settings;
        private static Type? pedestrianCamType;
        private static Type? cityMapCamType;

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
            runtime.nextControllerRefreshAt = 0f;
            runtime.hasManualGameplayPitch = false;
            runtime.hasShownGameplayPitchHint = false;
            runtime.configuredGameplayController = null;
            runtime.configuredMapController = null;
            pedestrianCamType ??= FindType(PedestrianCamTypeName);
            cityMapCamType ??= FindType(CityMapCamTypeName);
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

            if (Time.unscaledTime >= nextControllerRefreshAt)
            {
                RefreshControllers();
                nextControllerRefreshAt = Time.unscaledTime + ControllerRefreshInterval;
            }

            ApplyGameplayTweaks();
            ApplyMapTweaks();
        }

        private void RefreshControllers()
        {
            gameplayController = FindFirstActiveController(pedestrianCamType);
            mapController = FindFirstActiveController(cityMapCamType);

            if (gameplayController != configuredGameplayController)
            {
                configuredGameplayController = gameplayController;
            }

            if (mapController != configuredMapController)
            {
                configuredMapController = mapController;
            }

            ConfigureGameplayController();
            ConfigureMapController();
        }

        private static MonoBehaviour? FindFirstActiveController(Type? type)
        {
            if (type == null)
                return null;

            foreach (var obj in Resources.FindObjectsOfTypeAll(type))
            {
                var behaviour = obj as MonoBehaviour;
                if (behaviour == null)
                    continue;

                var gameObject = behaviour.gameObject;
                if (gameObject == null || !gameObject.activeInHierarchy || !behaviour.isActiveAndEnabled)
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

            var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);

            if (!hasShownGameplayPitchHint && context != null)
            {
                context.Logger.Info("CameraTools: hold Left Alt and use the mouse wheel to tilt the gameplay camera.");
                hasShownGameplayPitchHint = true;
            }

            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                var scrollDelta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scrollDelta) > Mathf.Epsilon)
                {
                    manualGameplayPitch = Mathf.Clamp(manualGameplayPitch + scrollDelta * PitchStepPerScrollTick, minPitch, maxPitch);
                    hasManualGameplayPitch = true;
                }
            }

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
            if (settings == null || mapController == null || !settings.EnableMapTopDown)
            {
                RestoreMapCameraState();
                return;
            }

            var mapCamera = ResolveMapCamera(mapController);
            if (mapCamera == null)
                return;

            if (activeMapCamera != mapCamera)
            {
                RestoreMapCameraState();
                activeMapCamera = mapCamera;
                activeMapCameraState = new CameraState(mapCamera);
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
