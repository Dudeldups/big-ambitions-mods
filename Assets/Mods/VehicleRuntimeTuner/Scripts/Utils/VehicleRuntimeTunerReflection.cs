#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace VehicleRuntimeTuner.Utils
{
    public static class VehicleRuntimeTunerReflection
    {
        private static readonly Dictionary<string, MemberInfo?> CachedMembers = new Dictionary<string, MemberInfo?>(StringComparer.Ordinal);

        public static object? GetMemberValue(object? target, string name)
        {
            TryGetMemberValue(target, name, out var value);
            return value;
        }

        public static bool TryGetMemberValue(object? target, string name, out object? value)
        {
            value = null;
            if (target == null || string.IsNullOrWhiteSpace(name))
                return false;

            var member = GetCachedMember(target.GetType(), name);
            if (member is FieldInfo fieldInfo)
            {
                try
                {
                    value = fieldInfo.GetValue(target);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (member is PropertyInfo propertyInfo && propertyInfo.CanRead && propertyInfo.GetIndexParameters().Length == 0)
            {
                try
                {
                    value = propertyInfo.GetValue(target);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static bool TrySetMemberValue(object? target, string name, object? value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
                return false;

            var member = GetCachedMember(target.GetType(), name);
            if (member is FieldInfo fieldInfo)
            {
                try
                {
                    fieldInfo.SetValue(target, ConvertValue(value, fieldInfo.FieldType));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (member is PropertyInfo propertyInfo && propertyInfo.CanWrite && propertyInfo.GetIndexParameters().Length == 0)
            {
                try
                {
                    propertyInfo.SetValue(target, ConvertValue(value, propertyInfo.PropertyType));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static bool TryGetBooleanMemberValue(object target, string name)
        {
            if (!TryGetMemberValue(target, name, out var value) || value == null)
                return false;

            return value is bool boolValue && boolValue;
        }

        public static bool HasMember(object target, string name)
        {
            return GetCachedMember(target.GetType(), name) != null;
        }

        public static bool TryGetFieldValue(object target, FieldInfo fieldInfo, out object? value)
        {
            try
            {
                value = fieldInfo.GetValue(target);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public static bool TryGetPropertyValue(object target, PropertyInfo propertyInfo, out object? value)
        {
            try
            {
                value = propertyInfo.GetValue(target);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public static string GetGameObjectPath(Transform? transform)
        {
            if (transform == null)
                return "<null>";

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        public static string FormatValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is Vector3 vector3)
                return $"({vector3.x.ToString(CultureInfo.InvariantCulture)}, {vector3.y.ToString(CultureInfo.InvariantCulture)}, {vector3.z.ToString(CultureInfo.InvariantCulture)})";

            if (value is Quaternion quaternion)
                return $"({quaternion.x.ToString(CultureInfo.InvariantCulture)}, {quaternion.y.ToString(CultureInfo.InvariantCulture)}, {quaternion.z.ToString(CultureInfo.InvariantCulture)}, {quaternion.w.ToString(CultureInfo.InvariantCulture)})";

            return value.ToString() ?? string.Empty;
        }

        private static MemberInfo? GetCachedMember(Type type, string name)
        {
            var cacheKey = type.FullName + "::" + name;
            if (CachedMembers.TryGetValue(cacheKey, out var cached))
                return cached;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MemberInfo? member =
                (MemberInfo?)type.GetField(name, flags) ??
                type.GetProperty(name, flags);

            CachedMembers[cacheKey] = member;
            return member;
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null)
                return null;

            var valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType))
                return value;

            if (targetType.IsEnum && value is string enumText)
                return Enum.Parse(targetType, enumText, true);

            return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
    }
}
