using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BigAmbitions.SaveSystem.Legacy;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    [DefaultExecutionOrder(-9990)]
    internal sealed class StreetQuestMapMarkerWatcher : MonoBehaviour
    {
        private const bool PreferNativePoiMarkers = false;
        private const float UpdateIntervalSeconds = 0.05f;
        private const float MarkerVerticalOffset = 10f;
        private const float MarkerSize = 120f;

        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] RootNames =
        {
            "POIs",
            "Pois",
            "MapMarkers",
            "Waypoints",
            "MapIcons",
            "Icons"
        };
        private static readonly string[] PositionMemberKeywords =
        {
            "position",
            "world",
            "target",
            "building",
            "address",
            "entity",
            "poi",
            "point"
        };

        private float _elapsedSeconds;
        private float _nextRefreshAtSeconds;
        private Type _cityMapType;
        private RectTransform _poiRoot;
        private RectTransform _streetQuestRoot;
        private Sprite _markerSprite;
        private bool _loggedCalibrationFailure;
        private bool _hasCalibration;
        private CalibrationData _calibration;
        private readonly Dictionary<string, Component> _nativePoiComponents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _nativePoiTargetAnchors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RectTransform> _markerRoots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _markerVisibilityStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _markerStatusReasons = new(StringComparer.OrdinalIgnoreCase);
        private string _lastLifecycleState;
        private string _lastKnownCharacterSnapshot;
        private string _lastPoiRootPath;
        private float _nextVerboseLogAtSeconds;
        private bool _dumpedPoiSampleDiagnostics;
        private bool _dumpedPoiRuntimeDiagnostics;
        private RectTransform _poiMarkerTemplate;
        private RectTransform _nativePoiTemplate;
        private Transform _nativePoiTargetParent;
        private readonly Dictionary<string, Vector3> _nativePoiLastTargetPositions = new(StringComparer.OrdinalIgnoreCase);

        public void Initialize()
        {
            _elapsedSeconds = 0f;
            _nextRefreshAtSeconds = 0f;
            _poiRoot = null;
            _streetQuestRoot = null;
            _poiMarkerTemplate = null;
            _nativePoiTemplate = null;
            _nativePoiTargetParent = null;
            _loggedCalibrationFailure = false;
            _hasCalibration = false;
            _calibration = default;
            _lastLifecycleState = null;
            _lastKnownCharacterSnapshot = null;
            _lastPoiRootPath = null;
            _nextVerboseLogAtSeconds = 0f;
            _dumpedPoiSampleDiagnostics = false;
            _dumpedPoiRuntimeDiagnostics = false;
            DestroyLingeringStreetQuestMapObjects();
            DestroyMarkerImages();
        }

        private void Update()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;
            if (_elapsedSeconds < _nextRefreshAtSeconds)
                return;

            _nextRefreshAtSeconds = _elapsedSeconds + UpdateIntervalSeconds;

            if (SaveGameManager.Current == null)
            {
                LogLifecycleState("No save loaded; hiding StreetQuest map markers.");
                HideStreetQuestRoot();
                return;
            }

            if (!IsCityMapOpen())
            {
                LogLifecycleState("City map closed; hiding StreetQuest map markers.");
                HideStreetQuestRoot();
                return;
            }

            LogLifecycleState("City map open; refreshing StreetQuest map markers.");

            if (!TryResolvePoiRoot(out var poiRoot))
            {
                MaybeLogVerbose("POI root not found while city map is open.");
                return;
            }

            EnsureStreetQuestRoot(poiRoot);
            DumpPoiDiagnosticsOnce(poiRoot);
            DumpPoiRuntimeDiagnosticsOnce(poiRoot);
            EnsureCalibration(poiRoot);
            UpdateKnownNpcMarkers();
        }

        private void EnsureCalibration(RectTransform poiRoot)
        {
            _poiRoot = poiRoot;
            _hasCalibration = TryBuildCalibration(poiRoot, out _calibration);
            if (_hasCalibration)
            {
                _loggedCalibrationFailure = false;
                MaybeLogVerbose(
                    $"Using calibration scaleX={_calibration.scaleX:F4} offsetX={_calibration.offsetX:F2} scaleY={_calibration.scaleY:F4} offsetY={_calibration.offsetY:F2}");
                return;
            }

            if (_loggedCalibrationFailure)
                return;

            StreetQuestShared.LogDebug("Map marker calibration failed: could not derive vanilla POI world-to-map mapping.");
            _loggedCalibrationFailure = true;
        }

        private void UpdateKnownNpcMarkers()
        {
            if (_streetQuestRoot == null)
                return;

            _streetQuestRoot.gameObject.SetActive(true);

            var knownCharacterIds = StreetQuestShared.GetKnownCharacterIds()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            LogKnownCharacters(knownCharacterIds);
            var seen = new HashSet<string>(knownCharacterIds, StringComparer.OrdinalIgnoreCase);

            foreach (var existing in _markerRoots.Keys.ToArray())
            {
                if (seen.Contains(existing))
                    continue;

                if (_nativePoiComponents.TryGetValue(existing, out var stalePoiComponent) && stalePoiComponent != null)
                    Destroy(stalePoiComponent.gameObject);
                _nativePoiComponents.Remove(existing);
                _nativePoiLastTargetPositions.Remove(existing);

                if (_nativePoiTargetAnchors.TryGetValue(existing, out var staleAnchor) && staleAnchor != null)
                    Destroy(staleAnchor.gameObject);
                _nativePoiTargetAnchors.Remove(existing);

                if (_markerRoots.TryGetValue(existing, out var staleRoot) && staleRoot != null)
                    Destroy(staleRoot.gameObject);
                _markerRoots.Remove(existing);
                _markerVisibilityStates.Remove(existing);
                _markerStatusReasons.Remove(existing);
                StreetQuestShared.LogDebug($"Map marker removed characterId={existing}");
            }

            foreach (var characterId in knownCharacterIds)
            {
                if (!StreetQuestShared.TryGetCharacterWorldPosition(characterId, out var worldPosition))
                {
                    LogMarkerState(characterId, false, "No live world position resolved for character.");
                    SetMarkerActive(characterId, false);
                    continue;
                }

                if (!_hasCalibration || !TryMapWorldToUi(worldPosition, out var anchoredPosition))
                {
                    LogMarkerState(
                        characterId,
                        false,
                        _hasCalibration
                            ? $"Failed to map world position {FormatVector3(worldPosition)} to UI coordinates."
                            : $"Calibration unavailable for world position {FormatVector3(worldPosition)}.");
                    SetMarkerActive(characterId, false);
                    continue;
                }

                if (PreferNativePoiMarkers && EnsureNativePoiMarker(characterId, worldPosition))
                {
                    DestroyCustomMarkerRoot(characterId);
                    SetMarkerActive(characterId, false);
                    LogMarkerState(
                        characterId,
                        true,
                        $"Native PointOfInterest active world={FormatVector3(worldPosition)}");
                    continue;
                }

                var markerRoot = GetOrCreateMarkerRoot(characterId);
                if (markerRoot == null)
                {
                    LogMarkerState(characterId, false, "Failed to create marker root.");
                    continue;
                }

                markerRoot.anchoredPosition = anchoredPosition + new Vector2(0f, MarkerVerticalOffset);
                markerRoot.gameObject.SetActive(true);
                MaybeLogMarkerScreenRect(characterId, markerRoot);
                LogMarkerState(
                    characterId,
                    true,
                    $"Placed marker world={FormatVector3(worldPosition)} ui={FormatVector2(markerRoot.anchoredPosition)} sibling={markerRoot.GetSiblingIndex()}");
            }
        }

        private RectTransform GetOrCreateMarkerRoot(string characterId)
        {
            if (_poiRoot == null || _streetQuestRoot == null || string.IsNullOrWhiteSpace(characterId))
                return null;

            if (_markerRoots.TryGetValue(characterId, out var existingRoot) && existingRoot != null)
                return existingRoot;

            var template = ResolvePoiMarkerTemplate();
            if (template == null)
            {
                StreetQuestShared.LogDebug($"Map marker template missing for characterId={characterId}");
                return null;
            }

            var markerObject = new GameObject(
                $"StreetQuestMapMarker.{characterId}",
                typeof(RectTransform));
            var rectTransform = markerObject.GetComponent<RectTransform>();
            rectTransform.SetParent(_streetQuestRoot, false);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(MarkerSize, MarkerSize);
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.SetAsLastSibling();

            if (!TryAttachVanillaMarkerVisual(template, rectTransform))
            {
                var image = markerObject.AddComponent<Image>();
                image.sprite = GetMarkerSprite();
                image.color = new Color(1f, 0f, 1f, 1f);
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.enabled = true;
                StreetQuestShared.LogDebug($"Map marker visual fallback=plain_image characterId={characterId}");
            }

            _markerRoots[characterId] = rectTransform;
            StreetQuestShared.LogDebug(
                $"Map marker created characterId={characterId} template={template.name} anchorMin={FormatVector2(template.anchorMin)} anchorMax={FormatVector2(template.anchorMax)} pivot={FormatVector2(template.pivot)} size={FormatVector2(template.sizeDelta)}");
            return rectTransform;
        }

        private bool EnsureNativePoiMarker(string characterId, Vector3 worldPosition)
        {
            if (_poiRoot == null || string.IsNullOrWhiteSpace(characterId))
                return false;

            if (_nativePoiComponents.TryGetValue(characterId, out var existingPoi) && existingPoi != null)
            {
                UpdateNativePoiComponent(existingPoi, characterId, worldPosition);
                return true;
            }

            var template = ResolveNativePoiTemplate();
            if (template == null)
                return false;

            var pointOfInterestTemplate = template.GetComponent("PointOfInterest");
            if (pointOfInterestTemplate == null)
                return false;

            var poiObject = Instantiate(template.gameObject, _poiRoot, false);
            poiObject.name = $"StreetQuestNativePoi.{characterId}";
            poiObject.SetActive(true);

            var poiComponent = poiObject.GetComponent(pointOfInterestTemplate.GetType().Name) as Component;
            if (poiComponent == null)
            {
                Destroy(poiObject);
                return false;
            }

            TryInvokeMethod(poiComponent, "Initialize");
            ConfigureNativePoiComponent(poiComponent, characterId, worldPosition);
            _nativePoiComponents[characterId] = poiComponent;
            StreetQuestShared.LogDebug($"Native POI created characterId={characterId} template={template.name}");
            return true;
        }

        private void UpdateNativePoiComponent(Component poiComponent, string characterId, Vector3 worldPosition)
        {
            if (poiComponent == null || string.IsNullOrWhiteSpace(characterId))
                return;

            var targetTransform = ResolveNativePoiTargetTransform(characterId);
            if (targetTransform == null)
                return;

            var targetPosition = targetTransform.position;
            _nativePoiLastTargetPositions[characterId] = targetPosition;
            TryInvokeMethod(poiComponent, "UpdatePosition");
        }

        private void ConfigureNativePoiComponent(Component poiComponent, string characterId, Vector3 worldPosition)
        {
            if (poiComponent == null)
                return;

            var targetTransform = ResolveNativePoiTargetTransform(characterId);
            if (targetTransform == null)
            {
                StreetQuestShared.LogDebug($"Native POI target missing characterId={characterId} world={FormatVector3(worldPosition)}");
                return;
            }

            SetNamedFieldValue(poiComponent, "target", targetTransform);
            SetNamedFieldValue(poiComponent, "offset", Vector3.zero);
            SetNamedFieldValue(poiComponent, "hidden", false);
            SetNamedFieldValue(poiComponent, "isGuider", false);
            SetNamedFieldValue(poiComponent, "_isQuestGuider", false);
            SetNamedFieldValue(poiComponent, "_text", string.Empty);
            SetNamedFieldValue(poiComponent, "<targetAddress>k__BackingField", null);
            SetNamedFieldValue(poiComponent, "<Permanent>k__BackingField", true);
            SetNamedFieldValue(poiComponent, "enabled", true);

            TryInvokeMethod(poiComponent, "UpdatePosition");
            _nativePoiLastTargetPositions[characterId] = targetTransform.position;
            StreetQuestShared.LogDebug($"Native POI configured characterId={characterId} target={FormatMemberValue(targetTransform)} hidden=False permanent=True initialized=True");
        }

        private Transform ResolveNativePoiTargetTransform(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return null;

            if (!StreetQuestShared.TryGetSpawnedCharacterRoot(characterId, out var characterRoot) || characterRoot == null)
                return null;

            var spawnedTransform = characterRoot.transform;
            var spawnedPath = GetHierarchyPath(spawnedTransform);
            if (spawnedPath.StartsWith("GameManager/ItemsContainer/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnedPath, "GameManager/ItemsContainer", StringComparison.OrdinalIgnoreCase))
            {
                return spawnedTransform;
            }

            var targetParent = ResolveNativePoiTargetParent();
            if (targetParent == null)
                return spawnedTransform;

            if (!_nativePoiTargetAnchors.TryGetValue(characterId, out var anchorTransform) || anchorTransform == null)
            {
                var anchorObject = new GameObject($"StreetQuestPoiTarget.{characterId}");
                anchorTransform = anchorObject.transform;
                anchorTransform.SetParent(targetParent, worldPositionStays: false);
                _nativePoiTargetAnchors[characterId] = anchorTransform;
                StreetQuestShared.LogDebug(
                    $"Native POI anchor created characterId={characterId} parent={GetHierarchyPath(targetParent)}");
            }

            anchorTransform.position = spawnedTransform.position;
            anchorTransform.rotation = spawnedTransform.rotation;
            return anchorTransform;
        }

        private Transform ResolveNativePoiTargetParent()
        {
            if (_nativePoiTargetParent != null)
                return _nativePoiTargetParent;

            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null)
                    continue;

                if (string.Equals(GetHierarchyPath(transform), "GameManager/ItemsContainer", StringComparison.OrdinalIgnoreCase))
                {
                    _nativePoiTargetParent = transform;
                    return _nativePoiTargetParent;
                }
            }

            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null)
                    continue;

                if (string.Equals(transform.name, "ItemsContainer", StringComparison.OrdinalIgnoreCase))
                {
                    _nativePoiTargetParent = transform;
                    return _nativePoiTargetParent;
                }
            }

            return null;
        }

        private bool TryAttachVanillaMarkerVisual(RectTransform template, RectTransform markerRoot)
        {
            if (template == null || markerRoot == null)
                return false;

            var blob = FindChildRectTransform(template, "POIBlob");
            if (blob == null)
                return false;

            var blobCloneObject = Instantiate(blob.gameObject, markerRoot, false);
            blobCloneObject.name = "StreetQuestMarkerBlob";
            blobCloneObject.SetActive(true);

            var blobCloneRect = blobCloneObject.GetComponent<RectTransform>();
            if (blobCloneRect != null)
            {
                blobCloneRect.anchorMin = new Vector2(0.5f, 0.5f);
                blobCloneRect.anchorMax = new Vector2(0.5f, 0.5f);
                blobCloneRect.pivot = new Vector2(0.5f, 0.5f);
                blobCloneRect.anchoredPosition = Vector2.zero;
                blobCloneRect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
                blobCloneRect.localScale = Vector3.one;
                blobCloneRect.localRotation = Quaternion.identity;
            }

            foreach (var canvasGroup in blobCloneObject.GetComponentsInChildren<CanvasGroup>(includeInactive: true))
            {
                if (canvasGroup == null)
                    continue;

                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            foreach (var image in blobCloneObject.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (image == null)
                    continue;

                image.raycastTarget = false;
                image.enabled = true;
                image.material = null;
            }

            StreetQuestShared.LogDebug($"Map marker visual source=vanilla_blob template={template.name}");
            return true;
        }

        private RectTransform ResolvePoiMarkerTemplate()
        {
            if (_poiMarkerTemplate != null && _poiMarkerTemplate.gameObject != null)
                return _poiMarkerTemplate;

            if (_poiRoot == null)
                return null;

            RectTransform fallbackTemplate = null;

            foreach (RectTransform childRect in _poiRoot)
            {
                if (childRect == null || childRect == _streetQuestRoot)
                    continue;

                if (childRect.GetComponent("PointOfInterest") == null)
                    continue;

                if (string.Equals(childRect.name, "Template", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackTemplate ??= childRect;
                    continue;
                }

                if (!childRect.gameObject.activeInHierarchy)
                    continue;

                _poiMarkerTemplate = childRect;
                StreetQuestShared.LogDebug($"Resolved map marker template: {GetHierarchyPath(childRect)}");
                return _poiMarkerTemplate;
            }

            if (fallbackTemplate != null)
            {
                _poiMarkerTemplate = fallbackTemplate;
                StreetQuestShared.LogDebug($"Resolved fallback map marker template: {GetHierarchyPath(fallbackTemplate)}");
                return _poiMarkerTemplate;
            }

            return null;
        }

        private RectTransform ResolveNativePoiTemplate()
        {
            if (_nativePoiTemplate != null && _nativePoiTemplate.gameObject != null)
                return _nativePoiTemplate;

            if (_poiRoot == null)
                return null;

            RectTransform fallbackTemplate = null;

            foreach (RectTransform childRect in _poiRoot)
            {
                if (childRect == null || childRect == _streetQuestRoot)
                    continue;

                var pointOfInterest = childRect.GetComponent("PointOfInterest");
                if (pointOfInterest == null)
                    continue;

                if (string.Equals(childRect.name, "Template", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackTemplate ??= childRect;
                    continue;
                }

                if (!childRect.gameObject.activeInHierarchy)
                    continue;

                if (!TryReadMemberValue(pointOfInterest, "hidden", out var hiddenValue) ||
                    hiddenValue is not bool hidden ||
                    hidden)
                {
                    continue;
                }

                if (!TryReadMemberValue(pointOfInterest, "target", out var targetValue) ||
                    targetValue is not Transform targetTransform ||
                    targetTransform == null)
                {
                    continue;
                }

                var targetPath = GetHierarchyPath(targetTransform);
                if (!targetPath.StartsWith("GameManager/ItemsContainer/", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(targetPath, "GameManager/Player", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _nativePoiTemplate = childRect;
                StreetQuestShared.LogDebug(
                    $"Resolved native POI runtime template: {GetHierarchyPath(childRect)} target={targetPath}");
                return _nativePoiTemplate;
            }

            if (fallbackTemplate != null)
            {
                _nativePoiTemplate = fallbackTemplate;
                StreetQuestShared.LogDebug($"Resolved native POI fallback template: {GetHierarchyPath(_nativePoiTemplate)}");
                return _nativePoiTemplate;
            }

            _nativePoiTemplate = ResolvePoiMarkerTemplate();
            if (_nativePoiTemplate != null)
                StreetQuestShared.LogDebug($"Resolved native POI last-resort template: {GetHierarchyPath(_nativePoiTemplate)}");

            return _nativePoiTemplate;
        }

        private bool TryResolvePoiRoot(out RectTransform poiRoot)
        {
            poiRoot = null;

            if (_poiRoot != null && _poiRoot.gameObject != null)
            {
                poiRoot = _poiRoot;
                return true;
            }

            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rectTransform == null || rectTransform.gameObject == null || !rectTransform.gameObject.activeInHierarchy)
                    continue;

                if (!RootNames.Any(name => string.Equals(rectTransform.name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!IsUnderCityMap(rectTransform))
                    continue;

                _poiRoot = rectTransform;
                poiRoot = rectTransform;
                var hierarchyPath = GetHierarchyPath(rectTransform);
                if (!string.Equals(_lastPoiRootPath, hierarchyPath, StringComparison.Ordinal))
                {
                    _lastPoiRootPath = hierarchyPath;
                    StreetQuestShared.LogDebug($"Resolved POI root: {hierarchyPath}");
                }
                return true;
            }

            return false;
        }

        private void EnsureStreetQuestRoot(RectTransform poiRoot)
        {
            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null && _streetQuestRoot.parent == poiRoot)
                return;

            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null)
                Destroy(_streetQuestRoot.gameObject);

            var rootObject = new GameObject("StreetQuestPOIs", typeof(RectTransform));
            _streetQuestRoot = rootObject.GetComponent<RectTransform>();
            _streetQuestRoot.SetParent(poiRoot, false);
            _streetQuestRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _streetQuestRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _streetQuestRoot.pivot = new Vector2(0.5f, 0.5f);
            _streetQuestRoot.sizeDelta = poiRoot.rect.size;
            _streetQuestRoot.anchoredPosition = Vector2.zero;
            _streetQuestRoot.SetAsLastSibling();
            StreetQuestShared.LogDebug($"Created StreetQuest POI root under {GetHierarchyPath(poiRoot)} size={FormatVector2(_streetQuestRoot.sizeDelta)}");
        }

        private Sprite GetMarkerSprite()
        {
            if (_markerSprite != null)
                return _markerSprite;

            _markerSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            if (_markerSprite != null)
            {
                StreetQuestShared.LogDebug("Loaded built-in UI sprite for map marker.");
                return _markerSprite;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                    texture.SetPixel(x, y, Color.white);
            }

            texture.Apply(false, false);
            _markerSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            StreetQuestShared.LogDebug("Loaded fallback solid sprite for map marker.");
            return _markerSprite;
        }

        private void DumpPoiDiagnosticsOnce(RectTransform poiRoot)
        {
            if (_dumpedPoiSampleDiagnostics || poiRoot == null)
                return;

            _dumpedPoiSampleDiagnostics = true;

            try
            {
                var childRects = poiRoot.GetComponentsInChildren<RectTransform>(includeInactive: false)
                    .Where(value => value != null && value != poiRoot && value != _streetQuestRoot)
                    .Take(8)
                    .ToArray();

                StreetQuestShared.LogDebug($"POI diagnostic dump start childCount={childRects.Length} root={GetHierarchyPath(poiRoot)}");

                foreach (var childRect in childRects)
                    LogPoiDiagnosticEntry(childRect);
            }
            catch (Exception exception)
            {
                StreetQuestShared.LogDebug($"POI diagnostic dump failed: {exception}");
            }
        }

        private void DumpPoiRuntimeDiagnosticsOnce(RectTransform poiRoot)
        {
            if (_dumpedPoiRuntimeDiagnostics || poiRoot == null)
                return;

            _dumpedPoiRuntimeDiagnostics = true;

            try
            {
                LogTransformComponentChain("POI runtime chain", poiRoot.transform, 3);

                foreach (RectTransform childRect in poiRoot)
                {
                    if (childRect == null)
                        continue;

                    var pointOfInterest = childRect.GetComponent("PointOfInterest");
                    if (pointOfInterest == null)
                        continue;

                    LogPointOfInterestRuntime(childRect, pointOfInterest);
                }
            }
            catch (Exception exception)
            {
                StreetQuestShared.LogDebug($"POI runtime diagnostic dump failed: {exception}");
            }
        }

        private bool TryBuildCalibration(RectTransform poiRoot, out CalibrationData calibration)
        {
            calibration = default;
            var samples = new List<CalibrationSample>();

            foreach (RectTransform childRect in poiRoot)
            {
                if (childRect == null || childRect == _streetQuestRoot)
                    continue;

                if (!IsUsableCalibrationPoi(childRect))
                    continue;

                if (!TryExtractCalibrationWorldPosition(childRect, out var worldPosition))
                    continue;

                var anchoredPosition = childRect.anchoredPosition;
                if (float.IsNaN(anchoredPosition.x) || float.IsNaN(anchoredPosition.y))
                    continue;

                samples.Add(new CalibrationSample(worldPosition, anchoredPosition));
            }

            if (samples.Count < 2)
            {
                MaybeLogVerbose($"Map marker calibration aborted: only {samples.Count} usable samples found.");
                return false;
            }

            var minWorldX = samples.Min(value => value.WorldPosition.x);
            var maxWorldX = samples.Max(value => value.WorldPosition.x);
            var minWorldZ = samples.Min(value => value.WorldPosition.z);
            var maxWorldZ = samples.Max(value => value.WorldPosition.z);
            var minUiX = samples.Min(value => value.UiPosition.x);
            var maxUiX = samples.Max(value => value.UiPosition.x);
            var minUiY = samples.Min(value => value.UiPosition.y);
            var maxUiY = samples.Max(value => value.UiPosition.y);

            if (Mathf.Abs(maxWorldX - minWorldX) < 0.01f || Mathf.Abs(maxWorldZ - minWorldZ) < 0.01f)
            {
                MaybeLogVerbose(
                    $"Map marker calibration aborted: insufficient world spread x=({minWorldX:F2},{maxWorldX:F2}) z=({minWorldZ:F2},{maxWorldZ:F2})");
                return false;
            }

            calibration = new CalibrationData
            {
                scaleX = (maxUiX - minUiX) / (maxWorldX - minWorldX),
                offsetX = minUiX - (((maxUiX - minUiX) / (maxWorldX - minWorldX)) * minWorldX),
                scaleY = (maxUiY - minUiY) / (maxWorldZ - minWorldZ),
                offsetY = minUiY - (((maxUiY - minUiY) / (maxWorldZ - minWorldZ)) * minWorldZ)
            };

            StreetQuestShared.LogDebug(
                $"Map marker calibration succeeded samples={samples.Count} scaleX={calibration.scaleX:F4} scaleY={calibration.scaleY:F4}");
            return true;
        }

        private bool IsUsableCalibrationPoi(RectTransform rectTransform)
        {
            if (rectTransform == null || !rectTransform.gameObject.activeInHierarchy)
                return false;

            var pointOfInterest = rectTransform.GetComponent("PointOfInterest");
            if (pointOfInterest == null)
                return false;

            if (TryReadMemberValue(pointOfInterest, "hidden", out var hiddenValue) &&
                hiddenValue is bool hidden &&
                hidden)
            {
                return false;
            }

            if (TryReadMemberValue(pointOfInterest, "target", out var targetValue) &&
                !TryResolveTargetWorldPosition(targetValue, out _))
            {
                return false;
            }

            return true;
        }

        private bool TryExtractCalibrationWorldPosition(RectTransform rectTransform, out Vector3 worldPosition)
        {
            if (TryExtractVanillaPoiWorldPosition(rectTransform, out worldPosition))
                return true;

            return TryExtractWorldPosition(rectTransform, out worldPosition);
        }

        private bool TryExtractVanillaPoiWorldPosition(RectTransform rectTransform, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (rectTransform == null)
                return false;

            foreach (var component in rectTransform.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                var type = component.GetType();
                if (!string.Equals(type.Name, "PointOfInterest", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryReadMemberValue(component, "target", out var targetValue) &&
                    TryResolveTargetWorldPosition(targetValue, out worldPosition))
                {
                    return true;
                }

                if (TryReadMemberValue(component, "targetAddress", out var targetAddressValue) &&
                    TryResolveTargetWorldPosition(targetAddressValue, out worldPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryMapWorldToUi(Vector3 worldPosition, out Vector2 anchoredPosition)
        {
            anchoredPosition = Vector2.zero;
            if (!_hasCalibration)
                return false;

            anchoredPosition = new Vector2(
                (_calibration.scaleX * worldPosition.x) + _calibration.offsetX,
                (_calibration.scaleY * worldPosition.z) + _calibration.offsetY);
            return true;
        }

        private bool TryExtractWorldPosition(RectTransform rectTransform, out Vector3 worldPosition)
        {
            worldPosition = default;
            var visited = new HashSet<object>();

            for (var current = rectTransform.transform; current != null; current = current.parent)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    if (TryExtractWorldPositionFromObject(component, visited, 0, out worldPosition))
                        return true;
                }

                if (current == rectTransform.transform.parent?.parent)
                    break;
            }

            return false;
        }

        private static bool TryReadMemberValue(object instance, string memberName, out object value)
        {
            value = null;
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var type = instance.GetType();

            var field = type.GetField(memberName, ReflectionFlags);
            if (field != null)
            {
                try
                {
                    value = field.GetValue(instance);
                    return true;
                }
                catch
                {
                }
            }

            var property = type.GetProperty(memberName, ReflectionFlags);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                return false;

            try
            {
                value = property.GetValue(instance, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveTargetWorldPosition(object candidate, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (candidate == null)
                return false;

            switch (candidate)
            {
                case Transform transform:
                    worldPosition = transform.position;
                    return true;
                case Component component:
                    worldPosition = component.transform.position;
                    return true;
                case GameObject gameObject:
                    worldPosition = gameObject.transform.position;
                    return true;
                case Vector3 vector3:
                    worldPosition = vector3;
                    return true;
            }

            if (TryReadMemberValue(candidate, "position", out var positionValue))
            {
                switch (positionValue)
                {
                    case Vector3 vector3:
                        worldPosition = vector3;
                        return true;
                    case Transform transform:
                        worldPosition = transform.position;
                        return true;
                    case Component component:
                        worldPosition = component.transform.position;
                        return true;
                    case GameObject gameObject:
                        worldPosition = gameObject.transform.position;
                        return true;
                }
            }

            return false;
        }

        private void LogPoiDiagnosticEntry(RectTransform rectTransform)
        {
            var builder = new StringBuilder();
            builder.Append("POI child: path=");
            builder.Append(GetHierarchyPath(rectTransform));
            builder.Append(", anchored=");
            builder.Append(FormatVector2(rectTransform.anchoredPosition));
            builder.Append(", size=");
            builder.Append(FormatVector2(rectTransform.sizeDelta));
            builder.Append(", components=[");

            var firstComponent = true;
            foreach (var component in rectTransform.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (!firstComponent)
                    builder.Append("; ");

                firstComponent = false;
                builder.Append(component.GetType().FullName);

                var interestingMembers = DescribeInterestingMembers(component);
                if (!string.IsNullOrWhiteSpace(interestingMembers))
                {
                    builder.Append(" {");
                    builder.Append(interestingMembers);
                    builder.Append("}");
                }
            }

            builder.Append(']');
            StreetQuestShared.LogDebug(builder.ToString());

            if (TryExtractWorldPosition(rectTransform, out var extractedWorldPosition))
            {
                StreetQuestShared.LogDebug(
                    $"POI child extracted world: path={GetHierarchyPath(rectTransform)} world={FormatVector3(extractedWorldPosition)}");
            }
            else
            {
                StreetQuestShared.LogDebug($"POI child extracted world: path={GetHierarchyPath(rectTransform)} world=<none>");
            }
        }

        private string DescribeInterestingMembers(Component component)
        {
            var values = new List<string>();
            var type = component.GetType();

            foreach (var field in type.GetFields(ReflectionFlags))
            {
                if (!ShouldInspectMember(field.Name))
                    continue;

                try
                {
                    var value = field.GetValue(component);
                    values.Add($"{field.Name}={FormatMemberValue(value)}");
                }
                catch
                {
                }
            }

            foreach (var property in type.GetProperties(ReflectionFlags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0 || !ShouldInspectMember(property.Name))
                    continue;

                try
                {
                    var value = property.GetValue(component, null);
                    values.Add($"{property.Name}={FormatMemberValue(value)}");
                }
                catch
                {
                }
            }

            return string.Join(", ", values.Distinct(StringComparer.Ordinal));
        }

        private void LogTransformComponentChain(string label, Transform start, int maxDepth)
        {
            var depth = 0;
            for (var current = start; current != null && depth < maxDepth; current = current.parent, depth++)
            {
                var componentNames = current.GetComponents<Component>()
                    .Where(value => value != null)
                    .Select(value => value.GetType().FullName)
                    .ToArray();
                StreetQuestShared.LogDebug(
                    $"{label}: depth={depth} path={GetHierarchyPath(current)} components=[{string.Join(", ", componentNames)}]");
            }
        }

        private void LogPointOfInterestRuntime(RectTransform rectTransform, Component pointOfInterest)
        {
            if (rectTransform == null || pointOfInterest == null)
                return;

            var type = pointOfInterest.GetType();
            var builder = new StringBuilder();
            builder.Append("POI runtime: path=");
            builder.Append(GetHierarchyPath(rectTransform));
            builder.Append(", type=");
            builder.Append(type.FullName);
            builder.Append(", target=");
            builder.Append(ReadNamedMemberSnapshot(pointOfInterest, "target"));
            builder.Append(", targetAddress=");
            builder.Append(ReadNamedMemberSnapshot(pointOfInterest, "targetAddress"));
            builder.Append(", pointerRectTransform=");
            builder.Append(ReadNamedMemberSnapshot(pointOfInterest, "pointerRectTransform"));
            builder.Append(", buildingIcon=");
            builder.Append(ReadNamedMemberSnapshot(pointOfInterest, "buildingIcon"));
            StreetQuestShared.LogDebug(builder.ToString());

            var methodNames = type.GetMethods(ReflectionFlags)
                .Where(method => method != null)
                .Select(method => method.Name)
                .Where(name =>
                    name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("address", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("init", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("refresh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("hide", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            StreetQuestShared.LogDebug(
                $"POI runtime methods type={type.FullName} methods=[{string.Join(", ", methodNames)}]");

            var initializeMethods = type.GetMethods(ReflectionFlags)
                .Where(method => method != null && string.Equals(method.Name, "Initialize", StringComparison.Ordinal))
                .ToArray();
            foreach (var method in initializeMethods)
            {
                var parameters = method.GetParameters()
                    .Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}")
                    .ToArray();
                StreetQuestShared.LogDebug(
                    $"POI runtime initialize-signature type={type.FullName} signature={method.ReturnType.FullName} Initialize({string.Join(", ", parameters)})");
            }

            var fieldSnapshots = type.GetFields(ReflectionFlags)
                .Where(field => field != null)
                .Select(field => $"{field.FieldType.FullName} {field.Name}={SafeReadMemberValue(() => field.GetValue(pointOfInterest))}")
                .ToArray();
            StreetQuestShared.LogDebug(
                $"POI runtime fields type={type.FullName} fields=[{string.Join("; ", fieldSnapshots)}]");

            var propertySnapshots = type.GetProperties(ReflectionFlags)
                .Where(property => property != null && property.GetIndexParameters().Length == 0)
                .Select(property => $"{property.PropertyType.FullName} {property.Name}={SafeReadMemberValue(() => property.CanRead ? property.GetValue(pointOfInterest, null) : "<write-only>")}")
                .ToArray();
            StreetQuestShared.LogDebug(
                $"POI runtime properties type={type.FullName} properties=[{string.Join("; ", propertySnapshots)}]");
        }

        private string ReadNamedMemberSnapshot(object instance, string memberName)
        {
            if (!TryReadMemberValue(instance, memberName, out var value))
                return "<missing>";

            return FormatMemberValue(value);
        }

        private static bool SetNamedFieldValue(object instance, string fieldName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
                return false;

            var field = instance.GetType().GetField(fieldName, ReflectionFlags);
            if (field == null)
                return false;

            try
            {
                field.SetValue(instance, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            var method = instance.GetType().GetMethod(methodName, ReflectionFlags, null, Type.EmptyTypes, null);
            if (method == null)
                return false;

            try
            {
                method.Invoke(instance, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeReadMemberValue(Func<object> getter)
        {
            try
            {
                return FormatMemberValue(getter());
            }
            catch (Exception exception)
            {
                return $"<error:{exception.GetType().Name}>";
            }
        }

        private static string FormatMemberValue(object value)
        {
            if (value == null)
                return "<null>";

            return value switch
            {
                Vector3 vector3 => FormatVector3(vector3),
                Vector2 vector2 => FormatVector2(vector2),
                Transform transform => $"Transform({GetHierarchyPath(transform)})",
                Component component => component.GetType().FullName,
                GameObject gameObject => $"GameObject({GetHierarchyPath(gameObject.transform)})",
                _ => value.ToString()
            };
        }

        private bool TryExtractWorldPositionFromObject(
            object candidate,
            HashSet<object> visited,
            int depth,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (candidate == null || depth > 2 || !visited.Add(candidate))
                return false;

            switch (candidate)
            {
                case Transform transform when depth > 0:
                    worldPosition = transform.position;
                    return true;
                case Component component when depth > 0:
                    worldPosition = component.transform.position;
                    return true;
                case GameObject gameObject when depth > 0:
                    worldPosition = gameObject.transform.position;
                    return true;
                case Vector3 vector3:
                    worldPosition = vector3;
                    return true;
            }

            var type = candidate.GetType();

            foreach (var field in type.GetFields(ReflectionFlags))
            {
                if (!ShouldInspectMember(field.Name))
                    continue;

                object value;
                try
                {
                    value = field.GetValue(candidate);
                }
                catch
                {
                    continue;
                }

                if (TryExtractWorldPositionFromValue(value, visited, depth, out worldPosition))
                    return true;
            }

            foreach (var property in type.GetProperties(ReflectionFlags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0 || !ShouldInspectMember(property.Name))
                    continue;

                object value;
                try
                {
                    value = property.GetValue(candidate, null);
                }
                catch
                {
                    continue;
                }

                if (TryExtractWorldPositionFromValue(value, visited, depth, out worldPosition))
                    return true;
            }

            return false;
        }

        private bool TryExtractWorldPositionFromValue(
            object value,
            HashSet<object> visited,
            int depth,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (value == null)
                return false;

            switch (value)
            {
                case Vector3 vector3:
                    worldPosition = vector3;
                    return true;
                case Transform transform:
                    worldPosition = transform.position;
                    return true;
                case Component component:
                    worldPosition = component.transform.position;
                    return true;
                case GameObject gameObject:
                    worldPosition = gameObject.transform.position;
                    return true;
            }

            return TryExtractWorldPositionFromObject(value, visited, depth + 1, out worldPosition);
        }

        private static bool ShouldInspectMember(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
                return false;

            return PositionMemberKeywords.Any(keyword =>
                memberName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsCityMapOpen()
        {
            _cityMapType ??= FindType("CityMap");
            if (_cityMapType == null)
                return false;

            var isOpenProperty = _cityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return isOpenProperty?.GetValue(null) as bool? ?? false;
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var exactType = assembly.GetType(typeName, throwOnError: false);
                if (exactType != null)
                    return exactType;

                Type[] types;
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

                    if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                        (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static bool IsUnderCityMap(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (string.Equals(current.name, "CityMap", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);

            return string.Join("/", names);
        }

        private static RectTransform FindChildRectTransform(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return null;

            foreach (var rectTransform in root.GetComponentsInChildren<RectTransform>(includeInactive: true))
            {
                if (rectTransform == null)
                    continue;

                if (string.Equals(rectTransform.name, childName, StringComparison.OrdinalIgnoreCase))
                    return rectTransform;
            }

            return null;
        }

        private void SetMarkerActive(string characterId, bool active)
        {
            if (_markerRoots.TryGetValue(characterId, out var markerRoot) && markerRoot != null)
                markerRoot.gameObject.SetActive(active);
        }

        private void HideStreetQuestRoot()
        {
            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null)
                _streetQuestRoot.gameObject.SetActive(false);
        }

        private void DestroyCustomMarkerRoot(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (!_markerRoots.TryGetValue(characterId, out var markerRoot) || markerRoot == null)
                return;

            Destroy(markerRoot.gameObject);
            _markerRoots.Remove(characterId);
            StreetQuestShared.LogDebug($"Destroyed custom marker root characterId={characterId}");
        }

        private void DestroyMarkerImages()
        {
            foreach (var markerRoot in _markerRoots.Values)
            {
                if (markerRoot != null)
                    Destroy(markerRoot.gameObject);
            }

            _markerRoots.Clear();
            _nativePoiComponents.Clear();
            _nativePoiTargetAnchors.Clear();
            _nativePoiLastTargetPositions.Clear();
            _markerVisibilityStates.Clear();
            _markerStatusReasons.Clear();
        }

        private void DestroyLingeringStreetQuestMapObjects()
        {
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.gameObject == null)
                    continue;

                var name = transform.gameObject.name;
                if (!name.StartsWith("StreetQuestMapMarker.", StringComparison.Ordinal) &&
                    !name.StartsWith("StreetQuestNativePoi.", StringComparison.Ordinal) &&
                    !name.StartsWith("StreetQuestPoiTarget.", StringComparison.Ordinal) &&
                    !string.Equals(name, "StreetQuestPOIs", StringComparison.Ordinal))
                {
                    continue;
                }

                Destroy(transform.gameObject);
            }
        }

        private void LogLifecycleState(string state)
        {
            if (string.Equals(_lastLifecycleState, state, StringComparison.Ordinal))
                return;

            _lastLifecycleState = state;
            StreetQuestShared.LogDebug($"MapMarkerWatcher: {state}");
        }

        private void LogKnownCharacters(IReadOnlyCollection<string> knownCharacterIds)
        {
            var snapshot = knownCharacterIds == null || knownCharacterIds.Count == 0
                ? "<none>"
                : string.Join(", ", knownCharacterIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            if (string.Equals(_lastKnownCharacterSnapshot, snapshot, StringComparison.Ordinal))
                return;

            _lastKnownCharacterSnapshot = snapshot;
            StreetQuestShared.LogDebug($"Map marker known NPCs: {snapshot}");
        }

        private void LogMarkerState(string characterId, bool isVisible, string reason)
        {
            _markerVisibilityStates.TryGetValue(characterId, out var previousVisibility);
            _markerStatusReasons.TryGetValue(characterId, out var previousReason);

            if (previousVisibility == isVisible && string.Equals(previousReason, reason, StringComparison.Ordinal))
                return;

            _markerVisibilityStates[characterId] = isVisible;
            _markerStatusReasons[characterId] = reason;
            StreetQuestShared.LogDebug($"Map marker characterId={characterId} visible={isVisible} reason={reason}");
        }

        private void MaybeLogVerbose(string message)
        {
            if (_elapsedSeconds < _nextVerboseLogAtSeconds)
                return;

            _nextVerboseLogAtSeconds = _elapsedSeconds + 2f;
            StreetQuestShared.LogDebug($"MapMarkerWatcher: {message}");
        }

        private void MaybeLogMarkerScreenRect(string characterId, RectTransform rectTransform)
        {
            if (rectTransform == null || string.IsNullOrWhiteSpace(characterId))
                return;

            if (_elapsedSeconds < _nextVerboseLogAtSeconds)
                return;

            _nextVerboseLogAtSeconds = _elapsedSeconds + 2f;
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            StreetQuestShared.LogDebug(
                $"Map marker screenRect characterId={characterId} " +
                $"bl={FormatVector3(corners[0])} tl={FormatVector3(corners[1])} tr={FormatVector3(corners[2])} br={FormatVector3(corners[3])} " +
                $"active={rectTransform.gameObject.activeInHierarchy} canvas={rectTransform.GetComponentInParent<Canvas>()?.name ?? "<none>"}");
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"({value.x:F2}, {value.y:F2})";
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
        }

        private readonly struct CalibrationSample
        {
            public CalibrationSample(Vector3 worldPosition, Vector2 uiPosition)
            {
                WorldPosition = worldPosition;
                UiPosition = uiPosition;
            }

            public Vector3 WorldPosition { get; }
            public Vector2 UiPosition { get; }
        }

        private struct CalibrationData
        {
            public float scaleX;
            public float offsetX;
            public float scaleY;
            public float offsetY;
        }
    }
}
