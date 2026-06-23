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
        private static bool IsCityMapOpen() => StreetQuestShared.IsCityMapOpen();
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
        private static string FormatVector2(Vector2 value)
        {
            return $"({value.x:F2}, {value.y:F2})";
        }
        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
        }
    }
}
