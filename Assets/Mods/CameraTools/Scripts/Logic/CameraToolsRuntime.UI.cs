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

            var needsRefresh = hiddenUiStates.Length == 0;
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

            if (!needsRefresh || Time.unscaledTime < nextHiddenUiRefreshTime)
                return;

            ApplyHiddenUi();
        }

        private void ApplyHiddenUi()
        {
            RestoreHiddenUi();

            var targets = ResolveHiddenUiTargets(IsCityMapOpen());
            if (targets.Count == 0)
            {
                nextHiddenUiRefreshTime = Time.unscaledTime + HiddenUiRefreshIntervalSeconds;
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

        private static List<GameObject> ResolveHiddenUiTargets(bool cityMapOpen)
        {
            var targets = new List<GameObject>();
            var seen = new HashSet<int>();
            foreach (var rectTransform in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rectTransform == null)
                    continue;

                var gameObject = rectTransform.gameObject;
                if (gameObject == null || gameObject.hideFlags != HideFlags.None || !gameObject.activeInHierarchy)
                    continue;

                if (!ShouldHideUiTransform(rectTransform))
                    continue;

                if (!cityMapOpen && IsLikelyWorldMarker(rectTransform))
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

            return FilterNestedUiTargets(targets);
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

        private static bool ShouldHideUiTransform(RectTransform transform)
        {
            if (transform.GetComponentInParent<Canvas>(true) == null)
                return false;

            var path = GetHierarchyPath(transform).ToLowerInvariant();
            if (path.IndexOf("bizphone", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (ContainsAny(path, HiddenUiExcludeKeywords))
                return false;

            return ContainsAny(path, HiddenUiIncludeKeywords) ||
                IsLikelyFixedHudRegion(transform) ||
                IsLikelyWorldMarker(transform);
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
            if (rectTransform.GetComponent("Image") != null ||
                rectTransform.GetComponent("RawImage") != null ||
                rectTransform.GetComponent("TMP_Text") != null ||
                rectTransform.GetComponent("TextMeshProUGUI") != null)
                return true;

            foreach (Transform child in rectTransform)
            {
                if (child is not RectTransform childRect)
                    continue;

                if (!TryGetScreenRect(childRect, out _, out _, out var childMaxX, out var childMaxY))
                    continue;

                if (childMaxX <= 0f && childMaxY <= 0f)
                    continue;

                if (childRect.GetComponent("Image") != null ||
                    childRect.GetComponent("RawImage") != null ||
                    childRect.GetComponent("TMP_Text") != null ||
                    childRect.GetComponent("TextMeshProUGUI") != null)
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
