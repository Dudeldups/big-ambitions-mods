#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Buildings.BuildingTypes.Special.PrivateDriverService;
using Data.VehicleColors;
using GleyTrafficSystem;
using Helpers;
using UnityEngine;

internal static class Porsche911GT3RSPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/porsche911gt3rs.prefab";
    private const int PrivateDriverPoolSize = 2;

    private static readonly List<PrivateDriverContract> ModifiedContracts =
        new List<PrivateDriverContract>(2);
    private static readonly Dictionary<string, VehicleColor> CapturedVehicleColors =
        new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
    private static GameObject? customAiPrefab;
    private static UnityEngine.Object? previousCachedPrefab;
    private static bool previousCacheEntryCaptured;
    private static VehiclePool? modifiedVehiclePool;
    private static CarType? customCarType;
    private static ModContext? context;

    internal static void SetContext(ModContext modContext) => context = modContext;

    internal static bool PrepareTrafficPool(GameObject playerPrefab)
    {
        if (playerPrefab == null || !EnsureAiPrefab(playerPrefab) || customAiPrefab == null)
            return false;

        var trafficComponent = UnityEngine.Object.FindAnyObjectByType<TrafficComponent>();
        var pool = trafficComponent?.vehiclePool;
        if (pool == null)
            return false;

        var existing = pool.trafficCars ?? Array.Empty<CarType>();
        foreach (var carType in existing)
        {
            if (carType != null && ReferenceEquals(carType.vehiclePrefab, customAiPrefab))
            {
                modifiedVehiclePool = pool;
                customCarType = carType;
                return true;
            }
        }

        if (TrafficManager.IsInitialized)
            return false;

        customCarType = new CarType
        {
            name = "Porsche911GT3RSPrivateDriver",
            vehiclePrefab = customAiPrefab,
            nrOfVehicles = PrivateDriverPoolSize,
            canBeRandomlyParked = false,
            hasParkedVersion = false,
            canBeAiDriven = true,
        };
        var expanded = new CarType[existing.Length + 1];
        Array.Copy(existing, expanded, existing.Length);
        expanded[existing.Length] = customCarType;
        pool.trafficCars = expanded;
        modifiedVehiclePool = pool;
        return true;
    }

    internal static bool EnsureVehicleAvailable(string vehicleTypeName, GameObject playerPrefab)
    {
        if (string.IsNullOrWhiteSpace(vehicleTypeName) || playerPrefab == null)
            return false;
        if (!EnsureAiPrefab(playerPrefab) || !IsTrafficPrefabAvailable())
            return false;

        var contracts = PrivateDriverHelpers.GetContracts();
        if (contracts == null ||
            !contracts.TryGetValue(AdvancedContractKey, out var advanced) ||
            !contracts.TryGetValue(PremiumContractKey, out var premium) ||
            advanced == null || premium == null)
        {
            return false;
        }

        EnsureContractContains(advanced, vehicleTypeName);
        EnsureContractContains(premium, vehicleTypeName);
        return Contains(advanced.usableVehicleTypes, vehicleTypeName) &&
               Contains(premium.usableVehicleTypes, vehicleTypeName);
    }

    internal static void RemoveVehicle(string vehicleTypeName)
    {
        if (!string.IsNullOrWhiteSpace(vehicleTypeName))
        {
            foreach (var contract in ModifiedContracts)
                RemoveAll(contract.usableVehicleTypes, vehicleTypeName);
        }
        ModifiedContracts.Clear();

        foreach (var capturedColor in CapturedVehicleColors.Values)
        {
            if (capturedColor != null)
                UnityEngine.Object.Destroy(capturedColor);
        }
        CapturedVehicleColors.Clear();

        if (modifiedVehiclePool != null && customCarType != null)
        {
            var existing = modifiedVehiclePool.trafficCars ?? Array.Empty<CarType>();
            var remaining = new List<CarType>(existing.Length);
            foreach (var carType in existing)
            {
                if (!ReferenceEquals(carType, customCarType))
                    remaining.Add(carType);
            }
            modifiedVehiclePool.trafficCars = remaining.ToArray();
        }
        modifiedVehiclePool = null;
        customCarType = null;

        var cache = GetPrefabCache();
        if (cache != null && customAiPrefab != null &&
            cache.Contains(AiPrefabCacheKey) &&
            ReferenceEquals(cache[AiPrefabCacheKey], customAiPrefab))
        {
            if (previousCachedPrefab != null)
                cache[AiPrefabCacheKey] = previousCachedPrefab;
            else
                cache.Remove(AiPrefabCacheKey);
        }

        if (customAiPrefab != null)
            UnityEngine.Object.Destroy(customAiPrefab);
        customAiPrefab = null;
        previousCachedPrefab = null;
        previousCacheEntryCaptured = false;
        context = null;
    }

    internal static void ReportAppearanceResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"Porsche911GT3RS: chauffeur appearance color='{colorName ?? "<none>"}', " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            Porsche911GT3RSDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"Porsche911GT3RS: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            Porsche911GT3RSDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static bool TryResolveDriverColor(
        string? colorName,
        VehicleColor? liveColor,
        out VehicleColor? resolvedColor)
    {
        resolvedColor = null;
        if (string.IsNullOrEmpty(colorName))
            return false;
        var resolvedName = colorName!;

        if (liveColor != null &&
            string.Equals(liveColor.name, resolvedName, StringComparison.Ordinal))
        {
            resolvedColor = CaptureVehicleColor(resolvedName, liveColor);
            return true;
        }

        if (VehicleHelper.TryGetVehicleColor(resolvedName, out var registeredColor) &&
            registeredColor != null)
        {
            resolvedColor = CaptureVehicleColor(resolvedName, registeredColor);
            return true;
        }

        return CapturedVehicleColors.TryGetValue(resolvedName, out resolvedColor) &&
               resolvedColor != null;
    }

    private static VehicleColor CaptureVehicleColor(string colorName, VehicleColor source)
    {
        if (!CapturedVehicleColors.TryGetValue(colorName, out var captured) || captured == null)
        {
            captured = ScriptableObject.CreateInstance<VehicleColor>();
            captured.name = colorName;
            captured.hideFlags = HideFlags.DontSave;
            CapturedVehicleColors[colorName] = captured;
        }

        captured.randomWeight = source.randomWeight;
        captured.tint = source.tint;
        captured.fresnelColor = source.fresnelColor;
        captured.fresnelPower = source.fresnelPower;
        return captured;
    }

    private static void EnsureContractContains(
        PrivateDriverContract contract,
        string vehicleTypeName)
    {
        contract.usableVehicleTypes ??= new List<string>();
        if (Contains(contract.usableVehicleTypes, vehicleTypeName))
            return;

        contract.usableVehicleTypes.Add(vehicleTypeName);
        if (!ModifiedContracts.Contains(contract))
            ModifiedContracts.Add(contract);
    }

    private static bool EnsureAiPrefab(GameObject playerPrefab)
    {
        var cache = GetPrefabCache();
        if (cache == null)
            return false;

        if (customAiPrefab == null)
            customAiPrefab = CreateAiPrefab(playerPrefab);
        if (customAiPrefab == null)
            return false;

        if (!previousCacheEntryCaptured)
        {
            previousCachedPrefab = cache.Contains(AiPrefabCacheKey)
                ? cache[AiPrefabCacheKey] as UnityEngine.Object
                : null;
            previousCacheEntryCaptured = true;
        }

        cache[AiPrefabCacheKey] = customAiPrefab;
        return true;
    }

    private static bool IsTrafficPrefabAvailable() =>
        customAiPrefab != null && modifiedVehiclePool != null && customCarType != null;

    private static GameObject? CreateAiPrefab(GameObject playerPrefab)
    {
        var cache = GetPrefabCache();
        var template = cache != null && cache.Contains(AiTemplateCacheKey)
            ? cache[AiTemplateCacheKey] as GameObject
            : LoadAiTemplate();
        if (template == null)
            return null;

        var templateWasActive = template.activeSelf;
        GameObject clone;
        try
        {
            template.SetActive(false);
            clone = UnityEngine.Object.Instantiate(template);
        }
        finally
        {
            template.SetActive(templateWasActive);
        }

        clone.name = "Porsche911GT3RSPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "PorscheVisual",
            "PorscheDamageBody",
            "PorscheWheelFrontLeft",
            "PorscheWheelFrontRight",
            "PorscheWheelRearLeft",
            "PorscheWheelRearRight",
        };
        foreach (var visualName in requiredVisuals)
        {
            var source = FindTransform(playerPrefab.transform, visualName);
            if (source == null)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            var visual = UnityEngine.Object.Instantiate(source.gameObject, clone.transform, false);
            visual.name = visualName;
            SetLayerRecursively(visual.transform, clone.layer);
        }

        Porsche911GT3RSMaterials.FixSolidMaterials(clone);
        var appearance = clone.AddComponent<Porsche911GT3RSPrivateDriverAppearance>();
        appearance.BindWheelVisuals();

        foreach (var component in clone.GetComponents<MonoBehaviour>())
        {
            if (component != null && string.Equals(
                    component.GetType().FullName,
                    "GleyTrafficSystem.VehicleComponent",
                    StringComparison.Ordinal))
            {
                SetMember(component, "prefab", clone);
                break;
            }
        }

        return clone;
    }

    private static GameObject? LoadAiTemplate()
    {
        foreach (var method in typeof(BigAmbitions.SaveSystem.AddressableResolver).GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "LoadAssetAsync", StringComparison.Ordinal) ||
                !method.IsGenericMethodDefinition)
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                continue;

            var handle = method
                .MakeGenericMethod(typeof(GameObject))
                .Invoke(null, new object[] { AiTemplateCacheKey });
            if (handle == null)
                return null;

            var waitForCompletion = handle.GetType().GetMethod(
                "WaitForCompletion",
                BindingFlags.Instance | BindingFlags.Public);
            return waitForCompletion?.Invoke(handle, null) as GameObject;
        }

        return null;
    }

    private static IDictionary? GetPrefabCache()
    {
        var field = typeof(PrefabHelper).GetField(
            "PrefabCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        return field?.GetValue(null) as IDictionary;
    }

    private static void SetMember(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }

        var property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

    private static bool Contains(IEnumerable<string>? values, string target)
    {
        if (values == null)
            return false;
        foreach (var value in values)
        {
            if (string.Equals(value, target, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void RemoveAll(List<string>? values, string target)
    {
        if (values == null)
            return;
        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (string.Equals(values[index], target, StringComparison.Ordinal))
                values.RemoveAt(index);
        }
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class Porsche911GT3RSPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly string[,] WheelNames =
    {
        { "PorscheWheelFrontLeft", "FL" },
        { "PorscheWheelFrontRight", "FR" },
        { "PorscheWheelRearLeft", "BL" },
        { "PorscheWheelRearRight", "BR" },
    };

    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);
    private readonly List<CaliperBinding> caliperBindings = new List<CaliperBinding>(4);
    private Coroutine? initializationCoroutine;
    private Coroutine? departureCheckCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool trafficEventsSubscribed;

    internal void BindWheelVisuals()
    {
        wheelBindings.Clear();
        caliperBindings.Clear();
        for (var index = 0; index < WheelNames.GetLength(0); index++)
        {
            var visual = FindTransform(transform, WheelNames[index, 0]);
            var source = FindTransform(transform, WheelNames[index, 1]);
            if (visual == null || source == null)
                continue;
            wheelBindings.Add(new WheelBinding(transform, visual, source));
        }

        BindCaliper("PorscheFixedCaliperFrontLeft", "PorscheWheelFrontLeft");
        BindCaliper("PorscheFixedCaliperFrontRight", "PorscheWheelFrontRight");
        BindCaliper("PorscheFixedCaliperRearLeft", "PorscheWheelRearLeft");
        BindCaliper("PorscheFixedCaliperRearRight", "PorscheWheelRearRight");
    }

    private void BindCaliper(string caliperName, string wheelName)
    {
        var caliper = FindTransform(transform, caliperName);
        var wheel = FindTransform(transform, wheelName);
        if (caliper != null && wheel != null)
            caliperBindings.Add(new CaliperBinding(caliper, wheel));
    }

    private void Awake() => BindWheelVisuals();

    private void OnEnable()
    {
        BindWheelVisuals();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializePrivateDriverState());
    }

    private IEnumerator InitializePrivateDriverState()
    {
        for (var frame = 0; frame < InitializationFrameLimit; frame++)
        {
            var candidate = GetComponent<PrivateDriverVehicle>();
            if (candidate != null && candidate.vehicleInstance != null)
            {
                privateDriver = candidate;
                trafficVehicle = GetComponent<VehicleComponent>();
                SubscribeTrafficEvents();

                var paintApplied = RestoreSavedVehicleColor(false);
                for (var pass = 1; pass < InitializationFrameLimit; pass++)
                {
                    yield return new WaitForEndOfFrame();
                    paintApplied = RestoreSavedVehicleColor(
                        pass == InitializationFrameLimit - 1);
                }

                Porsche911GT3RSPrivateDriverSupport.ReportAppearanceResult(
                    candidate.vehicleInstance.vehicleColorName,
                    paintApplied);
                initializationCoroutine = null;
                yield break;
            }

            yield return null;
        }

        initializationCoroutine = null;
    }

    private void SubscribeTrafficEvents()
    {
        if (trafficEventsSubscribed)
            return;
        AIEvents.onChangeDrivingState += HandleDrivingStateChanged;
        trafficEventsSubscribed = true;
    }

    private void HandleDrivingStateChanged(
        int vehicleIndex,
        SpecialDriveActionTypes action,
        float actionValue)
    {
        if (privateDriver == null || trafficVehicle == null ||
            vehicleIndex != trafficVehicle.GetIndex())
        {
            return;
        }

        if (departureCheckCoroutine != null)
            StopCoroutine(departureCheckCoroutine);
        departureCheckCoroutine = StartCoroutine(EnsureDepartureAfterDismissal());
    }

    private IEnumerator EnsureDepartureAfterDismissal()
    {
        yield return null;

        var privateDriverUi = UI.UIs.Instance?.smartphoneUI?.privateDriverUI;
        if (privateDriverUi == null || CurrentVehicleProperty == null)
        {
            departureCheckCoroutine = null;
            yield break;
        }

        var currentPrivateDriver =
            CurrentVehicleProperty.GetValue(privateDriverUi) as PrivateDriverVehicle;
        var dismissed = privateDriver != null && currentPrivateDriver != privateDriver;
        if (dismissed && trafficVehicle != null && trafficVehicle.gameObject.activeInHierarchy)
        {
            trafficVehicle.presetPath = null;
            var trafficManager = TrafficManager.Instance;
            if (trafficManager != null)
            {
                trafficManager.SetVehicleAction(
                    trafficVehicle,
                    SpecialDriveActionTypes.Forward,
                    true);
                trafficManager.VehicleUpdateWaypoint(trafficVehicle);
            }
            if (privateDriver?.vehicleInstance != null)
            {
                var colorName = privateDriver.vehicleInstance.vehicleColorName;
                Porsche911GT3RSPrivateDriverSupport.ReportDeparturePaintResult(
                    colorName,
                    RestoreSavedVehicleColor(true));
            }
        }

        departureCheckCoroutine = null;
    }

    private bool RestoreSavedVehicleColor(bool applySpecializedPaint)
    {
        if (privateDriver?.vehicleInstance == null)
            return false;

        var colorName = privateDriver.vehicleInstance.vehicleColorName;
        var features = GetComponent<CarFeatures>();
        var restoredBaseColor = false;
        if (Porsche911GT3RSPrivateDriverSupport.TryResolveDriverColor(
                colorName,
                features?.VehicleColor,
                out var savedColor) &&
            savedColor != null)
        {
            features?.SetColor(savedColor);
            restoredBaseColor = features != null;
        }

        if (!applySpecializedPaint)
            return restoredBaseColor;

        var paint = GetComponent<Porsche911GT3RSPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<Porsche911GT3RSPaintController>();
        paint.InitializeForPrivateDriver(colorName, savedColor);
        return paint.HasAppliedColor;
    }

    private void LateUpdate()
    {
        foreach (var binding in wheelBindings)
            binding.Apply(transform);
        foreach (var binding in caliperBindings)
            binding.Apply(transform);
    }

    private void OnDisable()
    {
        var overlayManager =
            InstanceBehavior<Player.HUD.ItemInfoOverlays.OverlayManager>.Instance;
        if (overlayManager != null && privateDriver != null &&
            overlayManager.IsShowingOverlayOverItem(privateDriver))
        {
            overlayManager.HideSimpleOverlayAndClearCta();
        }

        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        if (departureCheckCoroutine != null)
            StopCoroutine(departureCheckCoroutine);
        initializationCoroutine = null;
        departureCheckCoroutine = null;

        if (trafficEventsSubscribed)
        {
            AIEvents.onChangeDrivingState -= HandleDrivingStateChanged;
            trafficEventsSubscribed = false;
        }

        privateDriver = null;
        trafficVehicle = null;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private sealed class WheelBinding
    {
        private readonly Transform visual;
        private readonly Transform source;
        private readonly Vector3 initialVisualPosition;
        private readonly Vector3 initialSourcePosition;
        private readonly Quaternion rotationOffset;

        internal WheelBinding(Transform root, Transform visual, Transform source)
        {
            this.visual = visual;
            this.source = source;
            initialVisualPosition = root.InverseTransformPoint(visual.position);
            initialSourcePosition = root.InverseTransformPoint(source.position);
            rotationOffset = Quaternion.Inverse(source.rotation) * visual.rotation;
        }

        internal void Apply(Transform root)
        {
            var sourcePosition = root.InverseTransformPoint(source.position);
            visual.position = root.TransformPoint(
                initialVisualPosition + sourcePosition - initialSourcePosition);
            visual.rotation = source.rotation * rotationOffset;
        }
    }

    private sealed class CaliperBinding
    {
        private readonly Transform caliper;
        private readonly Transform wheel;

        internal CaliperBinding(Transform caliper, Transform wheel)
        {
            this.caliper = caliper;
            this.wheel = wheel;
        }

        internal void Apply(Transform root)
        {
            var axle = Vector3.ProjectOnPlane(wheel.right, root.up).normalized;
            if (axle.sqrMagnitude < 0.5f)
                return;

            var steeringAngle = Vector3.SignedAngle(root.right, axle, root.up);
            if (Mathf.Abs(steeringAngle) > 60f)
                return;

            caliper.SetPositionAndRotation(
                wheel.position,
                root.rotation * Quaternion.Euler(0f, steeringAngle, 0f));
        }
    }
}
