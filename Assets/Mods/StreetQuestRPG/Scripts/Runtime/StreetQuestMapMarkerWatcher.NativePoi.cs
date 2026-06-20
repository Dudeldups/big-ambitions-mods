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
    }
}
