using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BigAmbitions.SaveSystem.Legacy;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    [DefaultExecutionOrder(-9990)]
    internal sealed class StreetQuestMapMarkerWatcher : MonoBehaviour
    {
        private static readonly bool EnableMarkerDebugLogging = false;
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
        private RectTransform _tooltipRoot;
        private Text _tooltipText;
        private Sprite _tooltipBackgroundSprite;
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
        private string _hoveredCharacterId;
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
            _tooltipRoot = null;
            _tooltipText = null;
            _tooltipBackgroundSprite = null;
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
            _hoveredCharacterId = null;
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
            UpdateTooltipPosition();
        }

        private void EnsureCalibration(RectTransform poiRoot)
        {
            _poiRoot = poiRoot;
            _hasCalibration = TryBuildCalibration(poiRoot, out _calibration);
            if (_hasCalibration)
            {
                _loggedCalibrationFailure = false;
                var calibrationSnapshot =
                    $"{_calibration.scaleX:F4}|{_calibration.offsetX:F2}|{_calibration.scaleY:F4}|{_calibration.offsetY:F2}";
                if (!string.Equals(_lastCalibrationSnapshot, calibrationSnapshot, StringComparison.Ordinal))
                {
                    _lastCalibrationSnapshot = calibrationSnapshot;
                    DebugLog(
                        $"Map marker calibration scaleX={_calibration.scaleX:F4} offsetX={_calibration.offsetX:F2} scaleY={_calibration.scaleY:F4} offsetY={_calibration.offsetY:F2}");
                }
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
                _markerAnchoredPositions.Remove(existing);
                _markerAnchoredVelocities.Remove(existing);
                _markerVisibilityStates.Remove(existing);
                _markerStatusReasons.Remove(existing);
                if (string.Equals(_hoveredCharacterId, existing, StringComparison.OrdinalIgnoreCase))
                    HideTooltip();
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
                if (!_markerAnchoredPositions.TryGetValue(characterId, out var currentAnchoredPosition))
                {
                    currentAnchoredPosition = targetAnchoredPosition;
                }

                if (!_markerAnchoredVelocities.TryGetValue(characterId, out var currentVelocity))
                {
                    currentVelocity = Vector2.zero;
                }

                var distance = Vector2.Distance(currentAnchoredPosition, targetAnchoredPosition);
                if (distance <= MarkerDeadzoneDistance)
                {
                    markerRoot.anchoredPosition = currentAnchoredPosition;
                }
                else if (distance >= MarkerSnapDistance)
                {
                    currentAnchoredPosition = targetAnchoredPosition;
                    markerRoot.anchoredPosition = currentAnchoredPosition;
                }
                else
                {
                    currentAnchoredPosition = Vector2.SmoothDamp(
                        currentAnchoredPosition,
                        targetAnchoredPosition,
                        ref currentVelocity,
                        MarkerSmoothTime,
                        Mathf.Infinity,
                        Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
                    markerRoot.anchoredPosition = currentAnchoredPosition;
                }

                _markerAnchoredPositions[characterId] = currentAnchoredPosition;
                _markerAnchoredVelocities[characterId] = currentVelocity;
                markerRoot.gameObject.SetActive(true);
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
            EnsureMarkerHoverTarget(rectTransform, characterId);

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
            iconImage.SetNativeSize();
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
                DebugLog($"Resolved map marker template: {GetHierarchyPath(childRect)}");
                return _poiMarkerTemplate;
            }

            if (fallbackTemplate != null)
            {
                _poiMarkerTemplate = fallbackTemplate;
                DebugLog($"Resolved fallback map marker template: {GetHierarchyPath(fallbackTemplate)}");
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
            EnsureTooltipRoot();
            DebugLog($"Created StreetQuest POI root under {GetHierarchyPath(poiRoot)} size={FormatVector2(_streetQuestRoot.sizeDelta)}");
        }

        private void EnsureTooltipRoot()
        {
            if (_streetQuestRoot == null)
                return;

            if (_tooltipRoot != null && _tooltipRoot.gameObject != null && _tooltipRoot.parent == _streetQuestRoot)
            {
                _tooltipRoot.SetAsLastSibling();
                return;
            }

            if (_tooltipRoot != null && _tooltipRoot.gameObject != null)
                Destroy(_tooltipRoot.gameObject);

            var tooltipObject = new GameObject("StreetQuestMarkerTooltip", typeof(RectTransform), typeof(Image));
            _tooltipRoot = tooltipObject.GetComponent<RectTransform>();
            _tooltipRoot.SetParent(_streetQuestRoot, false);
            _tooltipRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _tooltipRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltipRoot.pivot = new Vector2(0.5f, 0f);
            _tooltipRoot.sizeDelta = new Vector2(180f, 44f);
            _tooltipRoot.anchoredPosition = Vector2.zero;
            _tooltipRoot.SetAsLastSibling();

            var background = tooltipObject.GetComponent<Image>();
            background.sprite = GetTooltipBackgroundSprite();
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(_tooltipRoot, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            _tooltipText = textObject.GetComponent<Text>();
            _tooltipText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _tooltipText.fontSize = 20;
            _tooltipText.alignment = TextAnchor.MiddleCenter;
            _tooltipText.color = Color.white;
            _tooltipText.raycastTarget = false;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipText.verticalOverflow = VerticalWrapMode.Truncate;

            _tooltipRoot.gameObject.SetActive(false);
        }

        private void EnsureMarkerHoverTarget(RectTransform markerRoot, string characterId)
        {
            if (markerRoot == null)
                return;

            var hitImage = markerRoot.GetComponent<Image>();
            if (hitImage == null)
                hitImage = markerRoot.gameObject.AddComponent<Image>();

            hitImage.color = new Color(1f, 1f, 1f, 0.01f);
            hitImage.sprite ??= Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            hitImage.type = Image.Type.Sliced;
            hitImage.raycastTarget = true;

            var hoverTarget = markerRoot.GetComponent<StreetQuestMapMarkerHoverTarget>();
            if (hoverTarget == null)
                hoverTarget = markerRoot.gameObject.AddComponent<StreetQuestMapMarkerHoverTarget>();

            hoverTarget.Owner = this;
            hoverTarget.CharacterId = characterId;
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

        private Sprite GetTooltipBackgroundSprite()
        {
            if (_tooltipBackgroundSprite != null)
                return _tooltipBackgroundSprite;

            const int width = 32;
            const int height = 32;
            const int radius = 6;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var fill = new Color(0f, 0f, 0f, 1f);
            var clear = new Color(0f, 0f, 0f, 0f);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = 0f;
                    var dy = 0f;

                    if (x < radius)
                        dx = radius - x;
                    else if (x >= width - radius)
                        dx = x - (width - radius - 1);

                    if (y < radius)
                        dy = radius - y;
                    else if (y >= height - radius)
                        dy = y - (height - radius - 1);

                    if (dx <= 0f || dy <= 0f)
                    {
                        texture.SetPixel(x, y, fill);
                        continue;
                    }

                    texture.SetPixel(x, y, (dx * dx) + (dy * dy) <= (radius * radius) ? fill : clear);
                }
            }

            texture.Apply(false, false);
            _tooltipBackgroundSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            return _tooltipBackgroundSprite;
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

            DebugLog(
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
        }

        private void HideStreetQuestRoot()
        {
            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null)
                _streetQuestRoot.gameObject.SetActive(false);

            HideTooltip();
        }

        private void DestroyCustomMarkerRoot(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            if (!_markerRoots.TryGetValue(characterId, out var markerRoot) || markerRoot == null)
                return;

            Destroy(markerRoot.gameObject);
            _markerRoots.Remove(characterId);
            if (string.Equals(_hoveredCharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                HideTooltip();
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
            _markerVisibilityStates.Clear();
            _markerStatusReasons.Clear();
            _hoveredCharacterId = null;
            if (_tooltipRoot != null && _tooltipRoot.gameObject != null)
                Destroy(_tooltipRoot.gameObject);
            _tooltipRoot = null;
            _tooltipText = null;
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

        internal void HandleMarkerPointerEnter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            _hoveredCharacterId = characterId;
            ShowTooltip(characterId);
            DebugLog($"Map marker hover enter characterId={characterId}");
        }

        internal void HandleMarkerPointerExit(string characterId)
        {
            if (!string.Equals(_hoveredCharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                return;

            HideTooltip();
            DebugLog($"Map marker hover exit characterId={characterId}");
        }

        internal void HandleMarkerPointerClick(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return;

            StreetQuestShared.LogDebug($"Map marker click characterId={characterId}");
        }

        private void ShowTooltip(string characterId)
        {
            EnsureTooltipRoot();
            if (_tooltipRoot == null || _tooltipText == null)
                return;

            _tooltipText.text = StreetQuestShared.ResolveCharacterDisplayName(characterId);
            _tooltipRoot.gameObject.SetActive(true);
            _tooltipRoot.SetAsLastSibling();
            UpdateTooltipPosition();
        }

        private void HideTooltip()
        {
            _hoveredCharacterId = null;
            if (_tooltipRoot != null && _tooltipRoot.gameObject != null)
                _tooltipRoot.gameObject.SetActive(false);
        }

        private void UpdateTooltipPosition()
        {
            if (string.IsNullOrWhiteSpace(_hoveredCharacterId) ||
                _tooltipRoot == null ||
                !_tooltipRoot.gameObject.activeSelf ||
                !_markerRoots.TryGetValue(_hoveredCharacterId, out var markerRoot) ||
                markerRoot == null)
            {
                return;
            }

            _tooltipRoot.anchoredPosition = markerRoot.anchoredPosition + new Vector2(0f, 40f);
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
