#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace CameraTools;

public sealed class CameraToolsRuntime : MonoBehaviour
{
    private const float ControllerRefreshInterval = 1f;
    private const float PitchStepPerScrollTick = 3f;
    private static readonly string[] GameplayCameraTypeNames = { "CameraControllers.PedestrianCam" };
    private static readonly string[] MapCameraTypeNames = { "CityMapCam" };
    private static readonly string[] CameraMemberCandidates =
    {
        "Camera",
        "_camera",
        "_mainCamera",
        "pedestrianCamera",
        "citymapCamera",
        "buildingPreviewCamera"
    };

    private static readonly string[] MaxDistanceMemberCandidates =
    {
        "maxDistance",
        "maximumDistance",
        "maxZoom"
    };

    private static readonly string[] MinDistanceMemberCandidates =
    {
        "minDistance",
        "minimumDistance",
        "minZoom"
    };

    private static readonly string[] DistanceMemberCandidates =
    {
        "distance",
        "zoom",
        "cityMapZoom"
    };

    private static readonly string[] PitchMemberCandidates =
    {
        "pitch",
        "_angle",
        "_currentAngle"
    };

    private static readonly string[] RotationMemberCandidates =
    {
        "rotation",
        "yRotation"
    };

    private Camera? activeMapCamera;
    private CameraState activeMapCameraState;
    private ModContext? context;
    private MonoBehaviour? gameplayController;
    private bool hasManualGameplayPitch;
    private float manualGameplayPitch;
    private MonoBehaviour? mapController;
    private float nextControllerRefreshAt;
    private CameraToolsSettings? settings;

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
        gameplayController = FindFirstActiveController(GameplayCameraTypeNames);
        mapController = FindFirstActiveController(MapCameraTypeNames);
    }

    private MonoBehaviour? FindFirstActiveController(IEnumerable<string> typeNames)
    {
        foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour == null)
                continue;

            var gameObject = behaviour.gameObject;
            if (gameObject == null || !gameObject.activeInHierarchy || !behaviour.isActiveAndEnabled)
                continue;

            var fullName = behaviour.GetType().FullName;
            if (fullName != null && typeNames.Contains(fullName, StringComparer.Ordinal))
                return behaviour;
        }

        return null;
    }

    private void ApplyGameplayTweaks()
    {
        if (settings == null || !settings.EnableGameplayTweaks || gameplayController == null)
            return;

        var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
        var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);

        SetMaximumValue(gameplayController, settings.GameplayMaxZoom, MaxDistanceMemberCandidates);
        SetMinimumValue(gameplayController, 0f, MinDistanceMemberCandidates);
        TrySetBoolMember(gameplayController, false, "blockCameraZoom");

        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
        {
            var scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) > Mathf.Epsilon)
            {
                manualGameplayPitch = Mathf.Clamp(
                    GetCurrentGameplayPitch(settings.GameplayDefaultPitch) + scrollDelta * PitchStepPerScrollTick,
                    minPitch,
                    maxPitch);
                hasManualGameplayPitch = true;
            }
        }

        if (Input.GetKeyDown(KeyCode.Home))
        {
            manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, minPitch, maxPitch);
            hasManualGameplayPitch = true;
        }

        if (!hasManualGameplayPitch)
            return;

        if (!TrySetFirstNumericMember(gameplayController, manualGameplayPitch, PitchMemberCandidates))
        {
            var gameplayCamera = ResolveCamera(gameplayController);
            if (gameplayCamera != null)
            {
                var localAngles = gameplayCamera.transform.localEulerAngles;
                gameplayCamera.transform.localRotation = Quaternion.Euler(manualGameplayPitch, localAngles.y, localAngles.z);
            }
        }
    }

    private float GetCurrentGameplayPitch(float defaultPitch)
    {
        if (gameplayController == null)
            return defaultPitch;

        return TryGetFirstNumericMember(gameplayController, PitchMemberCandidates, out var pitch)
            ? pitch
            : defaultPitch;
    }

    private void ApplyMapTweaks()
    {
        if (settings == null || mapController == null || !settings.EnableMapTopDown)
        {
            RestoreMapCameraState();
            return;
        }

        var mapDistance = Mathf.Max(settings.MapDistance, 1);
        var mapPitch = Mathf.Clamp(settings.MapPitch, 75, 90);

        SetMaximumValue(mapController, mapDistance, MaxDistanceMemberCandidates);
        SetMinimumValue(mapController, mapDistance, MinDistanceMemberCandidates);
        TrySetFirstNumericMember(mapController, mapDistance, DistanceMemberCandidates);
        TrySetFirstNumericMember(mapController, mapPitch, PitchMemberCandidates);
        TrySetFirstNumericMember(mapController, 0f, RotationMemberCandidates);

        var mapCamera = ResolveCamera(mapController);
        if (mapCamera == null)
            return;

        if (activeMapCamera != mapCamera)
        {
            RestoreMapCameraState();
            activeMapCamera = mapCamera;
            activeMapCameraState = new CameraState(mapCamera);
        }

        mapCamera.orthographic = true;
        mapCamera.orthographicSize = settings.MapOrthographicSize;

        var eulerAngles = mapCamera.transform.eulerAngles;
        mapCamera.transform.rotation = Quaternion.Euler(mapPitch, eulerAngles.y, 0f);
    }

    private void RestoreMapCameraState()
    {
        if (activeMapCamera == null)
            return;

        activeMapCameraState.Restore(activeMapCamera);
        activeMapCamera = null;
    }

    private static Camera? ResolveCamera(Component controller)
    {
        foreach (var candidate in CameraMemberCandidates)
        {
            if (!TryGetMemberValue(controller, candidate, out var value) || value == null)
                continue;

            switch (value)
            {
                case Camera camera:
                    return camera;
                case GameObject gameObject:
                    return gameObject.GetComponentInChildren<Camera>(true);
                case Component component:
                    return component.GetComponentInChildren<Camera>(true);
            }
        }

        return controller.GetComponentInChildren<Camera>(true) ?? Camera.main;
    }

    private static void SetMaximumValue(object target, float minimumAllowedValue, IEnumerable<string> memberNames)
    {
        foreach (var memberName in memberNames)
        {
            if (TryGetNumericMember(target, memberName, out var currentValue) && currentValue >= minimumAllowedValue)
                continue;

            TrySetNumericMember(target, memberName, minimumAllowedValue);
        }
    }

    private static void SetMinimumValue(object target, float maximumAllowedValue, IEnumerable<string> memberNames)
    {
        foreach (var memberName in memberNames)
        {
            if (TryGetNumericMember(target, memberName, out var currentValue) && currentValue <= maximumAllowedValue)
                continue;

            TrySetNumericMember(target, memberName, maximumAllowedValue);
        }
    }

    private static bool TrySetFirstNumericMember(object target, float value, IEnumerable<string> memberNames)
    {
        foreach (var memberName in memberNames)
        {
            if (TrySetNumericMember(target, memberName, value))
                return true;
        }

        return false;
    }

    private static bool TryGetFirstNumericMember(object target, IEnumerable<string> memberNames, out float value)
    {
        foreach (var memberName in memberNames)
        {
            if (TryGetNumericMember(target, memberName, out value))
                return true;
        }

        value = default;
        return false;
    }

    private static bool TrySetBoolMember(object target, bool value, string memberName)
    {
        var members = GetMembers(target.GetType(), memberName);
        foreach (var member in members)
        {
            try
            {
                switch (member)
                {
                    case PropertyInfo property when property.CanWrite && property.PropertyType == typeof(bool):
                        property.SetValue(target, value);
                        return true;
                    case FieldInfo field when field.FieldType == typeof(bool):
                        field.SetValue(target, value);
                        return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TrySetNumericMember(object target, string memberName, float value)
    {
        var members = GetMembers(target.GetType(), memberName);
        foreach (var member in members)
        {
            try
            {
                switch (member)
                {
                    case PropertyInfo property when property.CanWrite:
                        property.SetValue(target, ConvertNumericValue(value, property.PropertyType));
                        return true;
                    case FieldInfo field:
                        field.SetValue(target, ConvertNumericValue(value, field.FieldType));
                        return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryGetNumericMember(object target, string memberName, out float value)
    {
        value = default;
        var members = GetMembers(target.GetType(), memberName);
        foreach (var member in members)
        {
            try
            {
                object? rawValue = member switch
                {
                    PropertyInfo property when property.CanRead => property.GetValue(target),
                    FieldInfo field => field.GetValue(target),
                    _ => null
                };

                if (rawValue == null)
                    continue;

                value = Convert.ToSingle(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryGetMemberValue(object target, string memberName, out object? value)
    {
        value = default;
        var members = GetMembers(target.GetType(), memberName);
        foreach (var member in members)
        {
            try
            {
                value = member switch
                {
                    PropertyInfo property when property.CanRead => property.GetValue(target),
                    FieldInfo field => field.GetValue(target),
                    _ => null
                };

                if (value != null)
                    return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static IEnumerable<MemberInfo> GetMembers(Type type, string memberName)
    {
        const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        return type
            .GetMember(memberName, MemberTypes.Field | MemberTypes.Property, Flags)
            .OrderBy(member => member is PropertyInfo ? 0 : 1);
    }

    private static object ConvertNumericValue(float value, Type targetType)
    {
        if (targetType == typeof(float))
            return value;
        if (targetType == typeof(double))
            return (double)value;
        if (targetType == typeof(int))
            return Mathf.RoundToInt(value);
        if (targetType == typeof(long))
            return (long)Mathf.RoundToInt(value);
        if (targetType == typeof(short))
            return (short)Mathf.RoundToInt(value);

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
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
