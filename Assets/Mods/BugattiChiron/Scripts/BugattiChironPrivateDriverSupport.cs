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

internal static class BugattiChironPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/bugattichiron.prefab";
    private const int PrivateDriverPoolSize = 2;

    private static readonly List<PrivateDriverContract> ModifiedContracts = new(2);
    private static readonly Dictionary<string, VehicleColor> CapturedVehicleColors =
        new(StringComparer.Ordinal);
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

        foreach (var carType in pool.trafficCars)
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
            name = "BugattiChironPrivateDriver",
            vehiclePrefab = customAiPrefab,
            nrOfVehicles = PrivateDriverPoolSize,
            canBeRandomlyParked = false,
            hasParkedVersion = false,
            canBeAiDriven = true,
        };
        var existing = pool.trafficCars ?? Array.Empty<CarType>();
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
        if (!EnsureAiPrefab(playerPrefab))
            return false;
        if (!IsTrafficPrefabAvailable())
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
            if (capturedColor != null)
                UnityEngine.Object.Destroy(capturedColor);
        CapturedVehicleColors.Clear();

        if (modifiedVehiclePool != null && customCarType != null)
        {
            var existing = modifiedVehiclePool.trafficCars ?? Array.Empty<CarType>();
            var remaining = new List<CarType>(existing.Length);
            foreach (var carType in existing)
                if (!ReferenceEquals(carType, customCarType))
                    remaining.Add(carType);
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
        string? liveColorName,
        bool paintApplied)
    {
        var message =
            $"BugattiChiron: chauffeur appearance color='{colorName ?? "<none>"}' " +
            $"liveColor='{liveColorName ?? "<none>"}', paintApplied={paintApplied}.";
        if (paintApplied)
            context?.Logger.Info(message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDepartureCorrection(int vehicleIndex) =>
        context?.Logger.Info(
            $"BugattiChiron: chauffeur departure resumed after dismissal vehicleIndex={vehicleIndex}.");

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"BugattiChiron: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            context?.Logger.Info(message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportTrafficAppearanceResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"BugattiChiron: traffic appearance color='{colorName ?? "<none>"}', " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            context?.Logger.Info(message);
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
        if (!Contains(contract.usableVehicleTypes, vehicleTypeName))
        {
            contract.usableVehicleTypes.Add(vehicleTypeName);
            if (!ModifiedContracts.Contains(contract))
                ModifiedContracts.Add(contract);
        }
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

    private static bool IsTrafficPrefabAvailable()
    {
        return customAiPrefab != null &&
               modifiedVehiclePool != null &&
               customCarType != null;
    }

    private static GameObject? CreateAiPrefab(GameObject playerPrefab)
    {
        var cache = GetPrefabCache();
        var template = cache != null && cache.Contains(AiTemplateCacheKey)
            ? cache[AiTemplateCacheKey] as GameObject
            : BigAmbitions.SaveSystem.AddressableResolver
                .LoadAssetAsync<GameObject>(AiTemplateCacheKey)
                .WaitForCompletion();
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

        clone.name = "BugattiChironPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "BugattiVisual",
            "BugattiWheelFrontLeft",
            "BugattiWheelFrontRight",
            "BugattiWheelRearLeft",
            "BugattiWheelRearRight",
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

        BugattiChironMaterials.FixSolidMaterials(clone);
        var appearance = clone.AddComponent<BugattiChironPrivateDriverAppearance>();
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
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
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
            if (string.Equals(value, target, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void RemoveAll(List<string>? values, string target)
    {
        if (values == null)
            return;
        for (var index = values.Count - 1; index >= 0; index--)
            if (string.Equals(values[index], target, StringComparison.Ordinal))
                values.RemoveAt(index);
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class BugattiChironPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly string[,] WheelNames =
    {
        { "BugattiWheelFrontLeft", "FL" },
        { "BugattiWheelFrontRight", "FR" },
        { "BugattiWheelRearLeft", "BL" },
        { "BugattiWheelRearRight", "BR" },
    };

    private readonly List<WheelBinding> wheelBindings = new(4);
    private Coroutine? initializationCoroutine;
    private Coroutine? departureCheckCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool trafficEventsSubscribed;
    private bool trafficAppearanceFailureLogged;
    private bool trafficAppearanceSuccessLogged;

    internal void BindWheelVisuals()
    {
        wheelBindings.Clear();
        for (var index = 0; index < WheelNames.GetLength(0); index++)
        {
            var visual = FindTransform(transform, WheelNames[index, 0]);
            var source = FindTransform(transform, WheelNames[index, 1]);
            if (visual == null || source == null)
                continue;
            wheelBindings.Add(new WheelBinding(transform, visual, source));
        }
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

                // A pooled traffic vehicle can receive a random color during its
                // first few activation frames. Restore the saved vehicle color at
                // that exact lifecycle boundary, then stop after this fixed window.
                var paintApplied = RestoreSavedVehicleColor(false);
                for (var pass = 1; pass < InitializationFrameLimit; pass++)
                {
                    yield return new WaitForEndOfFrame();
                    paintApplied = RestoreSavedVehicleColor(
                        pass == InitializationFrameLimit - 1);
                }

                var liveColorName = GetComponent<CarFeatures>()?.VehicleColor?.name;
                BugattiChironPrivateDriverSupport.ReportAppearanceResult(
                    candidate.vehicleInstance.vehicleColorName,
                    liveColorName,
                    paintApplied);
                initializationCoroutine = null;
                yield break;
            }

            if (frame == InitializationFrameLimit - 1)
            {
                ApplyTrafficVehicleColor();
                initializationCoroutine = null;
                yield break;
            }

            yield return null;
        }

        initializationCoroutine = null;
    }

    private void ApplyTrafficVehicleColor()
    {
        var features = GetComponent<CarFeatures>();
        var liveColor = features?.VehicleColor;
        if (liveColor == null)
        {
            if (!trafficAppearanceFailureLogged)
            {
                trafficAppearanceFailureLogged = true;
                BugattiChironPrivateDriverSupport.ReportTrafficAppearanceResult(null, false);
            }
            return;
        }

        var paint = GetComponent<BugattiChironPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<BugattiChironPaintController>();
        paint.InitializeForPrivateDriver(null, liveColor);

        if (!trafficAppearanceSuccessLogged)
        {
            trafficAppearanceSuccessLogged = true;
            BugattiChironPrivateDriverSupport.ReportTrafficAppearanceResult(
                liveColor.name,
                paint.HasAppliedColor);
        }
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

        // A stop-state event can arrive immediately before DriveAway. Restarting
        // the bounded check ensures the final event is the one evaluated after
        // SmartphonePrivateDriverUI has cleared CurrentVehicle.
        if (departureCheckCoroutine != null)
            StopCoroutine(departureCheckCoroutine);
        departureCheckCoroutine = StartCoroutine(EnsureDepartureAfterDismissal());
    }

    private IEnumerator EnsureDepartureAfterDismissal()
    {
        // DismissPrivateDriver clears CurrentVehicle only after DriveAway raises
        // its traffic-state event. Waiting one frame distinguishes that event
        // from the normal stop events used while picking up the player.
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
                BugattiChironPrivateDriverSupport.ReportDepartureCorrection(
                    trafficVehicle.GetIndex());
            }

            if (privateDriver?.vehicleInstance != null)
            {
                var colorName = privateDriver.vehicleInstance.vehicleColorName;
                BugattiChironPrivateDriverSupport.ReportDeparturePaintResult(
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
        if (BugattiChironPrivateDriverSupport.TryResolveDriverColor(
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

        var paint = GetComponent<BugattiChironPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<BugattiChironPaintController>();
        paint.InitializeForPrivateDriver(colorName, features?.VehicleColor);
        return paint.HasAppliedColor;
    }

    private void LateUpdate()
    {
        foreach (var binding in wheelBindings)
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
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
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
}
