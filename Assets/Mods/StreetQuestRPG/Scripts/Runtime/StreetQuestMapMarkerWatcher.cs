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
    internal sealed partial class StreetQuestMapMarkerWatcher : MonoBehaviour
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
        private bool _mapFilterVisible;
        private int _mapFilterStableFrames;
        private string _mapFilterReadinessSignature;
        private float _nextMapFilterReadinessLogAtSeconds;
        private Toggle _mapFilterToggle;
        private GameObject _mapFilterRowObject;
        private Toggle _mapFilterMasterToggle;
        private bool _mapFilterMasterToggleListenerAttached;
        private bool? _lastMapFilterMasterToggleValue;
        private bool _mapFilterMasterToggleResolvedForSession;
        private RectTransform _nameplateRoot;
        private Text _nameplateText;
        private Sprite _nameplateBackgroundSprite;
        private RectTransform _hoveredMarkerRoot;
        private string _hoveredCharacterId;
        private bool _projectionCalibrationLockedForSession;

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
            _nextCalibrationLogAtSeconds = 0f;
            _calibrationAnchorWorldPositionsInitialized = false;
            _useProjectionCalibration = false;
            _projectionCamera = null;
            _projectionUiCamera = null;
            _projectionOffset = Vector2.zero;
            _lastPlayerPoiRect = null;
            _lastPlayerWorldPosition = default;
            _lastPlayerUiPosition = default;
            _mapFilterVisible = UnityEngine.PlayerPrefs.GetInt(MapFilterPrefsKey, 0) != 0;
            _mapFilterStableFrames = 0;
            _mapFilterReadinessSignature = null;
            _nextMapFilterReadinessLogAtSeconds = 0f;
            _mapFilterToggle = null;
            _mapFilterRowObject = null;
            _mapFilterMasterToggle = null;
            _mapFilterMasterToggleListenerAttached = false;
            _lastMapFilterMasterToggleValue = null;
            _mapFilterMasterToggleResolvedForSession = false;
            _nameplateRoot = null;
            _nameplateText = null;
            _hoveredMarkerRoot = null;
            _hoveredCharacterId = null;
            _projectionCalibrationLockedForSession = false;
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
            TryCaptureMapSnapshot();
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
                DetachMapFilterMasterToggleListener();
                _mapFilterMasterToggle = null;
                _lastMapFilterMasterToggleValue = null;
                _mapFilterMasterToggleResolvedForSession = false;
                _projectionCalibrationLockedForSession = false;
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
            SyncWithMapFilterMasterToggle();
            UpdateNameplatePosition();
        }
    }
}
