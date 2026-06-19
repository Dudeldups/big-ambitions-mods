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
        private const string MapFilterPrefsKey = "streetquest.map_filter_npcs";
        private const string MapFilterLabel = "PEOPLE";
        private const float NameplateVerticalOffset = 68f;
        private const int NameplateFontSize = 24;
        private const float NameplateHeight = 40f;
        private const int NameplateBackgroundWidth = 64;
        private const int NameplateBackgroundHeight = 32;
        private const int NameplateCornerRadiusPixels = 5;
        private const int MapFilterRequiredStableFrames = 2;
        private static readonly Color MarkerBlobColor = new Color32(244, 188, 116, 255);
        private static readonly Color MarkerPointerColor = new Color32(250, 250, 250, 255);
        private static readonly Color MarkerOutlineColor = new Color32(252, 252, 252, 255);
        private const float ProjectionOffsetSanityLimit = 256f;

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
        private bool _mapFilterVisible = true;
        private int _mapFilterStableFrames;
        private string _mapFilterReadinessSignature;
        private float _nextMapFilterReadinessLogAtSeconds;
        private Toggle _mapFilterToggle;
        private GameObject _mapFilterRowObject;
        private bool _loggedMapFilterFailure;
        private RectTransform _nameplateRoot;
        private Text _nameplateText;
        private Sprite _nameplateBackgroundSprite;
        private RectTransform _hoveredMarkerRoot;
        private string _hoveredCharacterId;

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
            _mapFilterVisible = UnityEngine.PlayerPrefs.GetInt(MapFilterPrefsKey, 1) != 0;
            _mapFilterStableFrames = 0;
            _mapFilterReadinessSignature = null;
            _nextMapFilterReadinessLogAtSeconds = 0f;
            _mapFilterToggle = null;
            _mapFilterRowObject = null;
            _loggedMapFilterFailure = false;
            _nameplateRoot = null;
            _nameplateText = null;
            _hoveredMarkerRoot = null;
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
                _mapFilterStableFrames = 0;
                _mapFilterReadinessSignature = null;
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
            EnsureMapFilterToggle();
            UpdateNameplatePosition();
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

            DebugLog(
                "Map marker calibration failed: could not resolve player POI + map camera projection. " +
                "NPC map markers are hidden instead of falling back to moving vanilla/player marker samples.");
            _loggedCalibrationFailure = true;
        }

        private void UpdateKnownNpcMarkers()
        {
            if (_streetQuestRoot == null)
                return;

            _streetQuestRoot.gameObject.SetActive(_mapFilterVisible);
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
                if (!TryGetCharacterMapWorldPosition(characterId, out var worldPosition))
                {
                    LogMarkerState(characterId, false, "No map world position resolved for character.");
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
                ApplyFixedStreetQuestMarkerVisualColors(markerRoot);
                _markerAnchoredPositions[characterId] = targetAnchoredPosition;
                _markerAnchoredVelocities[characterId] = Vector2.zero;
                markerRoot.gameObject.SetActive(true);
                LogMarkerState(
                    characterId,
                    true,
                    "Placed marker with player-projection calibration.");
            }
        }

        private static bool TryGetCharacterMapWorldPosition(string characterId, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var definition = StreetQuestCharacterCatalog.Get(characterId);
            if (definition == null)
                return false;

            var runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (runtimeDefinition != null)
            {
                if (!runtimeDefinition.enabled)
                    return false;

                worldPosition = runtimeDefinition.PositionOr(Vector3.zero);
                return true;
            }

            if (!definition.enabled)
                return false;

            worldPosition = definition.PositionOr(Vector3.zero);
            return true;
        }

        private void EnsureMarkerHoverTarget(RectTransform markerRoot, string characterId)
        {
            if (markerRoot == null || string.IsNullOrWhiteSpace(characterId))
                return;

            var existing = markerRoot.Find("StreetQuestMarkerHoverHitTarget");
            if (existing != null)
            {
                var existingTarget = existing.GetComponent<StreetQuestMapMarkerHoverTarget>();
                if (existingTarget != null)
                    existingTarget.Configure(this, characterId, markerRoot);
                return;
            }

            var hitTargetObject = new GameObject("StreetQuestMarkerHoverHitTarget", typeof(RectTransform), typeof(Image), typeof(StreetQuestMapMarkerHoverTarget));
            var hitRect = hitTargetObject.GetComponent<RectTransform>();
            hitRect.SetParent(markerRoot, false);
            hitRect.anchorMin = new Vector2(0.5f, 0.5f);
            hitRect.anchorMax = new Vector2(0.5f, 0.5f);
            hitRect.pivot = new Vector2(0.5f, 0.5f);
            hitRect.sizeDelta = new Vector2(48f, 48f);
            hitRect.anchoredPosition = Vector2.zero;
            hitRect.SetAsLastSibling();

            var image = hitTargetObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.01f);
            image.raycastTarget = true;

            hitTargetObject.GetComponent<StreetQuestMapMarkerHoverTarget>().Configure(this, characterId, markerRoot);
            DebugLog($"Map marker hover target created characterId={characterId}");
        }

        internal void ShowMarkerNameplate(string characterId, RectTransform markerRoot)
        {
            if (string.IsNullOrWhiteSpace(characterId) || markerRoot == null || _streetQuestRoot == null)
                return;

            EnsureNameplate();
            if (_nameplateRoot == null || _nameplateText == null)
                return;

            var changed = !string.Equals(_hoveredCharacterId, characterId, StringComparison.OrdinalIgnoreCase);
            _hoveredCharacterId = characterId;
            _hoveredMarkerRoot = markerRoot;
            _nameplateText.text = StreetQuestShared.ResolveCharacterDisplayName(characterId);
            _nameplateRoot.gameObject.SetActive(_mapFilterVisible);
            UpdateNameplatePosition();

            if (changed)
                DebugLog($"Map marker nameplate shown characterId={characterId} text={_nameplateText.text}");
        }

        internal void HideMarkerNameplate(string characterId, RectTransform markerRoot)
        {
            if (!string.IsNullOrWhiteSpace(characterId) &&
                !string.Equals(_hoveredCharacterId, characterId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HideMarkerNameplate();
        }

        private void HideMarkerNameplate()
        {
            _hoveredCharacterId = null;
            _hoveredMarkerRoot = null;

            if (_nameplateRoot != null && _nameplateRoot.gameObject != null)
                _nameplateRoot.gameObject.SetActive(false);
        }

        private void EnsureNameplate()
        {
            if (_nameplateRoot != null && _nameplateRoot.gameObject != null)
                return;

            if (_streetQuestRoot == null)
                return;

            var rootObject = new GameObject("StreetQuestMarkerNameplate", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _nameplateRoot = rootObject.GetComponent<RectTransform>();
            _nameplateRoot.SetParent(_streetQuestRoot, false);
            _nameplateRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _nameplateRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _nameplateRoot.pivot = new Vector2(0.5f, 0f);
            _nameplateRoot.sizeDelta = new Vector2(170f, NameplateHeight);
            _nameplateRoot.anchoredPosition = Vector2.zero;
            _nameplateRoot.SetAsLastSibling();

            var background = rootObject.GetComponent<Image>();
            background.sprite = GetNameplateBackgroundSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color(0.05f, 0.04f, 0.035f, 0.93f);
            background.raycastTarget = false;

            var canvasGroup = rootObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(_nameplateRoot, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);

            _nameplateText = textObject.GetComponent<Text>();
            _nameplateText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _nameplateText.fontSize = NameplateFontSize;
            _nameplateText.alignment = TextAnchor.MiddleCenter;
            _nameplateText.color = Color.white;
            _nameplateText.raycastTarget = false;

            rootObject.SetActive(false);
            DebugLog("Map marker nameplate panel created.");
        }

        private void UpdateNameplatePosition()
        {
            if (_nameplateRoot == null || _nameplateText == null || _hoveredMarkerRoot == null)
                return;

            if (!_mapFilterVisible || !_hoveredMarkerRoot.gameObject.activeInHierarchy)
            {
                HideMarkerNameplate();
                return;
            }

            var width = Mathf.Clamp(_nameplateText.preferredWidth + 36f, 110f, 320f);
            _nameplateRoot.sizeDelta = new Vector2(width, NameplateHeight);
            _nameplateRoot.anchoredPosition = _hoveredMarkerRoot.anchoredPosition + new Vector2(0f, NameplateVerticalOffset);
            _nameplateRoot.SetAsLastSibling();
        }

        private void EnsureMapFilterToggle()
        {
            if (_streetQuestRoot == null)
                return;

            if (_mapFilterToggle != null && _mapFilterToggle.gameObject != null)
                return;

            if (!TryResolveMapFilterRowsForClone(
                    out var rowTemplate,
                    out var cloneTemplate,
                    out var templateToggle,
                    out var readinessSignature,
                    out var notReadyReason))
            {
                _mapFilterStableFrames = 0;
                _mapFilterReadinessSignature = null;
                MaybeLogMapFilterReadiness($"Map filter not ready: {notReadyReason}");
                return;
            }

            if (!string.Equals(_mapFilterReadinessSignature, readinessSignature, StringComparison.Ordinal))
            {
                _mapFilterReadinessSignature = readinessSignature;
                _mapFilterStableFrames = 1;
                MaybeLogMapFilterReadiness($"Map filter readiness observed frame=1 signature={readinessSignature}");
                return;
            }

            _mapFilterStableFrames++;
            if (_mapFilterStableFrames < MapFilterRequiredStableFrames)
            {
                MaybeLogMapFilterReadiness($"Map filter waiting for stable UI frames={_mapFilterStableFrames}/{MapFilterRequiredStableFrames} signature={readinessSignature}");
                return;
            }

            var parent = rowTemplate.parent;
            if (parent == null)
            {
                DebugLog($"Map filter toggle clone failed: row parent missing for rowTemplate={GetHierarchyPath(rowTemplate)}.");
                return;
            }

            var rowObject = Instantiate(cloneTemplate.gameObject, parent, false);
            rowObject.name = "StreetQuestMapFilter.NPCs";
            rowObject.SetActive(true);
            _mapFilterRowObject = rowObject;

            StripNonUiComponents(rowObject);

            _mapFilterToggle = rowObject.GetComponent<Toggle>();
            if (_mapFilterToggle == null)
                _mapFilterToggle = rowObject.GetComponentInChildren<Toggle>(true);

            if (_mapFilterToggle == null)
            {
                Destroy(rowObject);
                _mapFilterRowObject = null;
                DebugLog(
                    $"Map filter toggle clone failed: cloned row has no Toggle component. rowTemplate={GetHierarchyPath(rowTemplate)} cloneTemplate={GetHierarchyPath(cloneTemplate)}");
                return;
            }

            var textCount = ApplyMapFilterLabel(rowObject);
            var iconInfo = ApplyMapFilterIcon(rowObject, _mapFilterToggle);
            _mapFilterToggle.onValueChanged.RemoveAllListeners();
            _mapFilterToggle.isOn = _mapFilterVisible;
            _mapFilterToggle.onValueChanged.AddListener(SetMapFilterVisibleFromUi);

            var siblingIndex = Mathf.Min(rowTemplate.GetSiblingIndex() + 1, parent.childCount - 1);
            rowObject.transform.SetSiblingIndex(siblingIndex);
            SetMapFilterVisible(_mapFilterVisible, persist: false);

            DebugLog(
                $"Map filter toggle created label={MapFilterLabel} persisted={_mapFilterVisible} stableFrames={_mapFilterStableFrames} " +
                $"templateToggle={(templateToggle != null ? GetHierarchyPath(templateToggle.transform) : "<none>")} " +
                $"rowTemplate={GetHierarchyPath(rowTemplate)} cloneTemplate={GetHierarchyPath(cloneTemplate)} parent={GetHierarchyPath(parent)} " +
                $"siblingIndex={rowObject.transform.GetSiblingIndex()} textCount={textCount} icon={iconInfo}");
        }

        private bool TryResolveMapFilterRowsForClone(
            out Transform rowTemplate,
            out Transform cloneTemplate,
            out Toggle templateToggle,
            out string readinessSignature,
            out string notReadyReason)
        {
            rowTemplate = ResolveMapFilterAnchorRow();
            templateToggle = rowTemplate != null ? rowTemplate.GetComponentInChildren<Toggle>(true) : null;

            if (rowTemplate == null)
            {
                templateToggle = ResolveMapFilterToggleTemplate();
                if (templateToggle != null)
                    rowTemplate = ResolveMapFilterRowRoot(templateToggle);
            }

            if (rowTemplate == null)
            {
                cloneTemplate = null;
                readinessSignature = null;
                notReadyReason = "anchor row missing";
                return false;
            }

            if (rowTemplate.parent == null || !rowTemplate.gameObject.activeInHierarchy)
            {
                cloneTemplate = null;
                readinessSignature = null;
                notReadyReason = $"anchor row inactive or has no parent row={GetHierarchyPath(rowTemplate)}";
                return false;
            }

            cloneTemplate = ResolveMapFilterCloneTemplateRow(rowTemplate) ?? rowTemplate;
            if (cloneTemplate == null || cloneTemplate.parent == null || !cloneTemplate.gameObject.activeInHierarchy)
            {
                readinessSignature = null;
                notReadyReason = "clone template missing or inactive";
                return false;
            }

            if (cloneTemplate.GetComponentInChildren<Toggle>(true) == null)
            {
                readinessSignature = null;
                notReadyReason = $"clone template has no Toggle cloneTemplate={GetHierarchyPath(cloneTemplate)}";
                return false;
            }

            var parent = rowTemplate.parent;
            var contentChildCount = parent != null ? parent.childCount : 0;
            readinessSignature =
                $"parent={GetHierarchyPath(parent)}|count={contentChildCount}|anchor={GetHierarchyPath(rowTemplate)}|clone={GetHierarchyPath(cloneTemplate)}";
            notReadyReason = null;
            return true;
        }

        private void MaybeLogMapFilterReadiness(string message)
        {
            if (!EnableMarkerDebugLogging || string.IsNullOrWhiteSpace(message))
                return;

            if (_elapsedSeconds < _nextMapFilterReadinessLogAtSeconds)
                return;

            _nextMapFilterReadinessLogAtSeconds = _elapsedSeconds + 0.5f;
            DebugLog(message);
        }

        private void SetMapFilterVisibleFromUi(bool visible)
        {
            SetMapFilterVisible(visible, persist: true);
        }

        private void SetMapFilterVisible(bool visible, bool persist)
        {
            _mapFilterVisible = visible;

            if (persist)
            {
                UnityEngine.PlayerPrefs.SetInt(MapFilterPrefsKey, visible ? 1 : 0);
                UnityEngine.PlayerPrefs.Save();
            }

            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null)
                _streetQuestRoot.gameObject.SetActive(visible);

            if (!visible)
                HideMarkerNameplate();

            DebugLog($"Map filter StreetQuest NPCs visible={visible} persist={persist}");
        }

        private Transform ResolveMapFilterCloneTemplateRow(Transform anchorRow)
        {
            if (anchorRow == null || anchorRow.parent == null)
                return null;

            var parent = anchorRow.parent;
            foreach (Transform child in parent)
            {
                if (child == null || child == anchorRow || child.gameObject == null || !child.gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(child);
                if (path.IndexOf("StreetQuest", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var name = child.name ?? string.Empty;
                if (name.IndexOf("rented", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("resume", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (child.GetComponentInChildren<Toggle>(true) != null)
                    {
                        DebugLog($"Map filter clone template resolved from rented/status row={path}");
                        return child;
                    }
                }
            }

            foreach (Transform child in parent)
            {
                if (child == null || child == anchorRow || child.gameObject == null || !child.gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(child);
                if (path.IndexOf("StreetQuest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    LooksLikeMapFilterGroupHeader(child) ||
                    LooksLikeHideClosedBusinessesPath(path))
                {
                    continue;
                }

                if (child.GetComponentInChildren<Toggle>(true) != null && HasReadableTextComponent(child))
                {
                    DebugLog($"Map filter clone template resolved from first normal row={path}");
                    return child;
                }
            }

            DebugLog($"Map filter clone template fallback to anchor row={GetHierarchyPath(anchorRow)}");
            return anchorRow;
        }

        private Transform ResolveMapFilterAnchorRow()
        {
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.gameObject == null || !transform.gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(transform);
                if (!IsMapFilterPath(path) || path.IndexOf("StreetQuest", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!LooksLikeHideClosedBusinessesPath(path))
                    continue;

                var row = ResolveMapFilterRowRootFromChild(transform);
                if (row == null)
                    continue;

                DebugLog($"Map filter anchor row resolved from stable hierarchy row={GetHierarchyPath(row)} source={path}");
                return row;
            }

            var structuralRow = ResolveMapFilterAnchorRowFromContentStructure();
            if (structuralRow != null)
                return structuralRow;

            DebugLog("Map filter anchor row not found by stable hierarchy. Falling back to generic Toggle template search.");
            return null;
        }

        private Transform ResolveMapFilterAnchorRowFromContentStructure()
        {
            var contentRoot = ResolveMapFilterContentRoot();
            if (contentRoot == null)
                return null;

            var children = new List<Transform>();
            foreach (Transform child in contentRoot)
            {
                if (child != null && child.gameObject != null && child.gameObject.activeInHierarchy)
                    children.Add(child);
            }

            foreach (var child in children)
            {
                var path = GetHierarchyPath(child);
                if (LooksLikeHideClosedBusinessesPath(path))
                {
                    DebugLog($"Map filter anchor row resolved from content child stable name row={path} content={GetHierarchyPath(contentRoot)}");
                    return child;
                }
            }

            var statusHeader = children.FirstOrDefault(child =>
                child != null &&
                string.Equals(child.name, "bizman_status", StringComparison.OrdinalIgnoreCase));
            if (statusHeader != null)
            {
                var statusIndex = children.IndexOf(statusHeader);
                var statusRows = new List<Transform>();
                for (var i = statusIndex + 1; i < children.Count; i++)
                {
                    var child = children[i];
                    if (child == null)
                        continue;

                    if (LooksLikeMapFilterGroupHeader(child))
                        break;

                    statusRows.Add(child);
                }

                if (statusRows.Count >= 2)
                {
                    var hideClosedLikeRow = statusRows[1];
                    DebugLog(
                        $"Map filter anchor row resolved from bizman_status structure row={GetHierarchyPath(hideClosedLikeRow)} " +
                        $"statusHeader={GetHierarchyPath(statusHeader)} statusRows={string.Join(",", statusRows.Select(row => row.name))}");
                    return hideClosedLikeRow;
                }

                DebugLog(
                    $"Map filter bizman_status structure found but not enough rows. " +
                    $"statusHeader={GetHierarchyPath(statusHeader)} rows={string.Join(",", statusRows.Select(row => row.name))}");
            }

            // Last-resort language-independent fallback: pick the last non-group toggle row before jobs/building/business sections.
            Transform lastEarlyToggleRow = null;
            foreach (var child in children)
            {
                var name = child.name ?? string.Empty;
                if (name.StartsWith("ba:businesstype_", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("job", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    break;
                }

                if (!LooksLikeMapFilterGroupHeader(child) && child.GetComponentInChildren<Toggle>(true) != null)
                    lastEarlyToggleRow = child;
            }

            if (lastEarlyToggleRow != null)
            {
                DebugLog(
                    $"Map filter anchor row resolved from last-resort structure fallback row={GetHierarchyPath(lastEarlyToggleRow)} " +
                    $"content={GetHierarchyPath(contentRoot)} childCount={children.Count}");
                return lastEarlyToggleRow;
            }

            DebugLog(
                $"Map filter content structure fallback failed content={GetHierarchyPath(contentRoot)} " +
                $"children={string.Join(",", children.Take(20).Select(child => child.name))}");
            return null;
        }

        private static bool LooksLikeMapFilterGroupHeader(Transform row)
        {
            if (row == null)
                return false;

            var name = row.name ?? string.Empty;
            if (string.Equals(name, "bizman_status", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("job", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("special", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return row.Find("Collapse") != null || row.Find("Collapsed") != null;
        }

        private Transform ResolveMapFilterContentRoot()
        {
            Transform best = null;
            var bestScore = int.MinValue;

            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.gameObject == null || !transform.gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(transform);
                if (!IsMapFilterPath(path) || path.IndexOf("StreetQuest", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var childCount = 0;
                var rowsWithToggle = 0;
                foreach (Transform child in transform)
                {
                    childCount++;
                    if (child != null && child.GetComponentInChildren<Toggle>(true) != null)
                        rowsWithToggle++;
                }

                if (childCount < 5 || rowsWithToggle < 3)
                    continue;

                var score = rowsWithToggle * 10 + childCount;
                if (path.EndsWith("/Content", StringComparison.OrdinalIgnoreCase) || string.Equals(transform.name, "Content", StringComparison.OrdinalIgnoreCase))
                    score += 100;
                if (path.IndexOf("Viewport", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 25;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = transform;
            }

            if (best != null)
                DebugLog($"Map filter content root resolved path={GetHierarchyPath(best)} score={bestScore}");

            return best;
        }

        private static bool IsMapFilterPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.IndexOf("CityMap", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   path.IndexOf("MapFilter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeHideClosedBusinessesPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var compact = path
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty);

            return compact.IndexOf("CityMapFilterHideClosed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("FilterHideClosed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("HideClosed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("HideClosedBusinesses", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("ClosedBusinesses", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (compact.IndexOf("Closed", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    compact.IndexOf("Business", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private Transform ResolveMapFilterRowRoot(Toggle toggle)
        {
            if (toggle == null)
                return null;

            var row = ResolveMapFilterRowRootFromChild(toggle.transform);
            DebugLog(
                row != null
                    ? $"Map filter row root resolved toggle={GetHierarchyPath(toggle.transform)} row={GetHierarchyPath(row)}"
                    : $"Map filter row root fallback failed toggle={GetHierarchyPath(toggle.transform)}");
            return row ?? toggle.transform;
        }

        private Transform ResolveMapFilterRowRootFromChild(Transform childTransform)
        {
            var current = childTransform;
            for (var depth = 0; depth < 8 && current != null && current.parent != null; depth++)
            {
                var parent = current.parent;
                var siblingRows = 0;
                foreach (Transform child in parent)
                {
                    if (child == null)
                        continue;

                    if (child.GetComponentInChildren<Toggle>(true) != null || HasReadableTextComponent(child))
                        siblingRows++;
                }

                if (siblingRows >= 4 && GetHierarchyPath(parent).IndexOf("MapFilter", StringComparison.OrdinalIgnoreCase) >= 0)
                    return current;

                current = parent;
            }

            return childTransform;
        }

        private static bool HasReadableTextComponent(Transform root)
        {
            if (root == null)
                return false;

            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (text != null && !string.IsNullOrWhiteSpace(text.text))
                    return true;
            }

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Text)
                    continue;

                var type = component.GetType();
                var fullName = type.FullName ?? string.Empty;
                if (fullName.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) < 0 &&
                    fullName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var textProperty = type.GetProperty("text", ReflectionFlags);
                if (textProperty == null || textProperty.PropertyType != typeof(string))
                    continue;

                try
                {
                    return !string.IsNullOrWhiteSpace(textProperty.GetValue(component) as string);
                }
                catch
                {
                    // Ignore unsupported text-like components.
                }
            }

            return false;
        }

        private static int ApplyMapFilterLabel(GameObject rowObject)
        {
            if (rowObject == null)
                return 0;

            var count = 0;
            foreach (var text in rowObject.GetComponentsInChildren<Text>(true))
            {
                if (text == null)
                    continue;

                text.text = MapFilterLabel;
                text.raycastTarget = false;
                count++;
            }

            foreach (var component in rowObject.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Text)
                    continue;

                var type = component.GetType();
                var fullName = type.FullName ?? string.Empty;
                if (fullName.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) < 0 &&
                    fullName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var textProperty = type.GetProperty("text", ReflectionFlags);
                if (textProperty == null || !textProperty.CanWrite || textProperty.PropertyType != typeof(string))
                    continue;

                try
                {
                    textProperty.SetValue(component, MapFilterLabel);
                    count++;
                }
                catch
                {
                    // Ignore unsupported text-like components.
                }
            }

            return count;
        }

        private static int ScoreMapFilterIconCandidate(Image image, Toggle toggle)
        {
            if (image == null || image.transform == null)
                return int.MinValue;

            var name = image.name ?? string.Empty;
            if (ContainsAnyIgnoreCase(name, "background", "check", "toggle", "switch", "knob", "handle", "thumb", "fill", "mask", "collapse", "arrow"))
                return int.MinValue / 2;

            if (toggle != null)
            {
                if (toggle.graphic != null && IsSameOrChildOf(image.transform, toggle.graphic.transform))
                    return int.MinValue / 2;

                if (toggle.targetGraphic != null && IsSameOrChildOf(image.transform, toggle.targetGraphic.transform))
                    return int.MinValue / 2;
            }

            var score = 0;
            if (ContainsAnyIgnoreCase(name, "icon", "image", "sprite", "picto"))
                score += 60;

            if (image.sprite != null)
                score += 20;

            var rect = image.rectTransform;
            if (rect != null)
            {
                var width = Mathf.Abs(rect.sizeDelta.x);
                var height = Mathf.Abs(rect.sizeDelta.y);
                if (width <= 0.1f || height <= 0.1f)
                {
                    width = Mathf.Abs(rect.rect.width);
                    height = Mathf.Abs(rect.rect.height);
                }

                if (width >= 12f && width <= 90f && height >= 12f && height <= 90f)
                    score += 30;

                if (width > 0.1f && height > 0.1f && Mathf.Abs(width - height) <= 20f)
                    score += 20;

                if (rect.localPosition.x < 0f)
                    score += 25;

                if (rect.localPosition.x > 120f)
                    score -= 25;
            }

            return score;
        }

        private static bool ContainsAnyIgnoreCase(string value, params string[] tokens)
        {
            if (string.IsNullOrEmpty(value) || tokens == null)
                return false;

            foreach (var token in tokens)
            {
                if (!string.IsNullOrEmpty(token) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsSameOrChildOf(Transform transform, Transform possibleParent)
        {
            if (transform == null || possibleParent == null)
                return false;

            var current = transform;
            while (current != null)
            {
                if (current == possibleParent)
                    return true;

                current = current.parent;
            }

            return false;
        }

        private Sprite GetNameplateBackgroundSprite()
        {
            if (_nameplateBackgroundSprite != null)
                return _nameplateBackgroundSprite;

            var texture = new Texture2D(NameplateBackgroundWidth, NameplateBackgroundHeight, TextureFormat.RGBA32, false)
            {
                name = "StreetQuestMarkerNameplateRoundedBackground"
            };
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[NameplateBackgroundWidth * NameplateBackgroundHeight];
            var transparent = new Color32(255, 255, 255, 0);
            var opaque = new Color32(255, 255, 255, 255);
            for (var y = 0; y < NameplateBackgroundHeight; y++)
            {
                for (var x = 0; x < NameplateBackgroundWidth; x++)
                {
                    pixels[y * NameplateBackgroundWidth + x] = IsInsideRoundedRect(
                        x + 0.5f,
                        y + 0.5f,
                        NameplateBackgroundWidth,
                        NameplateBackgroundHeight,
                        NameplateCornerRadiusPixels)
                        ? opaque
                        : transparent;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _nameplateBackgroundSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, NameplateBackgroundWidth, NameplateBackgroundHeight),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(
                    NameplateCornerRadiusPixels,
                    NameplateCornerRadiusPixels,
                    NameplateCornerRadiusPixels,
                    NameplateCornerRadiusPixels));
            _nameplateBackgroundSprite.name = "StreetQuestMarkerNameplateRoundedBackgroundSprite";
            return _nameplateBackgroundSprite;
        }

        private static bool IsInsideRoundedRect(float x, float y, int width, int height, float radius)
        {
            var clampedX = Mathf.Clamp(x, radius, width - radius);
            var clampedY = Mathf.Clamp(y, radius, height - radius);
            var dx = x - clampedX;
            var dy = y - clampedY;
            return dx * dx + dy * dy <= radius * radius;
        }

        private Toggle ResolveMapFilterToggleTemplate()
        {
            Toggle best = null;
            var bestScore = int.MinValue;

            foreach (var toggle in Resources.FindObjectsOfTypeAll<Toggle>())
            {
                if (toggle == null || toggle.gameObject == null)
                    continue;

                if (!toggle.gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(toggle.transform);
                if (string.IsNullOrWhiteSpace(path) || path.IndexOf("CityMap", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (path.IndexOf("StreetQuest", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var label = ResolveToggleLabel(toggle);
                var score = 0;
                if (path.IndexOf("Filter", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 50;
                if (label.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;
                if (label.IndexOf("business", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 30;
                if (label.IndexOf("hide", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 15;
                if (!string.IsNullOrWhiteSpace(label))
                    score += 10;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = toggle;
            }

            return best;
        }

        private static string ResolveToggleLabel(Toggle toggle)
        {
            if (toggle == null)
                return string.Empty;

            foreach (var text in toggle.GetComponentsInChildren<Text>(true))
            {
                if (text == null || string.IsNullOrWhiteSpace(text.text))
                    continue;

                return text.text.Trim();
            }

            return toggle.gameObject.name ?? string.Empty;
        }

        private string ApplyMapFilterIcon(GameObject rowObject, Toggle toggle)
        {
            if (rowObject == null)
                return "row-missing";

            var sprite = GetMarkerSprite();
            var image = rowObject
                .GetComponentsInChildren<Image>(true)
                .Select(candidate => new
                {
                    Image = candidate,
                    Score = ScoreMapFilterIconCandidate(candidate, toggle)
                })
                .Where(candidate => candidate.Image != null && candidate.Score > int.MinValue / 4)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Image)
                .FirstOrDefault();

            if (image == null)
                return CreateFallbackMapFilterIcon(rowObject, sprite);

            var oldName = image.sprite != null ? image.sprite.name : "<none>";
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.rectTransform.localRotation = Quaternion.identity;
            image.rectTransform.localScale = Vector3.one;
            image.raycastTarget = false;
            return $"{GetHierarchyPath(image.transform)} oldSprite={oldName} newSprite={(sprite != null ? sprite.name : "<null>")}";
        }

        private static string CreateFallbackMapFilterIcon(GameObject rowObject, Sprite sprite)
        {
            if (rowObject == null)
                return "fallback-row-missing";

            var textRect = rowObject.GetComponentsInChildren<Text>(true)
                .Select(text => text != null ? text.rectTransform : null)
                .FirstOrDefault(rect => rect != null);
            var parent = textRect != null && textRect.parent != null ? textRect.parent : rowObject.transform;

            var iconObject = new GameObject("StreetQuestMapFilterIcon", typeof(RectTransform), typeof(Image));
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(parent, false);
            iconRect.sizeDelta = new Vector2(34f, 34f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);

            if (textRect != null)
            {
                iconRect.anchorMin = textRect.anchorMin;
                iconRect.anchorMax = textRect.anchorMin;
                iconRect.anchoredPosition = textRect.anchoredPosition + new Vector2(-42f, 0f);
            }
            else
            {
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(24f, 0f);
            }

            var image = iconObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            iconRect.localRotation = Quaternion.identity;
            iconRect.localScale = Vector3.one;
            image.raycastTarget = false;

            return $"fallback-created path={GetHierarchyPath(iconRect)} sprite={(sprite != null ? sprite.name : "<null>")}";
        }

        private static void StripNonUiComponents(GameObject rootObject)
        {
            if (rootObject == null)
                return;

            foreach (var component in rootObject.GetComponentsInChildren<Component>(true).ToArray())
            {
                if (component == null)
                    continue;

                if (component is Transform ||
                    component is CanvasRenderer ||
                    component is Graphic ||
                    component is Selectable ||
                    component is LayoutGroup ||
                    component is LayoutElement ||
                    component is ContentSizeFitter ||
                    component is CanvasGroup)
                {
                    continue;
                }

                if (component.GetType().Namespace != null &&
                    component.GetType().Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
                {
                    continue;
                }

                Destroy(component);
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
                image.color = MarkerBlobColor;
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.enabled = true;
                DebugLog($"Map marker visual fallback=plain_image characterId={characterId}");
            }

            EnsureMarkerHoverTarget(rectTransform, characterId);
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
                ApplyFixedStreetQuestMarkerColors(pointerCloneObject.transform, isPointer: true);
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
                ApplyFixedStreetQuestMarkerColors(blobCloneObject.transform, isPointer: false);
                ApplyCustomMarkerIcon(blobCloneObject.transform);
            }

            DebugLog($"Map marker visual source=vanilla_blob template={template.name}");
            return true;
        }

        private static void ApplyFixedStreetQuestMarkerVisualColors(Transform markerRoot)
        {
            if (markerRoot == null)
                return;

            var pointer = markerRoot.Find("StreetQuestMarkerPointer");
            if (pointer != null)
                ApplyFixedStreetQuestMarkerColors(pointer, isPointer: true);

            var blob = markerRoot.Find("StreetQuestMarkerBlob");
            if (blob != null)
                ApplyFixedStreetQuestMarkerColors(blob, isPointer: false);
        }

        private static void StripMarkerRuntimeComponents(GameObject rootObject)
        {
            if (rootObject == null)
                return;

            foreach (var component in rootObject.GetComponentsInChildren<Component>(includeInactive: true).ToArray())
            {
                if (component == null)
                    continue;

                if (component is Transform ||
                    component is CanvasRenderer ||
                    component is Graphic ||
                    component is CanvasGroup)
                {
                    continue;
                }

                Destroy(component);
            }
        }

        private static void ApplyFixedStreetQuestMarkerColors(Transform rootTransform, bool isPointer)
        {
            if (rootTransform == null)
                return;

            var images = rootTransform
                .GetComponentsInChildren<Image>(includeInactive: true)
                .Where(image => image != null && !IsMarkerIconTransform(image.transform))
                .ToArray();

            if (isPointer)
            {
                foreach (var image in images)
                    image.color = MarkerPointerColor;
                return;
            }

            for (var i = 0; i < images.Length; i++)
            {
                var image = images[i];
                var imageName = image.name ?? string.Empty;
                if (imageName.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    imageName.IndexOf("border", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    imageName.IndexOf("ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    imageName.IndexOf("stroke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (images.Length > 1 && i == 0))
                {
                    image.color = MarkerOutlineColor;
                    continue;
                }

                image.color = MarkerBlobColor;
            }
        }

        private static bool IsMarkerIconTransform(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.name) &&
                    current.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
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

            StripMarkerRuntimeComponents(rootObject);

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
                DebugLog($"Resolved map marker template: {GetHierarchyPath(childRect)}");
                return _poiMarkerTemplate;
            }

            if (structuredFallbackTemplate != null)
            {
                _poiMarkerTemplate = structuredFallbackTemplate;
                DebugLog($"Resolved structured fallback map marker template: {GetHierarchyPath(structuredFallbackTemplate)}");
                return _poiMarkerTemplate;
            }

            if (dynamicStructuredTemplate != null)
            {
                _poiMarkerTemplate = dynamicStructuredTemplate;
                DebugLog($"Resolved dynamic/player map marker template: {GetHierarchyPath(dynamicStructuredTemplate)}");
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
                        _markerSprite.name = "StreetQuestPersonIcon";
                        DebugLog(
                            $"Loaded marker sprite from {MarkerIconFileName} size={iconTexture.width}x{iconTexture.height}");
                        return _markerSprite;
                    }
                }
                catch (Exception exception)
                {
                    DebugLog($"Failed loading marker sprite from {MarkerIconFileName}: {exception.Message}");
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
            _markerSprite.name = "StreetQuestFallbackIcon";
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

            var projectionCamera = ResolveMapProjectionCamera();
            if (projectionCamera == null)
            {
                MaybeLogVerbose("Projection calibration failed: no map/render camera found.");
                return false;
            }

            var uiCamera = ResolveCanvasCamera(poiRoot);
            if (!TryResolvePlayerPoiSample(out var playerRect, out var playerWorldPosition, out var playerAnchoredPosition))
            {
                _useProjectionCalibration = true;
                _projectionCamera = projectionCamera;
                _projectionUiCamera = uiCamera;
                _projectionOffset = Vector2.zero;
                _lastPlayerPoiRect = null;
                _lastPlayerWorldPosition = default;
                _lastPlayerUiPosition = default;

                if (_elapsedSeconds >= _nextCalibrationLogAtSeconds)
                {
                    _nextCalibrationLogAtSeconds = _elapsedSeconds + 2f;
                    DebugLog(
                        $"Map marker projection calibration using direct projection fallback; no live Player POI sample. " +
                        $"camera={GetHierarchyPath(projectionCamera.transform)} uiCamera={(_projectionUiCamera != null ? GetHierarchyPath(_projectionUiCamera.transform) : "<overlay>")}");
                }

                return true;
            }

            if (!TryProjectWorldPositionToPoiLocal(playerWorldPosition, projectionCamera, uiCamera, out var projectedPlayerPosition))
            {
                _useProjectionCalibration = true;
                _projectionCamera = projectionCamera;
                _projectionUiCamera = uiCamera;
                _projectionOffset = Vector2.zero;
                _lastPlayerPoiRect = playerRect;
                _lastPlayerWorldPosition = playerWorldPosition;
                _lastPlayerUiPosition = playerAnchoredPosition;
                MaybeLogVerbose(
                    $"Projection calibration fell back to direct projection: could not project player world={FormatVector3(playerWorldPosition)} with camera={projectionCamera.name}.");
                return true;
            }

            _useProjectionCalibration = true;
            _projectionCamera = projectionCamera;
            _projectionUiCamera = uiCamera;
            var projectionOffset = playerAnchoredPosition - projectedPlayerPosition;
            _projectionOffset = projectionOffset.sqrMagnitude > ProjectionOffsetSanityLimit * ProjectionOffsetSanityLimit
                ? Vector2.zero
                : projectionOffset;
            _lastPlayerPoiRect = playerRect;
            _lastPlayerWorldPosition = playerWorldPosition;
            _lastPlayerUiPosition = playerAnchoredPosition;

            if (_elapsedSeconds >= _nextCalibrationLogAtSeconds)
            {
                _nextCalibrationLogAtSeconds = _elapsedSeconds + 2f;
                DebugLog(
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
                DebugLog($"Resolved calibration POI template from named Template: {GetHierarchyPath(namedTemplate)}");
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

                DebugLog($"Resolved calibration POI template from non-dynamic POI: {GetHierarchyPath(childRect)}");
                return childRect;
            }

            if (dynamicTemplate != null)
                DebugLog($"Resolved calibration POI template from dynamic fallback: {GetHierarchyPath(dynamicTemplate)}");

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
                    DebugLog(
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
                DebugLog(
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

                    DebugLog(
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

                DebugLog(
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

            DebugLog(
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
            DebugLog(
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
                DebugLog(
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
                ApplyFixedStreetQuestMarkerColors(pointerCloneObject.transform, isPointer: true);
            }

            if (blob != null)
            {
                var blobCloneObject = Instantiate(blob.gameObject, poiRootTransform, false);
                blobCloneObject.name = "StreetQuestNativeBlob";
                blobCloneObject.SetActive(true);
                PrepareMarkerVisual(blobCloneObject);
                ApplyFixedStreetQuestMarkerColors(blobCloneObject.transform, isPointer: false);
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

            HideMarkerNameplate();
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

        private void DestroyMapFilterRow()
        {
            if (_mapFilterRowObject != null)
                Destroy(_mapFilterRowObject);

            _mapFilterRowObject = null;
            _mapFilterToggle = null;
            _loggedMapFilterFailure = false;
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

            if (_nameplateRoot != null)
                Destroy(_nameplateRoot.gameObject);
            _nameplateRoot = null;
            _nameplateText = null;
            _hoveredMarkerRoot = null;
            _hoveredCharacterId = null;

            DestroyMapFilterRow();

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
                    !name.StartsWith("StreetQuestMapFilter.", StringComparison.Ordinal) &&
                    !name.StartsWith("StreetQuestMarkerNameplate", StringComparison.Ordinal) &&
                    !name.StartsWith("StreetQuestMarkerHoverHitTarget", StringComparison.Ordinal) &&
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
