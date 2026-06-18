using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
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


        private static void InvokeParameterlessMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
                return;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var method = instanceType.GetMethod(methodName, ReflectionFlags, null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                method.Invoke(instance, null);
                return;
            }
        }


        private static string DescribeObject(object value)
        {
            if (value == null)
                return "<null>";

            if (value is Component component)
                return $"{component.GetType().FullName} name={component.name} id={component.GetInstanceID()}";

            if (value is GameObject gameObject)
                return $"{gameObject.GetType().FullName} name={gameObject.name} id={gameObject.GetInstanceID()}";

            if (value is UnityEngine.Object unityObject)
                return $"{unityObject.GetType().FullName} name={unityObject.name} id={unityObject.GetInstanceID()}";

            return $"{value.GetType().FullName}";
        }


        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var property = instanceType.GetProperty(memberName, ReflectionFlags);
                if (property != null)
                    return property.GetValue(instance);

                var field = instanceType.GetField(memberName, ReflectionFlags);
                if (field != null)
                    return field.GetValue(instance);
            }

            return null;
        }


        private static bool SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var property = instanceType.GetProperty(memberName, ReflectionFlags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, ConvertMemberValue(value, property.PropertyType));
                    return true;
                }

                var field = instanceType.GetField(memberName, ReflectionFlags);
                if (field == null)
                    continue;

                field.SetValue(instance, ConvertMemberValue(value, field.FieldType));
                return true;
            }

            return false;
        }


        private static object ConvertMemberValue(object value, Type targetType)
        {
            if (targetType.IsEnum)
            {
                var intValue = Convert.ToInt32(value);
                return Enum.ToObject(targetType, intValue);
            }

            return Convert.ChangeType(value, targetType);
        }
    }
}
