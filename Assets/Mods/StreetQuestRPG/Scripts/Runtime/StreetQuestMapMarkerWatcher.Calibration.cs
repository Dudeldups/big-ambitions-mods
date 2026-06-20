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
    internal sealed partial class StreetQuestMapMarkerWatcher
    {
        private void EnsureCalibration(RectTransform poiRoot)
        {
            if (_projectionCalibrationLockedForSession &&
                _hasCalibration &&
                _poiRoot == poiRoot)
            {
                return;
            }

            _poiRoot = poiRoot;

            _hasCalibration = TryBuildProjectionCalibration(poiRoot);
            if (_hasCalibration)
            {
                _projectionCalibrationLockedForSession = true;
                _loggedCalibrationFailure = false;
                return;
            }

            _projectionCalibrationLockedForSession = false;
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
