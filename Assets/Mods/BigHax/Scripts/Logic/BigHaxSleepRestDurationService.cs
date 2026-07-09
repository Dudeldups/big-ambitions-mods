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
        private const int ExtendedHours = 24;
        private static readonly BindingFlags InstanceFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Dictionary<int, int> originalEnvironmentMaxHoursByKey = new Dictionary<int, int>();
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
            originalEnvironmentMaxHoursByKey.Clear();
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
            if (originalEnvironmentMaxHoursByKey.Count == 0)
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

            originalEnvironmentMaxHoursByKey.Clear();
            return restoredCount;
        }

        private bool TryPatchEnvironment(Component component, EnvironmentPatchDescriptor descriptor)
        {
            var environmentValue = descriptor.EnvironmentField.GetValue(component);
            if (environmentValue == null)
                return false;

            var currentMaxHours = (int)descriptor.MaxHoursField.GetValue(environmentValue);
            if (currentMaxHours >= ExtendedHours)
                return false;

            var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
            if (!originalEnvironmentMaxHoursByKey.ContainsKey(key))
                originalEnvironmentMaxHoursByKey[key] = currentMaxHours;

            descriptor.MaxHoursField.SetValue(environmentValue, ExtendedHours);
            descriptor.EnvironmentField.SetValue(component, environmentValue);

            return true;
        }

        private bool TryRestoreEnvironment(Component component, EnvironmentPatchDescriptor descriptor)
        {
            var key = BuildEnvironmentKey(component, descriptor.EnvironmentField.Name);
            if (!originalEnvironmentMaxHoursByKey.TryGetValue(key, out var originalMaxHours))
                return false;

            var environmentValue = descriptor.EnvironmentField.GetValue(component);
            if (environmentValue == null)
                return false;

            descriptor.MaxHoursField.SetValue(environmentValue, originalMaxHours);
            descriptor.EnvironmentField.SetValue(component, environmentValue);
            return true;
        }

        private EnvironmentPatchDescriptor? GetDescriptor(Type behaviourType)
        {
            if (descriptorCache.TryGetValue(behaviourType, out var cachedDescriptor))
                return cachedDescriptor;

            var descriptor =
                TryCreateDescriptor(behaviourType, "sleepEnvironment", "maxSleepHours") ??
                TryCreateDescriptor(behaviourType, "restEnvironment", "maxRestHours");

            descriptorCache[behaviourType] = descriptor;
            return descriptor;
        }

        private static EnvironmentPatchDescriptor? TryCreateDescriptor(
            Type behaviourType,
            string environmentFieldName,
            string maxHoursFieldName)
        {
            var environmentField = behaviourType.GetField(environmentFieldName, InstanceFieldFlags);
            if (environmentField == null)
                return null;

            var maxHoursField = environmentField.FieldType.GetField(maxHoursFieldName, InstanceFieldFlags);
            if (maxHoursField == null || maxHoursField.FieldType != typeof(int))
                return null;

            return new EnvironmentPatchDescriptor(environmentField, maxHoursField);
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
            public EnvironmentPatchDescriptor(FieldInfo environmentField, FieldInfo maxHoursField)
            {
                EnvironmentField = environmentField;
                MaxHoursField = maxHoursField;
            }

            public FieldInfo EnvironmentField { get; }
            public FieldInfo MaxHoursField { get; }
        }
    }
}
