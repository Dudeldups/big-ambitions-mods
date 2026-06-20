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
                    $"Placed marker with player-projection calibration. filterVisible={_mapFilterVisible}");
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
    }
}
