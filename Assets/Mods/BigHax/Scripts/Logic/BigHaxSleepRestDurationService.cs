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
        private const float ExtendedBedSleepTimeMachineSpeedMultiplier = 6f;
        private static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Dictionary<int, OriginalRestDurations> originalEnvironmentDurationsByKey =
            new Dictionary<int, OriginalRestDurations>();
        private readonly Dictionary<int, int> originalBedConfigMaxMinutesByKey = new Dictionary<int, int>();
        private readonly Dictionary<int, AnimationCurve> originalTimeMachineCurvesByKey = new Dictionary<int, AnimationCurve>();
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
            RestoreTimeMachineSpeed();
        }

        public void UpdateExtendedBedSleepBehavior(BigHaxSettings settings)
        {
            if (!settings.EnableExtendedBedSleep || !TryGetActiveSleepActivity(out var activity, out var activityUi))
            {
                RestoreTimeMachineSpeed();
                return;
            }

            var minutesToSleepField = activity.GetType().GetField("_minutesToSleep", InstanceFieldFlags);
            if (minutesToSleepField?.GetValue(activity) is not int minutesToSleep || minutesToSleep <= ExtendedBenchRestMinutes)
            {
                RestoreTimeMachineSpeed();
                return;
            }

            NormalizeWakeUpTimestamp(activity, activityUi);
            AccelerateTimeMachineIfRunning();
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

        private static bool TryGetActiveSleepActivity(out object activity, out Component activityUi)
        {
            activity = null!;
            activityUi = null!;
            foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null || component.GetType().FullName != "PlayerActivity.PlayerActivityUI")
                    continue;

                var currentActivity = component.GetType().GetProperty("GetCurrentActivity", InstanceFieldFlags)?.GetValue(component);
                if (currentActivity?.GetType().FullName != "PlayerActivity.SleepActivity")
                    continue;

                activity = currentActivity;
                activityUi = component;
                return true;
            }

            return false;
        }

        private static void NormalizeWakeUpTimestamp(object activity, Component activityUi)
        {
            var finishTimeField = activity.GetType().GetField("_finishTime", InstanceFieldFlags);
            var finishTime = finishTimeField?.GetValue(activity);
            if (finishTime == null)
                return;

            var timestampType = finishTime.GetType();
            var dayField = timestampType.GetField("Day", InstanceFieldFlags);
            var hourField = timestampType.GetField("Hour", InstanceFieldFlags);
            if (dayField?.GetValue(finishTime) is not int day || hourField?.GetValue(finishTime) is not int hour || hour < 24)
                return;

            dayField.SetValue(finishTime, day + hour / 24);
            hourField.SetValue(finishTime, hour % 24);
            finishTimeField!.SetValue(activity, finishTime);
            activityUi.GetType().GetMethod("UpdateActivityDisplay", InstanceFieldFlags)?.Invoke(activityUi, null);
        }

        private void AccelerateTimeMachineIfRunning()
        {
            foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null || component.GetType().FullName != "Timemachine.TimeMachine")
                    continue;

                var timeMachineType = component.GetType();
                if (timeMachineType.GetProperty("isRunning", InstanceFieldFlags)?.GetValue(component) is not bool isRunning || !isRunning)
                    continue;

                var curveField = timeMachineType.GetField("timeSpeedCurve", InstanceFieldFlags);
                if (curveField?.GetValue(component) is not AnimationCurve currentCurve)
                    continue;

                var key = component.GetInstanceID();
                if (originalTimeMachineCurvesByKey.ContainsKey(key))
                    continue;

                var acceleratedKeys = currentCurve.keys;
                for (var index = 0; index < acceleratedKeys.Length; index++)
                    acceleratedKeys[index].value *= ExtendedBedSleepTimeMachineSpeedMultiplier;

                var acceleratedCurve = new AnimationCurve(acceleratedKeys)
                {
                    preWrapMode = currentCurve.preWrapMode,
                    postWrapMode = currentCurve.postWrapMode
                };
                originalTimeMachineCurvesByKey[key] = currentCurve;
                curveField.SetValue(component, acceleratedCurve);
            }
        }

        private void RestoreTimeMachineSpeed()
        {
            if (originalTimeMachineCurvesByKey.Count == 0)
                return;

            foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null || !originalTimeMachineCurvesByKey.TryGetValue(component.GetInstanceID(), out var originalCurve))
                    continue;

                component.GetType().GetField("timeSpeedCurve", InstanceFieldFlags)?.SetValue(component, originalCurve);
            }

            originalTimeMachineCurvesByKey.Clear();
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
