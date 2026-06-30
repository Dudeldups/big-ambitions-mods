#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HQCentral.Debugging
{
    internal static class HQCentralUiSnapshotWriter
    {
        private const BindingFlags InstanceMemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static HQCentralUiSnapshotResult WriteVisibleUiSnapshot()
        {
            var roots = FindVisibleRootCanvases();
            var builder = new StringBuilder(32 * 1024);
            var elementCount = 0;

            builder.AppendLine("============================================================");
            builder.AppendLine($"HQCentral visible UI snapshot: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            builder.AppendLine($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            builder.AppendLine($"Screen: {Screen.width}x{Screen.height}, root canvases: {roots.Count}");
            AppendEventSystem(builder);

            foreach (var canvas in roots)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"CANVAS {GetHierarchyPath(canvas.transform)} " +
                    $"mode={canvas.renderMode} order={canvas.sortingOrder} camera={FormatObject(canvas.worldCamera)}");
                AppendVisibleHierarchy(canvas.transform, builder, 0, ref elementCount);
            }

            builder.AppendLine($"END snapshot: canvases={roots.Count}, elements={elementCount}");
            HQCentralFileLogger.AppendUiSnapshot(builder);
            return new HQCentralUiSnapshotResult(HQCentralFileLogger.UiLogPath, roots.Count, elementCount);
        }

        private static List<Canvas> FindVisibleRootCanvases()
        {
            var uniqueRoots = new Dictionary<int, Canvas>();
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy)
                    continue;

                if (!canvas.gameObject.scene.IsValid() || !IsEffectivelyVisible(canvas.transform))
                    continue;

                var root = canvas.rootCanvas;
                if (root == null || !root.enabled || !root.gameObject.activeInHierarchy)
                    continue;

                uniqueRoots[root.GetInstanceID()] = root;
            }

            var roots = new List<Canvas>(uniqueRoots.Values);
            roots.Sort((left, right) =>
                string.Compare(GetHierarchyPath(left.transform), GetHierarchyPath(right.transform), StringComparison.Ordinal));
            return roots;
        }

        private static void AppendEventSystem(StringBuilder builder)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                builder.AppendLine("EventSystem: <none>");
                return;
            }

            var selected = eventSystem.currentSelectedGameObject;
            builder.AppendLine(
                $"EventSystem: {GetHierarchyPath(eventSystem.transform)}, " +
                $"selected={(selected == null ? "<none>" : GetHierarchyPath(selected.transform))}");
        }

        private static void AppendVisibleHierarchy(
            Transform transform,
            StringBuilder builder,
            int depth,
            ref int elementCount)
        {
            if (!transform.gameObject.activeInHierarchy || !IsEffectivelyVisible(transform))
                return;

            elementCount++;
            builder.Append(' ', depth * 2);
            builder.Append("- ");
            builder.Append(transform.name);
            builder.Append(" [");
            builder.Append(GetComponentTypeNames(transform.gameObject));
            builder.Append(']');

            if (transform is RectTransform rectTransform)
                AppendRectDetails(builder, rectTransform);

            var text = TryGetText(transform.gameObject);
            if (!string.IsNullOrWhiteSpace(text))
                builder.Append($" text=\"{Escape(text!)}\"");

            var interactable = TryGetBoolMember(transform.gameObject, "interactable");
            if (interactable.HasValue)
                builder.Append($" interactable={interactable.Value}");

            builder.AppendLine();

            for (var index = 0; index < transform.childCount; index++)
                AppendVisibleHierarchy(transform.GetChild(index), builder, depth + 1, ref elementCount);
        }

        private static void AppendRectDetails(StringBuilder builder, RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var camera = ResolveCanvasCamera(rectTransform);
            var minimum = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var maximum = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            var onScreen = maximum.x >= 0f && maximum.y >= 0f &&
                minimum.x <= Screen.width && minimum.y <= Screen.height;

            builder.Append(
                $" rect=({minimum.x:0.#},{minimum.y:0.#})-({maximum.x:0.#},{maximum.y:0.#})" +
                $" onScreen={onScreen} anchors={rectTransform.anchorMin}->{rectTransform.anchorMax}" +
                $" pivot={rectTransform.pivot}");
        }

        private static Camera? ResolveCanvasCamera(RectTransform rectTransform)
        {
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            return canvas.worldCamera;
        }

        private static bool IsEffectivelyVisible(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                var canvas = current.GetComponent<Canvas>();
                if (canvas != null && !canvas.enabled)
                    return false;

                var canvasGroup = current.GetComponent<CanvasGroup>();
                if (canvasGroup != null && canvasGroup.alpha <= 0.001f)
                    return false;

                current = current.parent;
            }

            return true;
        }

        private static string GetComponentTypeNames(GameObject gameObject)
        {
            var names = new List<string>();
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component != null)
                    names.Add(component.GetType().FullName ?? component.GetType().Name);
            }

            return string.Join(", ", names.ToArray());
        }

        private static string? TryGetText(GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                var type = component.GetType();
                if (type.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0 &&
                    type.Name.IndexOf("InputField", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                try
                {
                    var property = type.GetProperty("text", InstanceMemberFlags);
                    if (property?.GetValue(component) is string value && !string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
                catch
                {
                    // UI components can be destroyed between discovery and reflection.
                }
            }

            return null;
        }

        private static bool? TryGetBoolMember(GameObject gameObject, string memberName)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                try
                {
                    var property = component.GetType().GetProperty(memberName, InstanceMemberFlags);
                    if (property?.PropertyType == typeof(bool))
                        return (bool?)property.GetValue(component);
                }
                catch
                {
                    // Ignore transient/destroyed UI components in a diagnostic snapshot.
                }
            }

            return null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string FormatObject(UnityEngine.Object? value)
        {
            return value == null ? "<none>" : value.name;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
        }
    }
}
