using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    [DefaultExecutionOrder(-9990)]
    internal sealed class StreetQuestMapMarkerWatcher : MonoBehaviour
    {
        private static readonly bool EnableMarkerDebugLogging = true;
        private const bool PreferNativePoiMarkers = false;
        private const float UpdateIntervalSeconds = 0f;
        private const float MarkerVerticalOffset = 0f;
        private const float MarkerSmoothTime = 0.06f;
        private const float MarkerSnapDistance = 32f;
        private const float MarkerDeadzoneDistance = 0.35f;

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
        private static readonly string[] CalibrationAnchorNames =
        {
            "A",
            "B",
            "C"
        };
        private const float CalibrationAnchorSpan = 600f;
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
        private const string MarkerIconFileName = "person.png";

        private float _elapsedSeconds;
        private float _nextRefreshAtSeconds;
        private Type _cityMapType;
        private PropertyInfo _cityMapIsOpenProperty;
        private RectTransform _poiRoot;
        private RectTransform _streetQuestRoot;
        private Sprite _markerSprite;
        private bool _loggedCalibrationFailure;
        private bool _hasCalibration;
        private CalibrationData _calibration;
        private readonly Dictionary<string, Component> _nativePoiComponents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Transform> _nativePoiTargetAnchors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RectTransform> _markerRoots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _markerAnchoredPositions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _markerAnchoredVelocities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _markerVisibilityStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _markerStatusReasons = new(StringComparer.OrdinalIgnoreCase);
        private string _lastLifecycleState;
        private string _lastKnownCharacterSnapshot;
        private string _lastPoiRootPath;
        private string _lastCalibrationSnapshot;
        private float _nextVerboseLogAtSeconds;
        private RectTransform _poiMarkerTemplate;
        private RectTransform _nativePoiTemplate;
        private Transform _nativePoiTargetParent;
        private readonly Dictionary<string, Vector3> _nativePoiLastTargetPositions = new(StringComparer.OrdinalIgnoreCase);
        private readonly RectTransform[] _calibrationAnchorRects = new RectTransform[3];
        private readonly Component[] _calibrationAnchorPoiComponents = new Component[3];
        private readonly Transform[] _calibrationAnchorTargets = new Transform[3];
        private readonly Vector3[] _calibrationAnchorWorldPositions = new Vector3[3];
        private bool _calibrationAnchorWorldPositionsInitialized;
        private float _nextCalibrationLogAtSeconds;
        private bool _useProjectionCalibration;
        private Camera _projectionCamera;
        private Camera _projectionUiCamera;
        private Vector2 _projectionOffset;
        private RectTransform _lastPlayerPoiRect;
        private Vector3 _lastPlayerWorldPosition;
        private Vector2 _lastPlayerUiPosition;

        public void Initialize()
        {
            _elapsedSeconds = 0f;
            _nextRefreshAtSeconds = 0f;
            _poiRoot = null;
            _streetQuestRoot = null;
            _cityMapIsOpenProperty = null;
            _poiMarkerTemplate = null;
            _nativePoiTemplate = null;
            _nativePoiTargetParent = null;
            _loggedCalibrationFailure = false;
            _hasCalibration = false;
            _calibration = default;
            _lastLifecycleState = null;
            _lastKnownCharacterSnapshot = null;
            _lastPoiRootPath = null;
            _lastCalibrationSnapshot = null;
            _nextVerboseLogAtSeconds = 0f;
            _nextCalibrationLogAtSeconds = 0f;
            _calibrationAnchorWorldPositionsInitialized = false;
            _useProjectionCalibration = false;
            _projectionCamera = null;
            _projectionUiCamera = null;
            _projectionOffset = Vector2.zero;
            _lastPlayerPoiRect = null;
            _lastPlayerWorldPosition = default;
            _lastPlayerUiPosition = default;
            DestroyLingeringStreetQuestMapObjects();
            DestroyMarkerImages();
        }

        private void OnDestroy()
        {
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
            EnsureCalibration(poiRoot);
            UpdateKnownNpcMarkers();
        }

        private void EnsureCalibration(RectTransform poiRoot)
        {
            _poiRoot = poiRoot;

            _hasCalibration = TryBuildProjectionCalibration(poiRoot);
            if (_hasCalibration)
            {
                _loggedCalibrationFailure = false;
                return;
            }

            _useProjectionCalibration = false;
            _projectionCamera = null;
            _projectionUiCamera = null;

            if (_loggedCalibrationFailure)
                return;

            StreetQuestShared.LogDebug(
                "Map marker calibration failed: could not resolve player POI + map camera projection. " +
                "NPC map markers are hidden instead of falling back to moving vanilla/player marker samples.");
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
                _markerAnchoredPositions.Remove(existing);
                _markerAnchoredVelocities.Remove(existing);
                _markerVisibilityStates.Remove(existing);
                _markerStatusReasons.Remove(existing);
                DebugLog($"Map marker removed characterId={existing}");
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

                var targetAnchoredPosition = anchoredPosition + new Vector2(0f, MarkerVerticalOffset);
                markerRoot.anchoredPosition = targetAnchoredPosition;
                ApplyPlayerMarkerVisualSize(markerRoot);
                _markerAnchoredPositions[characterId] = targetAnchoredPosition;
                _markerAnchoredVelocities[characterId] = Vector2.zero;
                markerRoot.gameObject.SetActive(true);
                LogMarkerState(
                    characterId,
                    true,
                    "Placed marker with dummy-anchor calibration.");
            }
        }

        private void ApplyPlayerMarkerVisualSize(RectTransform markerRoot)
        {
            if (markerRoot == null)
                return;

            var playerRect = _lastPlayerPoiRect;
            if (playerRect == null || playerRect.gameObject == null)
            {
                TryResolvePlayerPoiSample(out playerRect, out _, out _);
            }

            if (playerRect == null)
                return;

            markerRoot.sizeDelta = playerRect.sizeDelta;
            markerRoot.localScale = playerRect.localScale;
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
                DebugLog($"Map marker template missing for characterId={characterId}");
                return null;
            }

            var markerObject = new GameObject(
                $"StreetQuestMapMarker.{characterId}",
                typeof(RectTransform));
            var rectTransform = markerObject.GetComponent<RectTransform>();
            rectTransform.SetParent(_streetQuestRoot, false);
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = template.pivot;
            rectTransform.sizeDelta = template.sizeDelta;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.SetAsLastSibling();

            if (!TryAttachVanillaMarkerVisual(template, rectTransform))
            {
                var image = markerObject.GetComponent<Image>();
                if (image == null)
                    image = markerObject.AddComponent<Image>();
                image.sprite = GetMarkerSprite();
                image.color = new Color(1f, 0f, 1f, 1f);
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.enabled = true;
                DebugLog($"Map marker visual fallback=plain_image characterId={characterId}");
            }

            _markerRoots[characterId] = rectTransform;
            DebugLog(
                $"Map marker created characterId={characterId} template={template.name} anchorMin={FormatVector2(template.anchorMin)} anchorMax={FormatVector2(template.anchorMax)} pivot={FormatVector2(template.pivot)} size={FormatVector2(template.sizeDelta)}");
            return rectTransform;
        }

        private bool EnsureNativePoiMarker(string characterId, Vector3 worldPosition)
        {
            if (_poiRoot == null || string.IsNullOrWhiteSpace(characterId))
                return false;

            if (_nativePoiComponents.TryGetValue(characterId, out var existingPoi) && existingPoi != null)
            {
                existingPoi.gameObject.SetActive(true);
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
            AttachNativePoiVisuals(template, poiObject.transform);

            var poiComponent = poiObject.GetComponent(pointOfInterestTemplate.GetType().Name) as Component;
            if (poiComponent == null)
            {
                Destroy(poiObject);
                return false;
            }

            TryInvokeMethod(poiComponent, "Initialize");
            ConfigureNativePoiComponent(poiComponent, characterId, worldPosition);
            _nativePoiComponents[characterId] = poiComponent;
            DebugLog($"Native POI created characterId={characterId} template={template.name}");
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
                DebugLog($"Native POI target missing characterId={characterId} world={FormatVector3(worldPosition)}");
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
            DebugLog($"Native POI configured characterId={characterId} target={FormatMemberValue(targetTransform)} hidden=False permanent=True initialized=True");
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
                DebugLog(
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

            var pointer = FindChildRectTransform(template, "POIPointer");
            var blob = FindChildRectTransform(template, "POIBlob");
            if (blob == null && pointer == null)
                return false;

            if (pointer != null)
            {
                var pointerCloneObject = Instantiate(pointer.gameObject, markerRoot, false);
                pointerCloneObject.name = "StreetQuestMarkerPointer";
                pointerCloneObject.SetActive(true);

                var pointerCloneRect = pointerCloneObject.GetComponent<RectTransform>();
                if (pointerCloneRect != null)
                {
                    pointerCloneRect.anchorMin = pointer.anchorMin;
                    pointerCloneRect.anchorMax = pointer.anchorMax;
                    pointerCloneRect.pivot = pointer.pivot;
                    pointerCloneRect.anchoredPosition = pointer.anchoredPosition;
                    pointerCloneRect.sizeDelta = pointer.sizeDelta;
                    pointerCloneRect.localScale = Vector3.one;
                    pointerCloneRect.localRotation = Quaternion.identity;
                }

                PrepareMarkerVisual(pointerCloneObject);
            }

            if (blob != null)
            {
                var blobCloneObject = Instantiate(blob.gameObject, markerRoot, false);
                blobCloneObject.name = "StreetQuestMarkerBlob";
                blobCloneObject.SetActive(true);

                var blobCloneRect = blobCloneObject.GetComponent<RectTransform>();
                if (blobCloneRect != null)
                {
                    blobCloneRect.anchorMin = blob.anchorMin;
                    blobCloneRect.anchorMax = blob.anchorMax;
                    blobCloneRect.pivot = blob.pivot;
                    blobCloneRect.anchoredPosition = blob.anchoredPosition;
                    blobCloneRect.sizeDelta = blob.sizeDelta;
                    blobCloneRect.localScale = Vector3.one;
                    blobCloneRect.localRotation = Quaternion.identity;
                }

                PrepareMarkerVisual(blobCloneObject);
                ApplyCustomMarkerIcon(blobCloneObject.transform);
            }

            DebugLog($"Map marker visual source=vanilla_blob template={template.name}");
            return true;
        }

        private void ApplyCustomMarkerIcon(Transform rootTransform)
        {
            if (rootTransform == null)
                return;

            var iconRect = FindChildRectTransform(rootTransform, "Icon");
            if (iconRect == null)
                return;

            var iconImage = iconRect.GetComponent<Image>();
            if (iconImage == null)
                return;

            iconImage.sprite = GetMarkerSprite();
            iconImage.color = Color.white;
            iconImage.material = null;
            iconImage.preserveAspect = true;
        }

        private static void PrepareMarkerVisual(GameObject rootObject)
        {
            if (rootObject == null)
                return;

            foreach (var canvasGroup in rootObject.GetComponentsInChildren<CanvasGroup>(includeInactive: true))
            {
                if (canvasGroup == null)
                    continue;

                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            foreach (var image in rootObject.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (image == null)
                    continue;

                image.raycastTarget = false;
                image.enabled = true;
                image.material = null;
            }
        }

        private RectTransform ResolvePoiMarkerTemplate()
        {
            if (_poiMarkerTemplate != null && _poiMarkerTemplate.gameObject != null)
                return _poiMarkerTemplate;

            if (_poiRoot == null)
                return null;

            RectTransform fallbackTemplate = null;
            RectTransform structuredFallbackTemplate = null;
            RectTransform dynamicStructuredTemplate = null;

            foreach (RectTransform childRect in _poiRoot)
            {
                if (childRect == null || childRect == _streetQuestRoot)
                    continue;

                if (IsCalibrationAnchorRect(childRect))
                    continue;

                if (childRect.GetComponent("PointOfInterest") == null)
                    continue;

                if (string.Equals(childRect.name, "Template", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackTemplate ??= childRect;
                    if (HasMarkerVisualStructure(childRect))
                    {
                        if (!IsDynamicPoiTemplate(childRect))
                            structuredFallbackTemplate ??= childRect;
                        else
                            dynamicStructuredTemplate ??= childRect;
                    }
                    continue;
                }

                if (!HasMarkerVisualStructure(childRect))
                    continue;

                if (!childRect.gameObject.activeInHierarchy)
                    continue;

                if (IsDynamicPoiTemplate(childRect))
                {
                    dynamicStructuredTemplate ??= childRect;
                    continue;
                }

                _poiMarkerTemplate = childRect;
                StreetQuestShared.LogDebug($"Resolved map marker template: {GetHierarchyPath(childRect)}");
                return _poiMarkerTemplate;
            }

            if (structuredFallbackTemplate != null)
            {
                _poiMarkerTemplate = structuredFallbackTemplate;
                StreetQuestShared.LogDebug($"Resolved structured fallback map marker template: {GetHierarchyPath(structuredFallbackTemplate)}");
                return _poiMarkerTemplate;
            }

            if (dynamicStructuredTemplate != null)
            {
                _poiMarkerTemplate = dynamicStructuredTemplate;
                StreetQuestShared.LogDebug($"Resolved dynamic/player map marker template: {GetHierarchyPath(dynamicStructuredTemplate)}");
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

        private static bool HasMarkerVisualStructure(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return false;

            return FindChildRectTransform(rectTransform, "POIBlob") != null ||
                   FindChildRectTransform(rectTransform, "POIPointer") != null;
        }

        private static bool IsDynamicPoiTemplate(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return false;

            var pointOfInterest = rectTransform.GetComponent("PointOfInterest");
            return pointOfInterest != null && IsDynamicPoiTarget(pointOfInterest);
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
                DebugLog(
                    $"Resolved native POI runtime template: {GetHierarchyPath(childRect)} target={targetPath}");
                return _nativePoiTemplate;
            }

            if (fallbackTemplate != null)
            {
                _nativePoiTemplate = fallbackTemplate;
                DebugLog($"Resolved native POI fallback template: {GetHierarchyPath(_nativePoiTemplate)}");
                return _nativePoiTemplate;
            }

            _nativePoiTemplate = ResolvePoiMarkerTemplate();
            if (_nativePoiTemplate != null)
                DebugLog($"Resolved native POI last-resort template: {GetHierarchyPath(_nativePoiTemplate)}");

            return _nativePoiTemplate;
        }

        private bool TryResolvePoiRoot(out RectTransform poiRoot)
        {
            poiRoot = null;

            if (_poiRoot != null &&
                _poiRoot.gameObject != null &&
                _poiRoot.gameObject.activeInHierarchy &&
                IsUnderCityMap(_poiRoot))
            {
                poiRoot = _poiRoot;
                return true;
            }

            _poiRoot = null;

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
                    DebugLog($"Resolved POI root: {hierarchyPath}");
                }
                return true;
            }

            return false;
        }

        private void EnsureStreetQuestRoot(RectTransform poiRoot)
        {
            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null && _streetQuestRoot.parent == poiRoot)
            {
                _streetQuestRoot.anchorMin = new Vector2(0.5f, 0.5f);
                _streetQuestRoot.anchorMax = new Vector2(0.5f, 0.5f);
                _streetQuestRoot.pivot = new Vector2(0.5f, 0.5f);
                _streetQuestRoot.sizeDelta = poiRoot.rect.size;
                _streetQuestRoot.anchoredPosition = Vector2.zero;
                _streetQuestRoot.SetAsLastSibling();
                return;
            }

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
            DebugLog($"Created StreetQuest POI root under {GetHierarchyPath(poiRoot)} size={FormatVector2(_streetQuestRoot.sizeDelta)}");
        }

        private Sprite GetMarkerSprite()
        {
            if (_markerSprite != null)
                return _markerSprite;

            var markerIconPath = ResolveInstalledMarkerIconPath();
            if (!string.IsNullOrWhiteSpace(markerIconPath) && File.Exists(markerIconPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(markerIconPath);
                    var iconTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    iconTexture.filterMode = FilterMode.Bilinear;
                    if (iconTexture.LoadImage(bytes, false))
                    {
                        _markerSprite = Sprite.Create(
                            iconTexture,
                            new Rect(0f, 0f, iconTexture.width, iconTexture.height),
                            new Vector2(0.5f, 0.5f),
                            100f);
                        DebugLog(
                            $"Loaded marker sprite from {MarkerIconFileName} size={iconTexture.width}x{iconTexture.height}");
                        return _markerSprite;
                    }
                }
                catch (Exception exception)
                {
                    StreetQuestShared.LogDebug($"Failed loading marker sprite from {MarkerIconFileName}: {exception.Message}");
                }
            }

            _markerSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            if (_markerSprite != null)
            {
                DebugLog("Loaded built-in UI sprite for map marker.");
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
            DebugLog("Loaded fallback solid sprite for map marker.");
            return _markerSprite;
        }

        private static string ResolveInstalledMarkerIconPath()
        {
            try
            {
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(assemblyLocation))
                    return null;

                var modDirectory = Path.GetDirectoryName(assemblyLocation);
                if (string.IsNullOrWhiteSpace(modDirectory))
                    return null;

                return Path.Combine(modDirectory, MarkerIconFileName);
            }
            catch
            {
                return null;
            }
        }

        private bool TryBuildProjectionCalibration(RectTransform poiRoot)
        {
            _useProjectionCalibration = false;

            if (poiRoot == null)
                return false;

            if (!TryResolvePlayerPoiSample(out var playerRect, out var playerWorldPosition, out var playerAnchoredPosition))
            {
                MaybeLogVerbose("Projection calibration failed: no live Player PointOfInterest sample found.");
                return false;
            }

            var projectionCamera = ResolveMapProjectionCamera();
            if (projectionCamera == null)
            {
                MaybeLogVerbose("Projection calibration failed: no map/render camera found.");
                return false;
            }

            var uiCamera = ResolveCanvasCamera(poiRoot);
            if (!TryProjectWorldPositionToPoiLocal(playerWorldPosition, projectionCamera, uiCamera, out var projectedPlayerPosition))
            {
                MaybeLogVerbose(
                    $"Projection calibration failed: could not project player world={FormatVector3(playerWorldPosition)} with camera={projectionCamera.name}.");
                return false;
            }

            _useProjectionCalibration = true;
            _projectionCamera = projectionCamera;
            _projectionUiCamera = uiCamera;
            _projectionOffset = playerAnchoredPosition - projectedPlayerPosition;
            _lastPlayerPoiRect = playerRect;
            _lastPlayerWorldPosition = playerWorldPosition;
            _lastPlayerUiPosition = playerAnchoredPosition;

            if (_elapsedSeconds >= _nextCalibrationLogAtSeconds)
            {
                _nextCalibrationLogAtSeconds = _elapsedSeconds + 2f;
                StreetQuestShared.LogDebug(
                    $"Map marker projection calibration playerWorld={FormatVector3(playerWorldPosition)} " +
                    $"playerUi={FormatVector2(playerAnchoredPosition)} projectedPlayerUi={FormatVector2(projectedPlayerPosition)} " +
                    $"offset={FormatVector2(_projectionOffset)} camera={GetHierarchyPath(projectionCamera.transform)} " +
                    $"uiCamera={(_projectionUiCamera != null ? GetHierarchyPath(_projectionUiCamera.transform) : "<overlay>")}");
            }

            return true;
        }

        private bool TryResolvePlayerPoiSample(out RectTransform playerRect, out Vector3 playerWorldPosition, out Vector2 playerAnchoredPosition)
        {
            playerRect = null;
            playerWorldPosition = default;
            playerAnchoredPosition = default;

            if (_poiRoot == null)
                return false;

            foreach (RectTransform childRect in _poiRoot)
            {
                if (childRect == null || childRect == _streetQuestRoot)
                    continue;

                if (IsCalibrationAnchorRect(childRect))
                    continue;

                var pointOfInterest = childRect.GetComponent("PointOfInterest");
                if (pointOfInterest == null)
                    continue;

                if (!TryReadMemberValue(pointOfInterest, "target", out var targetValue) || targetValue == null)
                    continue;

                if (!TryGetTargetHierarchyPath(targetValue, out var targetPath))
                    continue;

                if (!string.Equals(targetPath, "GameManager/Player", StringComparison.OrdinalIgnoreCase) &&
                    !targetPath.EndsWith("/Player", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryResolveTargetWorldPosition(targetValue, out playerWorldPosition))
                    continue;

                playerRect = childRect;
                playerAnchoredPosition = childRect.anchoredPosition;
                return true;
            }

            return false;
        }

        private Camera ResolveMapProjectionCamera()
        {
            var mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.gameObject != null && mainCamera.gameObject.activeInHierarchy)
                return mainCamera;

            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || camera.gameObject == null)
                    continue;

                if (!camera.gameObject.activeInHierarchy || !camera.enabled)
                    continue;

                return camera;
            }

            return null;
        }

        private static Camera ResolveCanvasCamera(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return null;

            var canvas = rectTransform.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        private bool TryProjectWorldPositionToPoiLocal(Vector3 worldPosition, Camera projectionCamera, Camera uiCamera, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;

            if (projectionCamera == null || _poiRoot == null)
                return false;

            var screenPosition = projectionCamera.WorldToScreenPoint(worldPosition);
            if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) || float.IsNaN(screenPosition.z))
                return false;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _poiRoot,
                new Vector2(screenPosition.x, screenPosition.y),
                uiCamera,
                out localPosition);
        }

        private RectTransform ResolveCalibrationPoiTemplate()
        {
            if (_poiRoot == null)
                return null;

            var namedTemplate = _poiRoot.Find("Template") as RectTransform;
            if (namedTemplate != null && namedTemplate.GetComponent("PointOfInterest") != null)
            {
                StreetQuestShared.LogDebug($"Resolved calibration POI template from named Template: {GetHierarchyPath(namedTemplate)}");
                return namedTemplate;
            }

            RectTransform dynamicTemplate = null;
            foreach (RectTransform childRect in _poiRoot)
            {
                if (childRect == null || childRect == _streetQuestRoot)
                    continue;

                if (IsCalibrationAnchorRect(childRect))
                    continue;

                if (childRect.GetComponent("PointOfInterest") == null)
                    continue;

                if (IsDynamicPoiTemplate(childRect))
                {
                    dynamicTemplate ??= childRect;
                    continue;
                }

                StreetQuestShared.LogDebug($"Resolved calibration POI template from non-dynamic POI: {GetHierarchyPath(childRect)}");
                return childRect;
            }

            if (dynamicTemplate != null)
                StreetQuestShared.LogDebug($"Resolved calibration POI template from dynamic fallback: {GetHierarchyPath(dynamicTemplate)}");

            return dynamicTemplate;
        }

        private bool IsCalibrationAnchorRect(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return false;

            for (var i = 0; i < 3; i++)
            {
                if (_calibrationAnchorRects[i] == rectTransform)
                    return true;
            }

            return rectTransform.name.StartsWith("StreetQuestCalibrationPoi.", StringComparison.Ordinal);
        }

        private bool TryBuildCalibrationFromAnchors(RectTransform poiRoot, out CalibrationData calibration)
        {
            calibration = default;

            if (!EnsureCalibrationAnchors(poiRoot))
                return false;

            var uiPositions = new Vector2[3];
            for (var i = 0; i < 3; i++)
            {
                var poiComponent = _calibrationAnchorPoiComponents[i];
                var rectTransform = _calibrationAnchorRects[i];
                var targetTransform = _calibrationAnchorTargets[i];

                if (poiComponent == null || rectTransform == null || targetTransform == null)
                    return false;

                targetTransform.position = _calibrationAnchorWorldPositions[i];
                TryInvokeMethod(poiComponent, "UpdatePosition");

                var anchoredPosition = rectTransform.anchoredPosition;
                if (float.IsNaN(anchoredPosition.x) || float.IsNaN(anchoredPosition.y))
                    return false;

                uiPositions[i] = anchoredPosition;
            }

            var worldA = _calibrationAnchorWorldPositions[0];
            var worldB = _calibrationAnchorWorldPositions[1];
            var worldC = _calibrationAnchorWorldPositions[2];

            var uiA = uiPositions[0];
            var uiB = uiPositions[1];
            var uiC = uiPositions[2];

            if (Vector2.Distance(uiA, uiB) < 0.01f || Vector2.Distance(uiA, uiC) < 0.01f)
            {
                if (_elapsedSeconds >= _nextCalibrationLogAtSeconds)
                {
                    _nextCalibrationLogAtSeconds = _elapsedSeconds + 2f;
                    StreetQuestShared.LogDebug(
                        "Map marker calibration anchors collapsed to the same UI position; " +
                        $"A={FormatVector2(uiA)} B={FormatVector2(uiB)} C={FormatVector2(uiC)}. " +
                        "This usually means the cloned POI target was not replaced correctly.");
                }

                return false;
            }

            var worldXSpan = worldB.x - worldA.x;
            var worldZSpan = worldC.z - worldA.z;
            if (Mathf.Abs(worldXSpan) < 0.01f || Mathf.Abs(worldZSpan) < 0.01f)
                return false;

            calibration = new CalibrationData
            {
                worldXToUiX = (uiB.x - uiA.x) / worldXSpan,
                worldZToUiX = (uiC.x - uiA.x) / worldZSpan,
                uiXOffset = uiA.x - (((uiB.x - uiA.x) / worldXSpan) * worldA.x) - (((uiC.x - uiA.x) / worldZSpan) * worldA.z),
                worldXToUiY = (uiB.y - uiA.y) / worldXSpan,
                worldZToUiY = (uiC.y - uiA.y) / worldZSpan,
                uiYOffset = uiA.y - (((uiB.y - uiA.y) / worldXSpan) * worldA.x) - (((uiC.y - uiA.y) / worldZSpan) * worldA.z)
            };

            if (_elapsedSeconds >= _nextCalibrationLogAtSeconds)
            {
                _nextCalibrationLogAtSeconds = _elapsedSeconds + 2f;
                StreetQuestShared.LogDebug(
                    "Map marker calibration anchors " +
                    $"A world={FormatVector3(worldA)} ui={FormatVector2(uiA)} " +
                    $"B world={FormatVector3(worldB)} ui={FormatVector2(uiB)} " +
                    $"C world={FormatVector3(worldC)} ui={FormatVector2(uiC)}");
            }

            return true;
        }

        private bool EnsureCalibrationAnchors(RectTransform poiRoot)
        {
            if (poiRoot == null)
                return false;

            if (HaveValidCalibrationAnchors(poiRoot))
                return true;

            var template = ResolveCalibrationPoiTemplate();
            if (template == null)
            {
                MaybeLogVerbose("Calibration anchors could not be created because no POI template was found.");
                return false;
            }

            var pointOfInterestTemplate = template.GetComponent("PointOfInterest");
            if (pointOfInterestTemplate == null)
            {
                MaybeLogVerbose($"Calibration anchor template has no PointOfInterest component: {GetHierarchyPath(template)}");
                return false;
            }

            InitializeCalibrationAnchorWorldPositions();
            var targetParent = ResolveNativePoiTargetParent();

            for (var i = 0; i < 3; i++)
            {
                var anchorName = CalibrationAnchorNames[i];

                if (_calibrationAnchorTargets[i] == null)
                {
                    var targetObject = new GameObject($"StreetQuestCalibrationTarget.{anchorName}");
                    _calibrationAnchorTargets[i] = targetObject.transform;
                    if (targetParent != null)
                        _calibrationAnchorTargets[i].SetParent(targetParent, worldPositionStays: true);

                    StreetQuestShared.LogDebug(
                        $"Map marker calibration target created name={anchorName} parent={(targetParent != null ? GetHierarchyPath(targetParent) : "<none>")} world={FormatVector3(_calibrationAnchorWorldPositions[i])}");
                }

                _calibrationAnchorTargets[i].position = _calibrationAnchorWorldPositions[i];

                if (_calibrationAnchorRects[i] != null &&
                    _calibrationAnchorRects[i].gameObject != null &&
                    _calibrationAnchorRects[i].parent == poiRoot &&
                    _calibrationAnchorPoiComponents[i] != null)
                {
                    continue;
                }

                var poiObject = Instantiate(template.gameObject, poiRoot, false);
                poiObject.name = $"StreetQuestCalibrationPoi.{anchorName}";
                poiObject.SetActive(true);

                var poiComponent = poiObject.GetComponent(pointOfInterestTemplate.GetType().Name) as Component;
                if (poiComponent == null)
                {
                    Destroy(poiObject);
                    MaybeLogVerbose($"Calibration anchor POI component missing after clone name={anchorName}");
                    return false;
                }

                var rectTransform = poiObject.GetComponent<RectTransform>();
                if (rectTransform == null)
                {
                    Destroy(poiObject);
                    MaybeLogVerbose($"Calibration anchor RectTransform missing after clone name={anchorName}");
                    return false;
                }

                ConfigureCalibrationAnchorPoi(poiComponent, _calibrationAnchorTargets[i], anchorName);
                poiObject.name = $"StreetQuestCalibrationPoi.{anchorName}";
                HideCalibrationAnchorVisuals(poiObject);
                _calibrationAnchorRects[i] = rectTransform;
                _calibrationAnchorPoiComponents[i] = poiComponent;

                StreetQuestShared.LogDebug(
                    $"Map marker calibration POI created name={anchorName} template={template.name} targetWorld={FormatVector3(_calibrationAnchorWorldPositions[i])} rectPath={GetHierarchyPath(rectTransform)}");
            }

            return HaveValidCalibrationAnchors(poiRoot);
        }

        private bool HaveValidCalibrationAnchors(RectTransform poiRoot)
        {
            if (poiRoot == null)
                return false;

            for (var i = 0; i < 3; i++)
            {
                if (_calibrationAnchorRects[i] == null ||
                    _calibrationAnchorRects[i].gameObject == null ||
                    _calibrationAnchorRects[i].parent != poiRoot ||
                    _calibrationAnchorPoiComponents[i] == null ||
                    _calibrationAnchorTargets[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void InitializeCalibrationAnchorWorldPositions()
        {
            if (_calibrationAnchorWorldPositionsInitialized)
                return;

            var basePosition = Vector3.zero;
            var knownCharacterIds = StreetQuestShared.GetKnownCharacterIds()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var characterId in knownCharacterIds)
            {
                if (StreetQuestShared.TryGetCharacterWorldPosition(characterId, out basePosition))
                    break;
            }

            _calibrationAnchorWorldPositions[0] = basePosition + new Vector3(-CalibrationAnchorSpan, 0f, -CalibrationAnchorSpan);
            _calibrationAnchorWorldPositions[1] = basePosition + new Vector3(CalibrationAnchorSpan, 0f, -CalibrationAnchorSpan);
            _calibrationAnchorWorldPositions[2] = basePosition + new Vector3(-CalibrationAnchorSpan, 0f, CalibrationAnchorSpan);
            _calibrationAnchorWorldPositionsInitialized = true;

            StreetQuestShared.LogDebug(
                $"Map marker calibration anchor world positions initialized base={FormatVector3(basePosition)} " +
                $"A={FormatVector3(_calibrationAnchorWorldPositions[0])} " +
                $"B={FormatVector3(_calibrationAnchorWorldPositions[1])} " +
                $"C={FormatVector3(_calibrationAnchorWorldPositions[2])}");
        }

        private void ConfigureCalibrationAnchorPoi(Component poiComponent, Transform targetTransform, string anchorName)
        {
            if (poiComponent == null || targetTransform == null)
                return;

            var preInitializeTargetSet = SetNamedFieldValue(poiComponent, "target", targetTransform);
            SetNamedFieldValue(poiComponent, "offset", Vector3.zero);
            SetNamedFieldValue(poiComponent, "hidden", false);
            SetNamedFieldValue(poiComponent, "isGuider", false);
            SetNamedFieldValue(poiComponent, "_isQuestGuider", false);
            SetNamedFieldValue(poiComponent, "_text", string.Empty);
            SetNamedFieldValue(poiComponent, "<targetAddress>k__BackingField", null);
            SetNamedFieldValue(poiComponent, "<Permanent>k__BackingField", true);

            TryInvokeMethod(poiComponent, "Initialize");

            var targetSet = SetNamedFieldValue(poiComponent, "target", targetTransform);
            var offsetSet = SetNamedFieldValue(poiComponent, "offset", Vector3.zero);
            var hiddenSet = SetNamedFieldValue(poiComponent, "hidden", false);
            SetNamedFieldValue(poiComponent, "isGuider", false);
            SetNamedFieldValue(poiComponent, "_isQuestGuider", false);
            SetNamedFieldValue(poiComponent, "_text", string.Empty);
            SetNamedFieldValue(poiComponent, "<targetAddress>k__BackingField", null);
            var permanentSet = SetNamedFieldValue(poiComponent, "<Permanent>k__BackingField", true);
            var enabledSet = SetNamedFieldValue(poiComponent, "enabled", true);
            TryInvokeMethod(poiComponent, "UpdatePosition");

            if (poiComponent.gameObject != null)
                poiComponent.gameObject.name = $"StreetQuestCalibrationPoi.{anchorName}";

            TryReadMemberValue(poiComponent, "target", out var targetReadback);
            StreetQuestShared.LogDebug(
                $"Map marker calibration POI configured name={anchorName} requestedTarget={FormatMemberValue(targetTransform)} " +
                $"preInitializeTargetSet={preInitializeTargetSet} targetSet={targetSet} targetReadback={FormatMemberValue(targetReadback)} " +
                $"offsetSet={offsetSet} hiddenSet={hiddenSet} permanentSet={permanentSet} enabledSet={enabledSet}");
        }

        private static void HideCalibrationAnchorVisuals(GameObject rootObject)
        {
            if (rootObject == null)
                return;

            foreach (var canvasGroup in rootObject.GetComponentsInChildren<CanvasGroup>(includeInactive: true))
            {
                if (canvasGroup == null)
                    continue;

                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            foreach (var image in rootObject.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (image == null)
                    continue;

                var color = image.color;
                color.a = 0f;
                image.color = color;
                image.raycastTarget = false;
                image.enabled = true;
                image.material = null;
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

                if (IsCalibrationAnchorRect(childRect))
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
                MaybeLogVerbose("Map marker calibration fallback aborted: fewer than 2 usable vanilla POI samples.");
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
                MaybeLogVerbose("Map marker calibration fallback aborted: world POI span too small.");
                return false;
            }

            var scaleX = (maxUiX - minUiX) / (maxWorldX - minWorldX);
            var scaleY = (maxUiY - minUiY) / (maxWorldZ - minWorldZ);
            calibration = new CalibrationData
            {
                worldXToUiX = scaleX,
                worldZToUiX = 0f,
                uiXOffset = minUiX - (scaleX * minWorldX),
                worldXToUiY = 0f,
                worldZToUiY = scaleY,
                uiYOffset = minUiY - (scaleY * minWorldZ)
            };

            if (_elapsedSeconds >= _nextCalibrationLogAtSeconds)
            {
                _nextCalibrationLogAtSeconds = _elapsedSeconds + 2f;
                StreetQuestShared.LogDebug(
                    $"Map marker calibration fallback succeeded samples={samples.Count} scaleX={scaleX:F4} scaleY={scaleY:F4}");
            }
            return true;
        }

        private void AttachNativePoiVisuals(RectTransform template, Transform poiRootTransform)
        {
            if (template == null || poiRootTransform == null)
                return;

            foreach (var image in poiRootTransform.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (image == null)
                    continue;

                image.enabled = false;
                image.raycastTarget = false;
            }

            var pointer = FindChildRectTransform(template, "POIPointer");
            var blob = FindChildRectTransform(template, "POIBlob");
            if (pointer != null)
            {
                var pointerCloneObject = Instantiate(pointer.gameObject, poiRootTransform, false);
                pointerCloneObject.name = "StreetQuestNativePointer";
                pointerCloneObject.SetActive(true);
                PrepareMarkerVisual(pointerCloneObject);
            }

            if (blob != null)
            {
                var blobCloneObject = Instantiate(blob.gameObject, poiRootTransform, false);
                blobCloneObject.name = "StreetQuestNativeBlob";
                blobCloneObject.SetActive(true);
                PrepareMarkerVisual(blobCloneObject);
                ApplyCustomMarkerIcon(blobCloneObject.transform);
            }
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

            if (_useProjectionCalibration)
            {
                var projectionCamera = _projectionCamera != null ? _projectionCamera : ResolveMapProjectionCamera();
                var uiCamera = _projectionUiCamera != null ? _projectionUiCamera : ResolveCanvasCamera(_poiRoot);
                if (projectionCamera == null ||
                    !TryProjectWorldPositionToPoiLocal(worldPosition, projectionCamera, uiCamera, out var projectedPosition))
                {
                    return false;
                }

                anchoredPosition = projectedPosition + _projectionOffset;
                return true;
            }

            anchoredPosition = new Vector2(
                (_calibration.worldXToUiX * worldPosition.x) + (_calibration.worldZToUiX * worldPosition.z) + _calibration.uiXOffset,
                (_calibration.worldXToUiY * worldPosition.x) + (_calibration.worldZToUiY * worldPosition.z) + _calibration.uiYOffset);
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

        private static bool IsDynamicPoiTarget(object pointOfInterest)
        {
            if (pointOfInterest == null)
                return false;

            if (!TryReadMemberValue(pointOfInterest, "target", out var targetValue) || targetValue == null)
                return false;

            if (!TryGetTargetHierarchyPath(targetValue, out var targetPath))
                return false;

            return targetPath.StartsWith("GameManager/ItemsContainer/", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(targetPath, "GameManager/ItemsContainer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(targetPath, "GameManager/Player", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetTargetHierarchyPath(object candidate, out string path)
        {
            path = null;
            switch (candidate)
            {
                case Transform transform:
                    path = GetHierarchyPath(transform);
                    return !string.IsNullOrWhiteSpace(path);
                case Component component:
                    path = GetHierarchyPath(component.transform);
                    return !string.IsNullOrWhiteSpace(path);
                case GameObject gameObject:
                    path = GetHierarchyPath(gameObject.transform);
                    return !string.IsNullOrWhiteSpace(path);
                default:
                    return false;
            }
        }

        private static bool SetNamedFieldValue(object instance, string fieldName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
                return false;

            var type = instance.GetType();
            var field = type.GetField(fieldName, ReflectionFlags);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, value);
                    return true;
                }
                catch
                {
                }
            }

            var property = type.GetProperty(fieldName, ReflectionFlags);
            if (property == null || !property.CanWrite || property.GetIndexParameters().Length > 0)
                return false;

            try
            {
                property.SetValue(instance, value, null);
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

            _cityMapIsOpenProperty ??= _cityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return _cityMapIsOpenProperty?.GetValue(null) as bool? ?? false;
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

            if (_nativePoiComponents.TryGetValue(characterId, out var poiComponent) &&
                poiComponent != null &&
                poiComponent.gameObject != null)
            {
                poiComponent.gameObject.SetActive(active);
            }
        }

        private void HideStreetQuestRoot()
        {
            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null)
                _streetQuestRoot.gameObject.SetActive(false);

            foreach (var poiComponent in _nativePoiComponents.Values)
            {
                if (poiComponent != null && poiComponent.gameObject != null)
                    poiComponent.gameObject.SetActive(false);
            }
        }

        private void DestroyCustomMarkerRoot(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (!_markerRoots.TryGetValue(characterId, out var markerRoot) || markerRoot == null)
                return;

            Destroy(markerRoot.gameObject);
            _markerRoots.Remove(characterId);
            DebugLog($"Destroyed custom marker root characterId={characterId}");
        }

        private void DestroyMarkerImages()
        {
            foreach (var markerRoot in _markerRoots.Values)
            {
                if (markerRoot != null)
                    Destroy(markerRoot.gameObject);
            }

            _markerRoots.Clear();
            _markerAnchoredPositions.Clear();
            _markerAnchoredVelocities.Clear();
            _nativePoiComponents.Clear();
            _nativePoiTargetAnchors.Clear();
            _nativePoiLastTargetPositions.Clear();

            for (var i = 0; i < 3; i++)
            {
                if (_calibrationAnchorRects[i] != null)
                    Destroy(_calibrationAnchorRects[i].gameObject);
                if (_calibrationAnchorTargets[i] != null)
                    Destroy(_calibrationAnchorTargets[i].gameObject);

                _calibrationAnchorRects[i] = null;
                _calibrationAnchorPoiComponents[i] = null;
                _calibrationAnchorTargets[i] = null;
                _calibrationAnchorWorldPositions[i] = default;
            }

            _calibrationAnchorWorldPositionsInitialized = false;
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
                    !name.StartsWith("StreetQuestCalibrationPoi.", StringComparison.Ordinal) &&
                    !name.StartsWith("StreetQuestCalibrationTarget.", StringComparison.Ordinal) &&
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
            DebugLog($"MapMarkerWatcher: {state}");
        }

        private void LogKnownCharacters(IReadOnlyCollection<string> knownCharacterIds)
        {
            var snapshot = knownCharacterIds == null || knownCharacterIds.Count == 0
                ? "<none>"
                : string.Join(", ", knownCharacterIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            if (string.Equals(_lastKnownCharacterSnapshot, snapshot, StringComparison.Ordinal))
                return;

            _lastKnownCharacterSnapshot = snapshot;
            DebugLog($"Map marker known NPCs: {snapshot}");
        }

        private void LogMarkerState(string characterId, bool isVisible, string reason)
        {
            _markerVisibilityStates.TryGetValue(characterId, out var previousVisibility);
            _markerStatusReasons.TryGetValue(characterId, out var previousReason);

            if (previousVisibility == isVisible && string.Equals(previousReason, reason, StringComparison.Ordinal))
                return;

            _markerVisibilityStates[characterId] = isVisible;
            _markerStatusReasons[characterId] = reason;
            DebugLog($"Map marker characterId={characterId} visible={isVisible} reason={reason}");
        }

        private void MaybeLogVerbose(string message)
        {
            if (!EnableMarkerDebugLogging)
                return;

            if (_elapsedSeconds < _nextVerboseLogAtSeconds)
                return;

            _nextVerboseLogAtSeconds = _elapsedSeconds + 2f;
            StreetQuestShared.LogDebug($"MapMarkerWatcher: {message}");
        }

        private static void DebugLog(string message)
        {
            if (!EnableMarkerDebugLogging || string.IsNullOrWhiteSpace(message))
                return;

            StreetQuestShared.LogDebug(message);
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
            public float worldXToUiX;
            public float worldZToUiX;
            public float uiXOffset;
            public float worldXToUiY;
            public float worldZToUiY;
            public float uiYOffset;
        }
    }
}
