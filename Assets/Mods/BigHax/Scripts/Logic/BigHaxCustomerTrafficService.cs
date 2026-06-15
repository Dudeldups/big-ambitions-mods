#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxCustomerTrafficService
    {
        private static readonly MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly Type? CustomerEntriesHelperType = FindType("AI.Customers.CustomerEntries.CustomerEntriesHelper");
        private static readonly MethodInfo? UpdateAllCustomerEntriesMethod =
            CustomerEntriesHelperType?.GetMethod("UpdateCustomerEntriesForAllPlayerBusinesses", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? GetBusinessCustomerEntriesMethod =
            CustomerEntriesHelperType?.GetMethod("GetBusinessCustomerEntries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? ShouldEntriesBeCreatedMethod =
            CustomerEntriesHelperType?.GetMethod("ShouldEntriesBeCreated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly Type? CustomerEntryType = FindType("AI.Customers.CustomerEntries.CustomerEntry");
        private static readonly Type? OrderType = FindType("Order");
        private static readonly Type? TimestampType = FindType("BigAmbitions.DayNightCycle.Timestamp");

        private static readonly FieldInfo? CustomerEntrySpawnTimeField = CustomerEntryType?.GetField("spawnTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? CustomerEntryCompletedField = CustomerEntryType?.GetField("completed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? CustomerEntryOrderField = CustomerEntryType?.GetField("order", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? OrderEntriesField = OrderType?.GetField("entries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? OrderCompletedField = OrderType?.GetField("completed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? OrderCustomerDemandTypesField = OrderType?.GetField("customerDemandTypes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? TimestampDayField = TimestampType?.GetField("Day", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? TimestampHourField = TimestampType?.GetField("Hour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? TimestampMinuteField = TimestampType?.GetField("Minute", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly Dictionary<string, int> originalCustomerCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> lastAppliedEntryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> lastAppliedCustomerCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private bool hasAppliedCustomTraffic;
        private int lastAppliedMultiplier = BigHaxSettings.DefaultCustomerTrafficMultiplier;

        public void InvalidateCache()
        {
            lastAppliedEntryCounts.Clear();
            lastAppliedCustomerCapacities.Clear();
        }

        public void ApplyConfiguredTraffic(ModContext context, BigHaxSettings settings, bool forceRefresh)
        {
            var multiplier = Mathf.Max(BigHaxSettings.DefaultCustomerTrafficMultiplier, settings.CustomerTrafficMultiplier);
            if (multiplier <= BigHaxSettings.DefaultCustomerTrafficMultiplier)
            {
                if (hasAppliedCustomTraffic || forceRefresh)
                    RestoreVanillaTraffic(context);

                hasAppliedCustomTraffic = false;
                lastAppliedMultiplier = multiplier;
                return;
            }

            if (!CanUseCustomerEntries())
            {
                BigHaxLogger.WarnOnce(context, "missing-customer-entries-hooks", "BigHax: customer traffic hooks are unavailable in this game build.");
                return;
            }

            if (forceRefresh || multiplier != lastAppliedMultiplier || !IsCurrentStateApplied(multiplier))
                RebuildAndApplyTraffic(context, multiplier);
        }

        public void RestoreOriginalState(ModContext? context)
        {
            RestoreVanillaTraffic(context);
            hasAppliedCustomTraffic = false;
            lastAppliedMultiplier = BigHaxSettings.DefaultCustomerTrafficMultiplier;
        }

        private void RebuildAndApplyTraffic(ModContext? context, int multiplier)
        {
            UpdateAllCustomerEntries();

            lastAppliedEntryCounts.Clear();
            lastAppliedCustomerCapacities.Clear();

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                if (!originalCustomerCapacities.ContainsKey(key))
                    originalCustomerCapacities[key] = registration.customerCapacity;

                var baseCapacity = originalCustomerCapacities[key];
                var desiredCapacity = baseCapacity > 0
                    ? Mathf.Max(baseCapacity, Mathf.CeilToInt(baseCapacity * multiplier))
                    : baseCapacity;
                registration.customerCapacity = desiredCapacity;
                lastAppliedCustomerCapacities[key] = desiredCapacity;

                if (!ShouldEntriesBeCreated(registration))
                {
                    lastAppliedEntryCounts[key] = 0;
                    continue;
                }

                var entries = GetBusinessCustomerEntries(registration);
                if (entries == null || entries.Count == 0)
                {
                    lastAppliedEntryCounts[key] = 0;
                    continue;
                }

                MultiplyEntries(entries, multiplier);
                lastAppliedEntryCounts[key] = entries.Count;
            }

            hasAppliedCustomTraffic = true;
            lastAppliedMultiplier = multiplier;
            BigHaxLogger.Info(context, $"BigHax: applied customer traffic multiplier x{multiplier} to player businesses.");
        }

        private void RestoreVanillaTraffic(ModContext? context)
        {
            UpdateAllCustomerEntries();

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                if (originalCustomerCapacities.TryGetValue(key, out var originalCapacity))
                    registration.customerCapacity = originalCapacity;
            }

            lastAppliedEntryCounts.Clear();
            lastAppliedCustomerCapacities.Clear();
            BigHaxLogger.Info(context, "BigHax: restored vanilla customer traffic for player businesses.");
        }

        private bool IsCurrentStateApplied(int multiplier)
        {
            if (!hasAppliedCustomTraffic || multiplier != lastAppliedMultiplier)
                return false;

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                if (!lastAppliedCustomerCapacities.TryGetValue(key, out var expectedCapacity) ||
                    registration.customerCapacity != expectedCapacity)
                {
                    return false;
                }

                var entries = GetBusinessCustomerEntries(registration);
                var currentCount = entries?.Count ?? 0;
                if (!lastAppliedEntryCounts.TryGetValue(key, out var expectedCount) || currentCount != expectedCount)
                    return false;
            }

            return true;
        }

        private static bool CanUseCustomerEntries()
        {
            return UpdateAllCustomerEntriesMethod != null &&
                   GetBusinessCustomerEntriesMethod != null &&
                   CustomerEntrySpawnTimeField != null &&
                   CustomerEntryCompletedField != null &&
                   CustomerEntryOrderField != null &&
                   OrderEntriesField != null &&
                   OrderCompletedField != null &&
                   OrderCustomerDemandTypesField != null &&
                   TimestampDayField != null &&
                   TimestampHourField != null &&
                   TimestampMinuteField != null &&
                   TimestampType != null;
        }

        private static void MultiplyEntries(IList entries, int multiplier)
        {
            var originalEntries = new object[entries.Count];
            for (var i = 0; i < entries.Count; i++)
                originalEntries[i] = entries[i]!;

            for (var duplicateIndex = 1; duplicateIndex < multiplier; duplicateIndex++)
            {
                foreach (var sourceEntry in originalEntries)
                {
                    var clone = CloneCustomerEntry(sourceEntry, duplicateIndex);
                    if (clone != null)
                        entries.Add(clone);
                }
            }
        }

        private static object? CloneCustomerEntry(object sourceEntry, int duplicateIndex)
        {
            var clone = MemberwiseCloneMethod.Invoke(sourceEntry, null);
            if (clone == null || CustomerEntrySpawnTimeField == null || CustomerEntryOrderField == null)
                return null;

            var sourceTimestamp = CustomerEntrySpawnTimeField.GetValue(sourceEntry);
            if (sourceTimestamp != null)
                CustomerEntrySpawnTimeField.SetValue(clone, CloneTimestamp(sourceTimestamp, duplicateIndex * 0.1f));

            CustomerEntryCompletedField?.SetValue(clone, false);

            var sourceOrder = CustomerEntryOrderField.GetValue(sourceEntry);
            if (sourceOrder != null)
                CustomerEntryOrderField.SetValue(clone, CloneOrder(sourceOrder));

            return clone;
        }

        private static object? CloneOrder(object sourceOrder)
        {
            var clone = MemberwiseCloneMethod.Invoke(sourceOrder, null);
            if (clone == null)
                return null;

            OrderCompletedField?.SetValue(clone, false);

            if (OrderEntriesField?.GetValue(sourceOrder) is IList sourceEntries)
            {
                var clonedEntries = CreateEmptyList(OrderEntriesField.FieldType);
                foreach (var sourceEntry in sourceEntries)
                {
                    if (sourceEntry != null)
                        clonedEntries.Add(MemberwiseCloneMethod.Invoke(sourceEntry, null));
                }

                OrderEntriesField.SetValue(clone, clonedEntries);
            }

            if (OrderCustomerDemandTypesField?.GetValue(sourceOrder) is IList sourceDemandTypes)
            {
                var clonedDemandTypes = CreateEmptyList(OrderCustomerDemandTypesField.FieldType);
                foreach (var demandType in sourceDemandTypes)
                    clonedDemandTypes.Add(demandType);

                OrderCustomerDemandTypesField.SetValue(clone, clonedDemandTypes);
            }

            return clone;
        }

        private static object CloneTimestamp(object sourceTimestamp, float minuteOffset)
        {
            var clone = Activator.CreateInstance(TimestampType!);
            var day = (int)(TimestampDayField!.GetValue(sourceTimestamp) ?? 0);
            var hour = (int)(TimestampHourField!.GetValue(sourceTimestamp) ?? 0);
            var minute = Convert.ToSingle(TimestampMinuteField!.GetValue(sourceTimestamp) ?? 0f);

            TimestampDayField.SetValue(clone, day);
            TimestampHourField.SetValue(clone, hour);
            TimestampMinuteField.SetValue(clone, Mathf.Min(59.9f, minute + minuteOffset));
            return clone!;
        }

        private static IList CreateEmptyList(Type listType)
        {
            return (IList)Activator.CreateInstance(listType)!;
        }

        private static List<BuildingRegistration> GetPlayerBusinessRegistrations()
        {
            var registrations = new List<BuildingRegistration>();
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null)
                return registrations;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null ||
                    !registration.RentedByPlayer ||
                    string.IsNullOrWhiteSpace(registration.businessTypeName))
                {
                    continue;
                }

                registrations.Add(registration);
            }

            return registrations;
        }

        private static bool ShouldEntriesBeCreated(BuildingRegistration registration)
        {
            if (ShouldEntriesBeCreatedMethod == null)
                return true;

            try
            {
                return (bool)(ShouldEntriesBeCreatedMethod.Invoke(null, new object[] { registration }) ?? false);
            }
            catch
            {
                return true;
            }
        }

        private static IList? GetBusinessCustomerEntries(BuildingRegistration registration)
        {
            if (GetBusinessCustomerEntriesMethod == null)
                return null;

            try
            {
                return GetBusinessCustomerEntriesMethod.Invoke(null, new object[] { registration }) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static void UpdateAllCustomerEntries()
        {
            UpdateAllCustomerEntriesMethod?.Invoke(null, null);
        }

        private static string GetRegistrationKey(BuildingRegistration registration)
        {
            return registration.StreetName + "|" + registration.StreetNumber;
        }

        private static Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var exact = assembly.GetType(typeName, false);
                if (exact != null)
                    return exact;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = Array.FindAll(exception.Types, type => type != null)!;
                }

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    if (type.FullName == typeName ||
                        type.Name == typeName ||
                        (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false))
                    {
                        return type;
                    }
                }
            }

            return null;
        }
    }
}
