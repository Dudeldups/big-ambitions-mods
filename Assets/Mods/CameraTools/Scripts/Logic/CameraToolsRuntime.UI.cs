#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private bool lastHiddenUiCityMapOpen;
        private bool lastHiddenUiHideMapMarkers;
        private float nextHiddenUiDiagnosticLogTime;

        private void HandleHideUiHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.HideUiHotkey))
                return;

            isUiHidden = !isUiHidden;
            if (isUiHidden)
                ApplyHiddenUi();
            else
                RestoreHiddenUi();
        }

        private void RefreshHiddenUiState()
        {
            if (!isUiHidden)
            {
                if (hiddenUiStates.Length > 0)
                    RestoreHiddenUi();

                return;
            }

            var cityMapOpen = IsCityMapOpen();
            var hideMapMarkers = settings != null && settings.HideMapMarkersWithUi;
            var needsRefresh = hiddenUiStates.Length == 0;

            if (cityMapOpen != lastHiddenUiCityMapOpen || hideMapMarkers != lastHiddenUiHideMapMarkers)
                needsRefresh = true;

            if (!needsRefresh)
            {
                foreach (var state in hiddenUiStates)
                {
                    if (state.Target == null)
                    {
                        needsRefresh = true;
                        break;
                    }

                    if (state.Target.activeSelf)
                        state.Target.SetActive(false);
                }
            }

            if (!needsRefresh && Time.unscaledTime >= nextHiddenUiRefreshTime)
                needsRefresh = true;

            if (!needsRefresh)
                return;

            ApplyHiddenUi();
        }

        private void ApplyHiddenUi()
        {
            RestoreHiddenUi();

            var cityMapOpen = IsCityMapOpen();
            var hideMapMarkers = settings != null && settings.HideMapMarkersWithUi;
            lastHiddenUiCityMapOpen = cityMapOpen;
            lastHiddenUiHideMapMarkers = hideMapMarkers;

            var logDiagnostics = hideMapMarkers && Time.unscaledTime >= nextHiddenUiDiagnosticLogTime;
            if (logDiagnostics)
                nextHiddenUiDiagnosticLogTime = Time.unscaledTime + 2f;

            var targets = ResolveHiddenUiTargets(cityMapOpen, hideMapMarkers, logDiagnostics);
            const int markerRendererCount = 0;
            if (targets.Count == 0)
            {
                nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
                LogHiddenUiDebug($"Hidden UI scan found no targets. cityMapOpen={cityMapOpen}, hideMapMarkers={hideMapMarkers}");
                return;
            }

            var states = new List<GameObjectActiveState>(targets.Count);
            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                states.Add(new GameObjectActiveState(target, target.activeSelf));
                target.SetActive(false);
            }

            hiddenUiStates = states.ToArray();
            nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
            LogHiddenUiDebug(
                $"Hidden UI V4_POI_ROOT_HIDE applied. cityMapOpen={cityMapOpen}, hideMapMarkers={hideMapMarkers}, uiTargets={hiddenUiStates.Length}, markerRenderers={markerRendererCount}, rendererScanDisabled=True");
        }

        private void RestoreHiddenUi()
        {
            foreach (var state in hiddenUiStates)
            {
                if (state.Target != null)
                    state.Target.SetActive(state.WasActive);
            }

            hiddenUiStates = Array.Empty<GameObjectActiveState>();
        }

        private static List<GameObject> ResolveHiddenUiTargets(bool cityMapOpen, bool hideMapMarkers, bool logDiagnostics)
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();
            var scanned = 0;
            var namedMarkerMatches = 0;
            var potentialMarkerMatches = 0;

            if (hideMapMarkers)
                AddKnownMapMarkerRoots(targets, seen, logDiagnostics);
            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                scanned++;
                if (rectTransform == null)
                    continue;

                var gameObject = rectTransform.gameObject;
                if (gameObject == null || gameObject.hideFlags != HideFlags.None || !gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
                var namedMarker = IsNamedMarkerPath(path) || HasMarkerComponentInHierarchy(rectTransform, cityMapOpen);
                var potentialMarker = hideMapMarkers && IsPotentialMarkerUiTransform(rectTransform, cityMapOpen);
                if (namedMarker)
                    namedMarkerMatches++;
                if (potentialMarker)
                    potentialMarkerMatches++;
                if (logDiagnostics && (namedMarker || potentialMarker) && namedMarkerMatches + potentialMarkerMatches <= 30)
                    LogHiddenUiDebug($"Hidden UI marker candidate: path={GetHierarchyPath(rectTransform)}, named={namedMarker}, potential={potentialMarker}, cityMapOpen={cityMapOpen}");
                var aggressiveMapMarkerMatch = cityMapOpen && hideMapMarkers &&
                    (namedMarker ||
                    potentialMarker ||
                    path.IndexOf("map", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("filter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("car", StringComparison.OrdinalIgnoreCase) >= 0);
                if (cityMapOpen && !hideMapMarkers && namedMarker)
                    continue;

                if (aggressiveMapMarkerMatch)
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    TryAddHiddenUiTarget(targets, seen, ResolveWorldMarkerRoot(rectTransform).gameObject);
                    continue;
                }

                if (!ShouldHideUiTransform(rectTransform, namedMarker, cityMapOpen, hideMapMarkers))
                    continue;

                var compactMarker =
                    hideMapMarkers && (potentialMarker ||
                    (!cityMapOpen && IsLikelyWorldMarker(rectTransform)) ||
                    (cityMapOpen && IsLikelyMapMarker(rectTransform)));
                if (compactMarker || namedMarker || aggressiveMapMarkerMatch)
                {
                    TryAddHiddenUiTarget(targets, seen, ResolveWorldMarkerRoot(rectTransform).gameObject);
                    continue;
                }

                if (IsLikelyFixedHudRegion(rectTransform))
                {
                    TryAddHiddenUiTarget(targets, seen, gameObject);
                    TryAddHiddenUiTarget(targets, seen, ResolveFixedHudRoot(rectTransform).gameObject);
                    continue;
                }

                TryAddHiddenUiTarget(targets, seen, gameObject);
            }

            var filtered = FilterNestedUiTargets(targets);
            if (logDiagnostics)
                LogHiddenUiDebug($"Hidden UI target scan: cityMapOpen={cityMapOpen}, hideMapMarkers={hideMapMarkers}, scannedRectTransforms={scanned}, rawTargets={targets.Count}, filteredTargets={filtered.Count}, namedMarkerMatches={namedMarkerMatches}, potentialMarkerMatches={potentialMarkerMatches}");

            return filtered;
        }

        private static void AddKnownMapMarkerRoots(List<GameObject> targets, HashSet<int> seen, bool logDiagnostics)
        {
            foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.gameObject == null)
                    continue;

                var gameObject = transform.gameObject;
                if (gameObject.hideFlags != HideFlags.None || !gameObject.activeInHierarchy)
                    continue;

                if (!IsKnownMapMarkerRoot(transform))
                    continue;

                TryAddHiddenUiTarget(targets, seen, gameObject);
                if (logDiagnostics)
                    LogHiddenUiDebug($"Hidden known map marker root: path={GetHierarchyPath(transform)}");
            }
        }

        private static bool IsKnownMapMarkerRoot(Transform transform)
        {
            var name = transform.name;
            if (!IsKnownMapMarkerRootName(name))
                return false;

            var current = transform.parent;
            var climbCount = 0;
            while (current != null && climbCount <= 4)
            {
                if (string.Equals(current.name, "CityMap", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current.name, "CityManager", StringComparison.OrdinalIgnoreCase))
                    return true;

                current = current.parent;
                climbCount++;
            }

            return false;
        }

        private static bool IsKnownMapMarkerRootName(string name)
        {
            return string.Equals(name, "Pois", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "POIs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Poi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Markers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "MapMarkers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Waypoints", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "MapIcons", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Icons", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryAddHiddenUiTarget(List<GameObject> targets, HashSet<int> seen, GameObject? target)
        {
            if (target == null)
                return;

            var id = target.GetInstanceID();
            if (!seen.Add(id))
                return;

            targets.Add(target);
        }

        private static bool ShouldHideUiTransform(RectTransform transform, bool namedMarker, bool cityMapOpen, bool hideMapMarkers)
        {
            if (transform.GetComponentInParent<Canvas>(true) == null)
                return false;

            var path = GetHierarchyPath(transform).ToLowerInvariant();
            if (path.IndexOf("bizphone", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            if (ContainsAny(path, HiddenUiIncludeKeywords) || IsLikelyFixedHudRegion(transform))
                return true;

            if (!hideMapMarkers)
                return false;

            return namedMarker ||
                IsPotentialMarkerUiTransform(transform, cityMapOpen) ||
                (!cityMapOpen && IsLikelyWorldMarker(transform)) ||
                (cityMapOpen && IsLikelyMapMarker(transform));
        }

        private static bool IsPotentialMarkerUiTransform(RectTransform rectTransform, bool cityMapOpen)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            if (!HasGraphicInMarkerHierarchy(rectTransform) && !HasMarkerComponentInHierarchy(rectTransform, cityMapOpen))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width <= 1f || height <= 1f)
                return false;

            if (!cityMapOpen)
                return width <= Screen.width * 0.34f && height <= Screen.height * 0.34f;

            if (IsLikelyFixedHudRegion(rectTransform))
                return false;

            if (width > Screen.width * 0.42f || height > Screen.height * 0.32f)
                return false;

            var aspectRatio = width > height ? width / height : height / width;
            return aspectRatio <= 10f;
        }

        private static bool HasMarkerComponentInHierarchy(Transform transform, bool cityMapOpen)
        {
            var current = transform;
            var climbCount = 0;
            while (current != null && climbCount <= 5)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    var typeName = component.GetType().Name.ToLowerInvariant();
                    if (ContainsAny(typeName, HiddenComponentMarkerKeywords))
                        return true;

                    if (cityMapOpen && ContainsAny(typeName, HiddenCityMapComponentMarkerKeywords))
                        return true;
                }

                current = current.parent;
                climbCount++;
            }

            return false;
        }

        private static bool IsNamedMarkerPath(string path)
        {
            return ContainsAny(path, HiddenUiMarkerKeywords);
        }

        private static bool IsLikelyFixedHudRegion(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width > Screen.width * 0.85f || height > Screen.height * 0.7f)
                return false;

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            var normalizedX = centerX / Screen.width;
            var normalizedY = centerY / Screen.height;

            var isTopLeftHud = normalizedX <= 0.25f && normalizedY >= 0.7f;
            var isTopCenterHud = normalizedX >= 0.25f && normalizedX <= 0.75f && normalizedY >= 0.75f;
            var isTopRightHud = normalizedX >= 0.75f && normalizedY >= 0.7f;
            var isLeftSideHud = normalizedX <= 0.3f && normalizedY >= 0.25f && normalizedY <= 0.7f;
            var isBottomRightHud = normalizedX >= 0.55f && normalizedY <= 0.42f;
            var isUpperMiddleSupportPanel = normalizedX >= 0.2f && normalizedX <= 0.8f && normalizedY >= 0.5f && normalizedY <= 0.78f;
            var isVehicleActionPanel = normalizedX >= 0.2f && normalizedX <= 0.8f && normalizedY >= 0.68f && normalizedY <= 0.9f;

            return isTopLeftHud || isTopCenterHud || isTopRightHud || isLeftSideHud || isBottomRightHud || isUpperMiddleSupportPanel || isVehicleActionPanel;
        }

        private static bool IsLikelyWorldMarker(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width > Screen.width * 0.18f || height > Screen.height * 0.18f)
                return false;

            var hasUiGraphic = HasGraphicInMarkerHierarchy(rectTransform);

            return hasUiGraphic;
        }

        private static bool IsLikelyMapMarker(RectTransform rectTransform)
        {
            if (!TryGetScreenRect(rectTransform, out var minX, out var minY, out var maxX, out var maxY))
                return false;

            var path = GetHierarchyPath(rectTransform).ToLowerInvariant();
            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            var width = maxX - minX;
            var height = maxY - minY;
            if (width <= 1f || height <= 1f)
                return false;

            if (width > Screen.width * 0.22f || height > Screen.height * 0.22f)
                return false;

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            var normalizedX = centerX / Screen.width;
            var normalizedY = centerY / Screen.height;
            if (normalizedX < 0.05f || normalizedX > 0.95f || normalizedY < 0.05f || normalizedY > 0.95f)
                return false;

            if (IsLikelyFixedHudRegion(rectTransform))
                return false;

            var aspectRatio = width > height ? width / height : height / width;
            if (aspectRatio > 2.5f)
                return false;

            return HasGraphicInMarkerHierarchy(rectTransform);
        }

        private static RectTransform ResolveWorldMarkerRoot(RectTransform rectTransform)
        {
            var best = rectTransform;
            var current = rectTransform;
            var climbCount = 0;
            while (current.parent is RectTransform parentRect &&
                parentRect.GetComponentInParent<Canvas>(true) != null &&
                !ContainsAny(GetHierarchyPath(parentRect).ToLowerInvariant(), HiddenUiExcludeKeywords) &&
                TryGetScreenRect(parentRect, out var minX, out var minY, out var maxX, out var maxY))
            {
                var width = maxX - minX;
                var height = maxY - minY;
                if (width > Screen.width * 0.18f || height > Screen.height * 0.18f)
                    break;

                best = parentRect;
                current = parentRect;
                climbCount++;
                if (climbCount >= 3)
                    break;
            }

            return best;
        }

        private static RectTransform ResolveFixedHudRoot(RectTransform rectTransform)
        {
            var best = rectTransform;
            var current = rectTransform;
            var climbCount = 0;
            while (current.parent is RectTransform parentRect &&
                parentRect.GetComponentInParent<Canvas>(true) != null &&
                !ContainsAny(GetHierarchyPath(parentRect).ToLowerInvariant(), HiddenUiExcludeKeywords) &&
                TryGetScreenRect(parentRect, out var minX, out var minY, out var maxX, out var maxY))
            {
                var width = maxX - minX;
                var height = maxY - minY;
                if (width > Screen.width * 0.9f || height > Screen.height * 0.45f)
                    break;

                best = parentRect;
                current = parentRect;
                climbCount++;
                if (climbCount >= 4)
                    break;
            }

            return best;
        }

        private static bool HasGraphicInMarkerHierarchy(RectTransform rectTransform)
        {
            return HasGraphicInMarkerHierarchy(rectTransform, depth: 0, visited: 0);
        }

        private static bool HasGraphicInMarkerHierarchy(RectTransform rectTransform, int depth, int visited)
        {
            if (depth > 5 || visited > 64)
                return false;

            if (rectTransform.GetComponent("Image") != null ||
                rectTransform.GetComponent("RawImage") != null ||
                rectTransform.GetComponent("TMP_Text") != null ||
                rectTransform.GetComponent("TextMeshProUGUI") != null ||
                rectTransform.GetComponent("CanvasRenderer") != null)
                return true;

            var nextVisited = visited;
            foreach (Transform child in rectTransform)
            {
                if (child is not RectTransform childRect)
                    continue;

                nextVisited++;
                if (nextVisited > 64)
                    return false;

                if (!TryGetScreenRect(childRect, out _, out _, out var childMaxX, out var childMaxY))
                    continue;

                if (childMaxX <= 0f && childMaxY <= 0f)
                    continue;

                if (HasGraphicInMarkerHierarchy(childRect, depth + 1, nextVisited))
                    return true;
            }

            return false;
        }

        private static bool TryGetScreenRect(RectTransform rectTransform, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = 0f;
            minY = 0f;
            maxX = 0f;
            maxY = 0f;
            if (Screen.width <= 0 || Screen.height <= 0)
                return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            minX = corners[0].x;
            minY = corners[0].y;
            maxX = corners[2].x;
            maxY = corners[2].y;
            return maxX - minX > 1f && maxY - minY > 1f;
        }

        private static List<GameObject> FilterNestedUiTargets(List<GameObject> targets)
        {
            var filtered = new List<GameObject>(targets.Count);
            for (var i = 0; i < targets.Count; i++)
            {
                var candidate = targets[i];
                if (candidate == null)
                    continue;

                var isChildOfSelectedTarget = false;
                for (var j = 0; j < targets.Count; j++)
                {
                    if (i == j)
                        continue;

                    var other = targets[j];
                    if (other == null)
                        continue;

                    if (candidate.transform.IsChildOf(other.transform))
                    {
                        isChildOfSelectedTarget = true;
                        break;
                    }
                }

                if (!isChildOfSelectedTarget)
                    filtered.Add(candidate);
            }

            return filtered;
        }

        private static bool ContainsAny(string source, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static readonly string[] HiddenComponentMarkerKeywords =
        {
            "marker",
            "mapmarker",
            "icon",
            "pin",
            "waypoint",
            "blip",
            "poi",
            "locationmarker",
            "buildingmarker",
            "vehiclemarker",
            "carmarker",
            "citymapmarker",
            "minimapmarker",
            "indicator",
            "overlay",
            "floating"
        };

        private static readonly string[] HiddenCityMapComponentMarkerKeywords =
        {
            "citymap",
            "mapicon",
            "maplabel",
            "mapbutton",
            "businessicon",
            "businesslabel",
            "buildingicon",
            "buildinglabel",
            "vehicleicon",
            "vehiclelabel",
            "locationicon",
            "locationlabel"
        };

        private static void LogHiddenUiDebug(string message)
        {
            if (!cameraToolsDebugEnabled)
                return;

            CameraToolsFileLogger.Log(message);
        }

        private bool IsGameplayInputBlockedByUi()
        {
            if (Time.unscaledTime < nextUiStateRefreshTime)
                return isGameplayUiBlocked;

            nextUiStateRefreshTime = Time.unscaledTime + UiStateRefreshIntervalSeconds;
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                if (eventSystem.IsPointerOverGameObject())
                    return isGameplayUiBlocked = true;

                var selectedGameObject = eventSystem.currentSelectedGameObject;
                if (selectedGameObject != null && selectedGameObject.activeInHierarchy)
                    return isGameplayUiBlocked = true;
            }

            if (IsCachedUiOpen(ref cachedMiniMenuController, miniMenuType, "IsOpen"))
                return isGameplayUiBlocked = true;

            if (IsCachedUiOpen(ref cachedFullMenuController, fullMenuType, "IsOpen"))
                return isGameplayUiBlocked = true;

            if (IsDialogPanelOpen())
                return isGameplayUiBlocked = true;

            isGameplayUiBlocked = false;
            return false;
        }

        private bool IsCachedUiOpen(ref MonoBehaviour? cachedController, Type? type, string propertyName)
        {
            if (cachedController == null || !cachedController.isActiveAndEnabled)
                cachedController = FindFirstActiveController(type, includeInactive: false);

            if (cachedController == null)
                return false;

            return TryGetBoolMember(cachedController, propertyName, out var isOpen) && isOpen;
        }

        private bool IsDialogPanelOpen()
        {
            if (dialogUiType == null)
                return false;

            if (cachedDialogUiController == null || !cachedDialogUiController.isActiveAndEnabled)
                cachedDialogUiController = FindFirstActiveController(dialogUiType, includeInactive: false);

            return cachedDialogUiController != null &&
                TryGetBoolMember(cachedDialogUiController, "isPanelOpen", out var isPanelOpen) &&
                isPanelOpen;
        }

        private static void ShowPopup(string message, string? duplicateIdentifier = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    message,
                    null,
                    6f,
                    duplicateIdentifier,
                    null,
                    false,
                    false);
            }
            catch (Exception exception)
            {
                LogVehicleDebug("Failed to show popup: " + exception.Message);
            }
        }

    }
}
