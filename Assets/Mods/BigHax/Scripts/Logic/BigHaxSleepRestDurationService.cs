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
        private const int ExtendedBenchRestMinutes = 24 * 60;
        private const int ExtendedBedSleepMinutes = 7 * 24 * 60;
        private static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Dictionary<int, OriginalRestDurations> originalEnvironmentDurationsByKey =
            new Dictionary<int, OriginalRestDurations>();
        private readonly Dictionary<int, int> originalBedConfigMaxMinutesByKey = new Dictionary<int, int>();
        private readonly Dictionary<string, EnvironmentPatchDescriptor?> descriptorCache = new Dictionary<string, EnvironmentPatchDescriptor?>();

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredDurations(ModContext? context, BigHaxSettings settings)
        {
            PatchLoadedBehaviours("restEnvironment", ExtendedBenchRestMinutes, null);
            if (settings.EnableExtendedBedSleep)
            {
                var patchedBedControllerCount = PatchLoadedBehaviours("sleepEnvironment", ExtendedBedSleepMinutes, "BedController");
                var patchedBedConfigCount = PatchLoadedBedSleepConfigurations();
                context?.Logger.Info($"BigHax: extended bed sleep to 7 days. controllers={patchedBedControllerCount}, configurations={patchedBedConfigCount}.");
            }
            else
            {
                RestoreOriginalDurations("sleepEnvironment");
                RestoreOriginalBedSleepConfigurations();
            }
        }

        public void RestoreOriginalDurationsOnShutdown()
        {
            RestoreOriginalDurations();
            originalEnvironmentDurationsByKey.Clear();
            RestoreOriginalBedSleepConfigurations();
            originalBedConfigMaxMinutesByKey.Clear();
        }

        private int PatchLoadedBehaviours(string environmentFieldName, int extendedMinutes, string? requiredBehaviourTypeName)
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

                if (requiredBehaviourTypeName != null && component.GetType().Name != requiredBehaviourTypeName)
                    continue;

                var descriptor = GetDescriptor(component.GetType(), environmentFieldName);
                if (descriptor == null)
                    continue;

                if (TryPatchEnvironment(component, descriptor.Value, extendedMinutes))
                    patchedCount++;
            }

            return patchedCount;
        }

        private int RestoreOriginalDurations(string? environmentFieldName = null)
        {
            if (originalEnvironmentDurationsByKey.Count == 0)
                return 0;

            if (environmentFieldName == null)
            {
                var totalRestoredCount = RestoreOriginalDurations("restEnvironment") + RestoreOriginalDurations("sleepEnvironment");
                originalEnvironmentDurationsByKey.Clear();
                return totalRestoredCount;
            }

            var restoredCount = 0;
            var components = Resources.FindObjectsOfTypeAll<Component>();
            foreach (var component in components)
            {
                if (component == null)
                    continue;

                var gameObject = component.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                    continue;

                if (environmentFieldName == "sleepEnvironment" && component.GetType().Name != "BedController")
                    continue;

                var descriptor = GetDescriptor(component.GetType(), environmentFieldName);
                if (descriptor == null)
                    continue;

                if (TryRestoreEnvironment(component, descriptor.Value))
                    restoredCount++;
            }

            return restoredCount;
        }

        private bool TryPatchEnvironment(Component component, EnvironmentPatchDescriptor descriptor, int extendedMinutes)
        {
            var environmentValue = descriptor.EnvironmentField.GetValue(component);
            if (environmentValue == null)
                return false;

            var balanceConfig = descriptor.BalanceConfigProperty.GetValue(environmentValue);
            if (balanceConfig == null)
                return false;

            var currentDefaultMinutes = (int)descriptor.GetDefaultMinutesMethod.Invoke(environmentValue, null);
            var currentMaxMinutes = (int)descriptor.MaxDurationMinutesField.GetValue(balanceConfig);
            if (currentDefaultMinutes >= extendedMinutes && currentMaxMinutes >= extendedMinutes)
                return false;

            var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
            if (!originalEnvironmentDurationsByKey.ContainsKey(key))
                originalEnvironmentDurationsByKey[key] = new OriginalRestDurations(currentDefaultMinutes, currentMaxMinutes);

            descriptor.SetDefaultMinutesMethod.Invoke(environmentValue, new object[] { extendedMinutes });
            descriptor.MaxDurationMinutesField.SetValue(balanceConfig, extendedMinutes);
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

        private int PatchLoadedBedSleepConfigurations()
        {
            var patchedCount = 0;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (candidate == null || candidate.GetType().FullName != "PlayerActivity.SleepEnvironmentConfig")
                    continue;

                var configType = candidate.GetType();
                var sleepEnvironmentTypeField = configType.GetField("sleepEnvironmentType", InstanceFieldFlags);
                var balanceConfigField = configType.GetField("balanceConfig", InstanceFieldFlags);
                var balanceConfig = balanceConfigField?.GetValue(candidate);
                var maxDurationMinutesField = balanceConfig?.GetType().GetField("maxDurationMinutes", InstanceFieldFlags);
                if (sleepEnvironmentTypeField?.GetValue(candidate)?.ToString() != "Bed" ||
                    maxDurationMinutesField == null || maxDurationMinutesField.FieldType != typeof(int))
                {
                    continue;
                }

                var currentMaxMinutes = (int)maxDurationMinutesField.GetValue(balanceConfig);
                if (currentMaxMinutes >= ExtendedBedSleepMinutes)
                    continue;

                var key = candidate.GetInstanceID();
                if (!originalBedConfigMaxMinutesByKey.ContainsKey(key))
                    originalBedConfigMaxMinutesByKey[key] = currentMaxMinutes;

                maxDurationMinutesField.SetValue(balanceConfig, ExtendedBedSleepMinutes);
                patchedCount++;
            }

            return patchedCount;
        }

        private void RestoreOriginalBedSleepConfigurations()
        {
            if (originalBedConfigMaxMinutesByKey.Count == 0)
                return;

            foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (candidate == null || !originalBedConfigMaxMinutesByKey.TryGetValue(candidate.GetInstanceID(), out var originalMaxMinutes))
                    continue;

                var balanceConfig = candidate.GetType().GetField("balanceConfig", InstanceFieldFlags)?.GetValue(candidate);
                var maxDurationMinutesField = balanceConfig?.GetType().GetField("maxDurationMinutes", InstanceFieldFlags);
                if (maxDurationMinutesField?.FieldType == typeof(int))
                    maxDurationMinutesField.SetValue(balanceConfig, originalMaxMinutes);
            }
        }

        private EnvironmentPatchDescriptor? GetDescriptor(Type behaviourType, string environmentFieldName)
        {
            var cacheKey = behaviourType.FullName + "|" + environmentFieldName;
            if (descriptorCache.TryGetValue(cacheKey, out var cachedDescriptor))
                return cachedDescriptor;

            var descriptor = TryCreateDescriptor(behaviourType, environmentFieldName);

            descriptorCache[cacheKey] = descriptor;
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
