using System;
using System.Collections.Generic;
using System.Linq;
using Buildings;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestMapMarkerWatcher
    {
        private void TryCaptureMapSnapshot()
        {
            if (!StreetQuestDebugSettings.Enabled || !Input.GetKeyDown(StreetQuestDebugSettings.MapSnapshotKey))
                return;

            if (!IsCityMapOpen())
            {
                StreetQuestShared.NotifyInfo("Open the city map first, then press F4.", "streetquest:debug_map_snapshot_map_closed", 2.5f);
                return;
            }

            if (!TryResolvePoiRoot(out var poiRoot))
            {
                StreetQuestShared.NotifyInfo("Map snapshot failed: POI root not found.", "streetquest:debug_map_snapshot_poi_missing", 2.5f);
                StreetQuestShared.LogSnapshot("MapSnapshot aborted: POI root missing while city map was open.", resetFile: true);
                return;
            }

            CaptureMapSnapshot(poiRoot);
            StreetQuestShared.NotifyInfo("Map snapshot written to streetquest-snapshot.log.", "streetquest:debug_map_snapshot_written", 2.5f);
        }

        private void CaptureMapSnapshot(RectTransform poiRoot)
        {
            if (poiRoot == null)
                return;

            var entries = new List<MapSnapshotEntry>();
            foreach (RectTransform rect in poiRoot)
            {
                if (rect == null || rect == poiRoot || rect == _streetQuestRoot)
                    continue;

                if (!rect.gameObject.activeInHierarchy)
                    continue;

                if (rect.name.StartsWith("StreetQuest", StringComparison.Ordinal))
                    continue;

                if (!TryBuildSnapshotEntry(rect, out var entry))
                    continue;

                entries.Add(entry);
            }

            var orderedEntries = entries
                .OrderBy(entry => entry.RootPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.RectPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            StreetQuestShared.LogSnapshot("=== MapSnapshot start ===", resetFile: true);
            StreetQuestShared.LogSnapshot($"MapSnapshot poiRoot={GetHierarchyPath(poiRoot)} count={orderedEntries.Count} currentAddress={StreetQuestShared.GetIndoorContextDisplayText()}");

            foreach (var entry in orderedEntries)
            {
                StreetQuestShared.LogSnapshot(
                    $"MapSnapshot entry root={entry.RootPath} rect={entry.RectPath} " +
                    $"anchored={FormatVector2(entry.AnchoredPosition)} size={FormatVector2(entry.SizeDelta)} " +
                    $"targetPath={entry.TargetPath} buildingAddress={entry.BuildingAddress} world={entry.WorldPositionText} " +
                    $"texts={entry.TextSummary} images={entry.ImageSummary}");
            }

            StreetQuestShared.LogSnapshot("=== MapSnapshot end ===");
        }

        private bool TryBuildSnapshotEntry(RectTransform rect, out MapSnapshotEntry entry)
        {
            entry = default;

            var poiRoot = FindPoiVisualRoot(rect);
            if (poiRoot == null)
                return false;

            var rectPath = GetHierarchyPath(rect);
            var rootPath = GetHierarchyPath(poiRoot);
            var targetPath = TryResolvePoiTargetPath(poiRoot, out var resolvedTargetPath)
                ? resolvedTargetPath
                : "<none>";
            var buildingAddress = TryResolvePoiBuildingAddress(poiRoot, out var resolvedBuildingAddress)
                ? resolvedBuildingAddress
                : "<none>";

            string worldPositionText;
            if (TryResolvePoiWorldPosition(poiRoot, out var worldPosition))
                worldPositionText = FormatVector3(worldPosition);
            else
                worldPositionText = "<none>";

            var texts = poiRoot.GetComponentsInChildren<Text>(true)
                .Select(text => text == null ? null : text.text?.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var images = poiRoot.GetComponentsInChildren<Image>(true)
                .Select(image => image == null ? null : DescribeImage(image))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            entry = new MapSnapshotEntry
            {
                RectPath = rectPath,
                RootPath = rootPath,
                TargetPath = targetPath,
                BuildingAddress = buildingAddress,
                WorldPositionText = worldPositionText,
                AnchoredPosition = poiRoot.anchoredPosition,
                SizeDelta = poiRoot.sizeDelta,
                TextSummary = texts.Length == 0 ? "<none>" : string.Join(" | ", texts),
                ImageSummary = images.Length == 0 ? "<none>" : string.Join(" | ", images)
            };

            return true;
        }

        private RectTransform FindPoiVisualRoot(RectTransform rect)
        {
            for (var current = rect; current != null && current != _poiRoot; current = current.parent as RectTransform)
            {
                if (current == null)
                    break;

                if (current.name.StartsWith("StreetQuest", StringComparison.Ordinal))
                    return null;

                if (HasPoiVisualForSnapshot(current) || HasPointOfInterestComponent(current))
                    return current;
            }

            return null;
        }

        private static bool HasPoiVisualForSnapshot(RectTransform rect)
        {
            if (rect == null)
                return false;

            return FindChildRectTransform(rect, "POIBlob") != null ||
                   FindChildRectTransform(rect, "POIPointer") != null;
        }

        private bool HasPointOfInterestComponent(RectTransform rect)
        {
            if (rect == null)
                return false;

            return rect.GetComponents<Component>()
                .Any(component => component != null &&
                                  string.Equals(component.GetType().Name, "PointOfInterest", StringComparison.Ordinal));
        }

        private bool TryResolvePoiTargetPath(RectTransform poiRoot, out string targetPath)
        {
            targetPath = null;
            if (poiRoot == null)
                return false;

            foreach (var component in poiRoot.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (!string.Equals(component.GetType().Name, "PointOfInterest", StringComparison.Ordinal))
                    continue;

                if (!TryReadMemberValue(component, "target", out var targetValue) || targetValue == null)
                    continue;

                if (TryGetTargetHierarchyPath(targetValue, out targetPath))
                    return true;
            }

            return false;
        }

        private bool TryResolvePoiWorldPosition(RectTransform poiRoot, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (poiRoot == null)
                return false;

            foreach (var component in poiRoot.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                if (!string.Equals(component.GetType().Name, "PointOfInterest", StringComparison.Ordinal))
                    continue;

                if (TryReadMemberValue(component, "target", out var targetValue) &&
                    TryResolveTargetWorldPosition(targetValue, out worldPosition))
                    return true;
            }

            return TryExtractWorldPosition(poiRoot, out worldPosition);
        }

        private bool TryResolvePoiBuildingAddress(RectTransform poiRoot, out string addressKey)
        {
            addressKey = null;
            if (poiRoot == null)
                return false;

            foreach (var component in poiRoot.GetComponents<Component>())
            {
                if (component == null ||
                    !string.Equals(component.GetType().Name, "PointOfInterest", StringComparison.Ordinal))
                    continue;

                if (!TryReadMemberValue(component, "target", out var targetValue) || targetValue == null)
                    continue;

                if (!TryGetTargetTransform(targetValue, out var targetTransform) || targetTransform == null)
                    continue;

                if (TryResolveAddressFromTargetTransformChain(targetTransform, out addressKey))
                    return true;
            }

            return false;
        }

        private static bool TryGetTargetTransform(object targetValue, out Transform targetTransform)
        {
            targetTransform = null;
            switch (targetValue)
            {
                case Transform transform:
                    targetTransform = transform;
                    return true;
                case Component component:
                    targetTransform = component.transform;
                    return true;
                case GameObject gameObject:
                    targetTransform = gameObject.transform;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryResolveAddressFromTargetTransformChain(Transform transform, out string addressKey)
        {
            addressKey = null;
            for (var current = transform; current != null; current = current.parent)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null || !IsViewBlockingEntityPartForSnapshot(component))
                        continue;

                    if (TryResolveAddressFromViewBlockingEntityPartForSnapshot(component, out addressKey))
                        return true;
                }
            }

            return false;
        }

        private static bool IsViewBlockingEntityPartForSnapshot(Component component)
        {
            var componentTypeName = component.GetType().Name;
            return string.Equals(componentTypeName, "ViewBlockingEntityPart", StringComparison.Ordinal) ||
                   string.Equals(component.GetType().FullName, "Entities.ViewBlockingEntityPart", StringComparison.Ordinal);
        }

        private static bool TryResolveAddressFromViewBlockingEntityPartForSnapshot(Component component, out string addressKey)
        {
            addressKey = null;
            if (!TryReadMemberValue(component, "cityBuildingController", out var cityBuildingController) ||
                cityBuildingController == null)
                return false;

            if (!TryReadMemberValue(cityBuildingController, "buildingRegistration", out var buildingRegistration) ||
                buildingRegistration == null)
                return false;

            if (!TryReadMemberValue(buildingRegistration, "Address", out var address))
                return false;

            return TryNormalizeAddressForSnapshot(address, out addressKey);
        }

        private static bool TryNormalizeAddressForSnapshot(object addressValue, out string addressKey)
        {
            addressKey = null;
            switch (addressValue)
            {
                case null:
                    return false;
                case Address address:
                    addressKey = address.ToString();
                    return !string.IsNullOrWhiteSpace(addressKey);
                default:
                    addressKey = addressValue.ToString();
                    return !string.IsNullOrWhiteSpace(addressKey);
            }
        }

        private static string DescribeImage(Image image)
        {
            if (image == null)
                return null;

            var spriteName = image.sprite != null ? image.sprite.name : "<none>";
            var color = ColorUtility.ToHtmlStringRGBA(image.color);
            return $"{image.name}:sprite={spriteName}:color=#{color}";
        }

        private sealed class MapSnapshotEntry
        {
            internal string RectPath;
            internal string RootPath;
            internal string TargetPath;
            internal string BuildingAddress;
            internal string WorldPositionText;
            internal Vector2 AnchoredPosition;
            internal Vector2 SizeDelta;
            internal string TextSummary;
            internal string ImageSummary;
        }
    }
}
