#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxSleepRestDurationService
    {
        private const int ExtendedMinutes = 24 * 60;
        private static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Dictionary<int, OriginalRestDurations> originalEnvironmentDurationsByKey =
            new Dictionary<int, OriginalRestDurations>();
        private readonly Dictionary<Type, EnvironmentPatchDescriptor?> descriptorCache = new Dictionary<Type, EnvironmentPatchDescriptor?>();

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredDurations(ModContext? context, BigHaxSettings settings)
        {
            PatchLoadedBehaviours();
        }

        public void RestoreOriginalDurationsOnShutdown()
        {
            RestoreOriginalDurations();
            originalEnvironmentDurationsByKey.Clear();
        }

        private int PatchLoadedBehaviours()
        {
            var patchedCount = 0;
            var components = Resources.FindObjectsOfTypeAll<Component>();
            foreach (var component in components)
            {
                if (component == null)
                    continue;

                var gameObject = component.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                    continue;

                var descriptor = GetDescriptor(component.GetType());
                if (descriptor == null)
                    continue;

                if (TryPatchEnvironment(component, descriptor.Value))
                    patchedCount++;
            }

            return patchedCount;
        }

        private int RestoreOriginalDurations()
        {
            if (originalEnvironmentDurationsByKey.Count == 0)
                return 0;

            var restoredCount = 0;
            var components = Resources.FindObjectsOfTypeAll<Component>();
            foreach (var component in components)
            {
                if (component == null)
                    continue;

                var gameObject = component.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                    continue;

                var descriptor = GetDescriptor(component.GetType());
                if (descriptor == null)
                    continue;

                if (TryRestoreEnvironment(component, descriptor.Value))
                    restoredCount++;
            }

            originalEnvironmentDurationsByKey.Clear();
            return restoredCount;
        }

        private bool TryPatchEnvironment(Component component, EnvironmentPatchDescriptor descriptor)
        {
            var environmentValue = descriptor.EnvironmentField.GetValue(component);
            if (environmentValue == null)
                return false;

            var balanceConfig = descriptor.BalanceConfigProperty.GetValue(environmentValue);
            if (balanceConfig == null)
                return false;

            var currentDefaultMinutes = (int)descriptor.GetDefaultMinutesMethod.Invoke(environmentValue, null);
            var currentMaxMinutes = (int)descriptor.MaxDurationMinutesField.GetValue(balanceConfig);
            if (currentDefaultMinutes >= ExtendedMinutes && currentMaxMinutes >= ExtendedMinutes)
                return false;

            var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
            if (!originalEnvironmentDurationsByKey.ContainsKey(key))
                originalEnvironmentDurationsByKey[key] = new OriginalRestDurations(currentDefaultMinutes, currentMaxMinutes);

            descriptor.SetDefaultMinutesMethod.Invoke(environmentValue, new object[] { ExtendedMinutes });
            descriptor.MaxDurationMinutesField.SetValue(balanceConfig, ExtendedMinutes);
            descriptor.EnvironmentField.SetValue(component, environmentValue);

            return true;
        }

        private bool TryRestoreEnvironment(Component component, EnvironmentPatchDescriptor descriptor)
        {
            var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
            if (!originalEnvironmentDurationsByKey.TryGetValue(key, out var originalDurations))
                return false;

            var environmentValue = descriptor.EnvironmentField.GetValue(component);
            if (environmentValue == null)
                return false;

            var balanceConfig = descriptor.BalanceConfigProperty.GetValue(environmentValue);
            if (balanceConfig == null)
                return false;

            descriptor.SetDefaultMinutesMethod.Invoke(environmentValue, new object[] { originalDurations.DefaultMinutes });
            descriptor.MaxDurationMinutesField.SetValue(balanceConfig, originalDurations.MaxMinutes);
            descriptor.EnvironmentField.SetValue(component, environmentValue);
            return true;
        }

        private EnvironmentPatchDescriptor? GetDescriptor(Type behaviourType)
        {
            if (descriptorCache.TryGetValue(behaviourType, out var cachedDescriptor))
                return cachedDescriptor;

            var descriptor = TryCreateDescriptor(behaviourType, "restEnvironment");

            descriptorCache[behaviourType] = descriptor;
            return descriptor;
        }

        private static EnvironmentPatchDescriptor? TryCreateDescriptor(
            Type behaviourType,
            string environmentFieldName)
        {
            var environmentField = behaviourType.GetField(environmentFieldName, InstanceFieldFlags);
            if (environmentField == null)
                return null;

            var getDefaultMinutesMethod = environmentField.FieldType.GetMethod("GetDefaultMinutes", InstanceFieldFlags);
            var setDefaultMinutesMethod = environmentField.FieldType.GetMethod("SetDefaultMinutes", InstanceFieldFlags, null, new[] { typeof(int) }, null);
            var balanceConfigProperty = environmentField.FieldType.GetProperty("BalanceConfig", InstanceFieldFlags);
            var maxDurationMinutesField = balanceConfigProperty?.PropertyType.GetField("maxDurationMinutes", InstanceFieldFlags);
            if (getDefaultMinutesMethod == null || getDefaultMinutesMethod.ReturnType != typeof(int) ||
                setDefaultMinutesMethod == null || balanceConfigProperty == null ||
                maxDurationMinutesField == null || maxDurationMinutesField.FieldType != typeof(int))
                return null;

            return new EnvironmentPatchDescriptor(
                environmentField,
                getDefaultMinutesMethod,
                setDefaultMinutesMethod,
                balanceConfigProperty,
                maxDurationMinutesField);
        }

        private static int BuildEnvironmentKey(Component component, string fieldName)
        {
            unchecked
            {
                return (component.GetInstanceID() * 397) ^ StringComparer.Ordinal.GetHashCode(fieldName);
            }
        }

        private readonly struct EnvironmentPatchDescriptor
        {
            public EnvironmentPatchDescriptor(
                FieldInfo environmentField,
                MethodInfo getDefaultMinutesMethod,
                MethodInfo setDefaultMinutesMethod,
                PropertyInfo balanceConfigProperty,
                FieldInfo maxDurationMinutesField)
            {
                EnvironmentField = environmentField;
                GetDefaultMinutesMethod = getDefaultMinutesMethod;
                SetDefaultMinutesMethod = setDefaultMinutesMethod;
                BalanceConfigProperty = balanceConfigProperty;
                MaxDurationMinutesField = maxDurationMinutesField;
            }

            public FieldInfo EnvironmentField { get; }
            public MethodInfo GetDefaultMinutesMethod { get; }
            public MethodInfo SetDefaultMinutesMethod { get; }
            public PropertyInfo BalanceConfigProperty { get; }
            public FieldInfo MaxDurationMinutesField { get; }
        }

        private readonly struct OriginalRestDurations
        {
            public OriginalRestDurations(int defaultMinutes, int maxMinutes)
            {
                DefaultMinutes = defaultMinutes;
                MaxMinutes = maxMinutes;
            }

            public int DefaultMinutes { get; }
            public int MaxMinutes { get; }
        }
    }
}
