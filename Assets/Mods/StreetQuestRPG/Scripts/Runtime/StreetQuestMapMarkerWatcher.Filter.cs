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
            DetachMapFilterMasterToggleListener();
            _mapFilterMasterToggle = null;
            _mapFilterMasterToggleListenerAttached = false;
            _lastMapFilterMasterToggleValue = null;
            _mapFilterMasterToggleResolvedForSession = false;

            var siblingIndex = Mathf.Min(rowTemplate.GetSiblingIndex() + 1, parent.childCount - 1);
            rowObject.transform.SetSiblingIndex(siblingIndex);
            ApplyMapFilterVisibility(_mapFilterVisible, "toggle-created", persist: false);

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
            ApplyMapFilterVisibility(visible, "people-ui", persist: true);
        }
        private void ApplyMapFilterVisibility(bool visible, string source, bool persist)
        {
            _mapFilterVisible = visible;

            if (persist)
            {
                UnityEngine.PlayerPrefs.SetInt(MapFilterPrefsKey, visible ? 1 : 0);
                UnityEngine.PlayerPrefs.Save();
            }

            if (_streetQuestRoot != null && _streetQuestRoot.gameObject != null)
                _streetQuestRoot.gameObject.SetActive(visible);

            if (_mapFilterToggle != null && _mapFilterToggle.gameObject != null && _mapFilterToggle.isOn != visible)
                _mapFilterToggle.SetIsOnWithoutNotify(visible);

            if (!visible)
                HideMarkerNameplate();

            DebugLog(
                $"Map filter apply source={source} visible={visible} persist={persist} " +
                $"masterKnown={(_mapFilterMasterToggle != null ? _mapFilterMasterToggle.isOn.ToString() : "<null>")}");
        }
        private void SyncWithMapFilterMasterToggle()
        {
            Toggle masterToggle = null;
            if (_mapFilterMasterToggle != null &&
                _mapFilterMasterToggle.gameObject != null &&
                _mapFilterMasterToggle.gameObject.activeInHierarchy)
            {
                masterToggle = _mapFilterMasterToggle;
            }
            else if (!_mapFilterMasterToggleResolvedForSession)
            {
                _mapFilterMasterToggleResolvedForSession = true;
                masterToggle = ResolveMapFilterMasterToggle();
                DebugLog(
                    $"Map filter master resolve attempted found={(masterToggle != null)} " +
                    $"path={(masterToggle != null ? GetHierarchyPath(masterToggle.transform) : "<none>")} " +
                    $"isOn={(masterToggle != null ? masterToggle.isOn.ToString() : "<n/a>")}");
            }

            if (masterToggle == null || masterToggle.gameObject == null || !masterToggle.gameObject.activeInHierarchy)
            {
                DetachMapFilterMasterToggleListener();
                _mapFilterMasterToggle = null;
                _lastMapFilterMasterToggleValue = null;
                return;
            }

            if (!ReferenceEquals(_mapFilterMasterToggle, masterToggle))
            {
                DetachMapFilterMasterToggleListener();
                _mapFilterMasterToggle = masterToggle;
                _lastMapFilterMasterToggleValue = masterToggle.isOn;
                AttachMapFilterMasterToggleListener(masterToggle);
                DebugLog(
                    $"Map filter master attached path={GetHierarchyPath(masterToggle.transform)} isOn={masterToggle.isOn} " +
                    $"peopleVisible={_mapFilterVisible}");

                return;
            }

            if (_lastMapFilterMasterToggleValue.HasValue && _lastMapFilterMasterToggleValue.Value == masterToggle.isOn)
                return;

            _lastMapFilterMasterToggleValue = masterToggle.isOn;
            DebugLog($"Map filter master changed via update isOn={masterToggle.isOn}");
            ApplyMapFilterVisibility(masterToggle.isOn, "master-update", persist: true);
        }
        private void AttachMapFilterMasterToggleListener(Toggle masterToggle)
        {
            if (masterToggle == null || _mapFilterMasterToggleListenerAttached)
                return;

            masterToggle.onValueChanged.AddListener(HandleMapFilterMasterToggleChanged);
            _mapFilterMasterToggleListenerAttached = true;
        }
        private void DetachMapFilterMasterToggleListener()
        {
            if (_mapFilterMasterToggle == null || !_mapFilterMasterToggleListenerAttached)
                return;

            _mapFilterMasterToggle.onValueChanged.RemoveListener(HandleMapFilterMasterToggleChanged);
            _mapFilterMasterToggleListenerAttached = false;
        }
        private void HandleMapFilterMasterToggleChanged(bool isOn)
        {
            _lastMapFilterMasterToggleValue = isOn;
            DebugLog($"Map filter master changed via listener isOn={isOn}");
            ApplyMapFilterVisibility(isOn, "master-listener", persist: true);
        }
        private Toggle ResolveMapFilterMasterToggle()
        {
            Toggle best = null;
            var bestScore = int.MinValue;

            foreach (var toggle in Resources.FindObjectsOfTypeAll<Toggle>())
            {
                if (toggle == null || toggle.gameObject == null || !toggle.gameObject.activeInHierarchy)
                    continue;

                var path = GetHierarchyPath(toggle.transform);
                if (!IsMapFilterPath(path) || path.IndexOf("StreetQuest", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var label = ResolveToggleLabel(toggle);
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                var normalized = label.Replace(" ", string.Empty);
                var score = 0;
                if (normalized.IndexOf("ENABLE/DISABLEALL", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 200;
                if (normalized.IndexOf("ENABLEDISABLEALL", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 200;
                if (label.IndexOf("enable", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;
                if (label.IndexOf("disable", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;
                if (label.IndexOf("all", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 40;
                if (path.IndexOf("Filter", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 10;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = toggle;
            }

            return bestScore > 0 ? best : null;
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
        private void DestroyMapFilterRow()
        {
            DetachMapFilterMasterToggleListener();

            if (_mapFilterRowObject != null)
                Destroy(_mapFilterRowObject);

            _mapFilterRowObject = null;
            _mapFilterToggle = null;
            _mapFilterMasterToggle = null;
            _mapFilterMasterToggleListenerAttached = false;
            _lastMapFilterMasterToggleValue = null;
            _mapFilterMasterToggleResolvedForSession = false;
        }
    }
}
