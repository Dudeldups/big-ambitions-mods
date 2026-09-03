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
        private const float AdditionalPromotionBoostPerMultiplierStep = 0.25f;
        private const float PendingScheduleRefreshIntervalSeconds = 10f;

        private static readonly MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly Type? CustomerEntriesHelperType = FindType("AI.Customers.CustomerEntries.CustomerEntriesHelper");
        private static readonly MethodInfo? UpdateAllCustomerEntriesMethod =
            CustomerEntriesHelperType?.GetMethod("UpdateCustomerEntriesForAllPlayerBusinesses", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? GetEntriesByAddressMethod =
            CustomerEntriesHelperType?.GetMethod("GetEntriesByAddress", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
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
        private readonly Dictionary<string, bool> lastKnownShouldCreateEntries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> lastKnownBusinessTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingScheduleBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool hasAppliedCustomTraffic;
        private float? originalBaseCustomerPromotionMultiplier;
        private float lastAppliedMultiplier = 1f;
        private float nextPendingScheduleRefreshAt;

        public void InvalidateCache()
        {
            // New player businesses are detected by address and business type on
            // every lightweight traffic health check. Scene loads therefore do
            // not need to invalidate already-applied customer schedules.
        }

        public void ApplyConfiguredTraffic(ModContext context, BigHaxSettings settings, bool forceRefresh)
        {
            var multiplier = Mathf.Max(1f, settings.CustomerTrafficMultiplier);

            if (multiplier <= 1f)
            {
                if (hasAppliedCustomTraffic)
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

            // During the mod's load callback, Big Ambitions 1.0 may have loaded
            // the save data but not initialized TimeHelper yet. Calling the game's
            // customer refresh at that stage throws and used to permanently disable
            // this service before the player could use the option.
            if (SaveGameManager.Current?.gameVariables == null)
                return;

            if (!hasAppliedCustomTraffic || !Mathf.Approximately(multiplier, lastAppliedMultiplier))
            {
                RebuildAndApplyTraffic(context, multiplier);
                return;
            }

            ApplyPromotionBoost(multiplier);
            DiscoverActivatedBusinesses(context, multiplier);
            TryApplyPendingBusinessSchedules(context, multiplier);
        }

        public void RestoreOriginalState(ModContext? context)
        {
            // The game disposes customer-entry dictionaries before unloading mods. Rebuilding
            // entries at this point calls into already-cleared game state and throws in 1.0.
            RestoreVanillaTraffic(context, refreshEntries: false);
            hasAppliedCustomTraffic = false;
            lastAppliedMultiplier = 1f;
        }

        private void RebuildAndApplyTraffic(ModContext? context, float multiplier)
        {
            if (!TryUpdateAllCustomerEntries(context))
                return;

            ApplyPromotionBoost(multiplier);

            lastAppliedEntryCounts.Clear();
            lastAppliedCustomerCapacities.Clear();
            lastKnownShouldCreateEntries.Clear();
            lastKnownBusinessTypes.Clear();
            pendingScheduleBusinessKeys.Clear();
            nextPendingScheduleRefreshAt = 0f;

            var appliedBusinessCount = 0;
            var waitingForEntriesCount = 0;
            var clonedBusinessCount = 0;

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                lastKnownBusinessTypes[key] = GetBusinessType(registration);
                var isNewBusiness = !originalCustomerCapacities.ContainsKey(key);
                if (isNewBusiness)
                    originalCustomerCapacities[key] = registration.customerCapacity;

                var baseCapacity = originalCustomerCapacities[key];
                var desiredCapacity = baseCapacity > 0
                    ? Mathf.Max(baseCapacity, Mathf.CeilToInt(baseCapacity * multiplier))
                    : baseCapacity;
                registration.customerCapacity = desiredCapacity;
                lastAppliedCustomerCapacities[key] = desiredCapacity;
                appliedBusinessCount++;

                var shouldCreateEntries = ShouldEntriesBeCreated(context, registration);
                lastKnownShouldCreateEntries[key] = shouldCreateEntries;
                if (!shouldCreateEntries)
                {
                    lastAppliedEntryCounts[key] = 0;
                    waitingForEntriesCount++;
                    continue;
                }

                var entries = GetBusinessCustomerEntries(context, registration, "after refresh");
                if (entries == null || entries.Count == 0)
                {
                    lastAppliedEntryCounts[key] = 0;
                    waitingForEntriesCount++;
                    pendingScheduleBusinessKeys.Add(key);
                    continue;
                }

                var originalEntryCount = entries.Count;
                MultiplyEntries(entries, multiplier);
                lastAppliedEntryCounts[key] = entries.Count;
                pendingScheduleBusinessKeys.Remove(key);
                if (entries.Count > originalEntryCount)
                    clonedBusinessCount++;

            }

            hasAppliedCustomTraffic = true;
            lastAppliedMultiplier = multiplier;
            BigHaxLogger.Info(
                context,
                $"BigHax: applied customer traffic multiplier x{multiplier} to player businesses. businesses={appliedBusinessCount}, waitingForEntries={waitingForEntriesCount}, clonedEntryBusinesses={clonedBusinessCount}.");
        }

        private void RestoreVanillaTraffic(ModContext? context, bool refreshEntries = true)
        {
            RestorePromotionBoost();
            if (refreshEntries)
                TryUpdateAllCustomerEntries(context);

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                if (originalCustomerCapacities.TryGetValue(key, out var originalCapacity))
                    registration.customerCapacity = originalCapacity;
            }

            lastAppliedEntryCounts.Clear();
            lastAppliedCustomerCapacities.Clear();
            lastKnownShouldCreateEntries.Clear();
            lastKnownBusinessTypes.Clear();
            pendingScheduleBusinessKeys.Clear();
            BigHaxLogger.Info(context, "BigHax: restored vanilla customer traffic for player businesses.");
        }

        private void DiscoverActivatedBusinesses(ModContext? context, float multiplier)
        {
            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                var businessType = GetBusinessType(registration);
                var isNewRegistration = !lastKnownBusinessTypes.TryGetValue(key, out var previousBusinessType);
                if (!isNewRegistration && string.Equals(previousBusinessType, businessType, StringComparison.Ordinal))
                    continue;

                lastKnownBusinessTypes[key] = businessType;
                ApplyCustomerCapacity(registration, key, multiplier);
                pendingScheduleBusinessKeys.Add(key);
                nextPendingScheduleRefreshAt = 0f;
            }

        }

        private void TryApplyPendingBusinessSchedules(ModContext? context, float multiplier)
        {
            if (pendingScheduleBusinessKeys.Count == 0)
                return;

            if (Time.unscaledTime < nextPendingScheduleRefreshAt)
                return;

            nextPendingScheduleRefreshAt = Time.unscaledTime + PendingScheduleRefreshIntervalSeconds;

            if (!TryUpdateAllCustomerEntries(context))
                return;

            var registrationsByKey = new Dictionary<string, BuildingRegistration>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in GetPlayerBusinessRegistrations())
                registrationsByKey[GetRegistrationKey(registration)] = registration;

            var resolvedKeys = new List<string>();
            foreach (var key in pendingScheduleBusinessKeys)
            {
                if (!registrationsByKey.TryGetValue(key, out var registration))
                {
                    resolvedKeys.Add(key);
                    continue;
                }

                ApplyCustomerCapacity(registration, key, multiplier);
                if (!ShouldEntriesBeCreated(context, registration))
                {
                    continue;
                }

                var entries = GetBusinessCustomerEntries(context, registration, "pending schedule refresh");
                if (entries == null || entries.Count == 0)
                {
                    continue;
                }

                var originalEntryCount = entries.Count;
                MultiplyEntries(entries, multiplier);
                lastAppliedEntryCounts[key] = entries.Count;
                lastKnownShouldCreateEntries[key] = true;
                resolvedKeys.Add(key);
            }

            foreach (var key in resolvedKeys)
                pendingScheduleBusinessKeys.Remove(key);
        }

        private void ApplyCustomerCapacity(BuildingRegistration registration, string key, float multiplier)
        {
            if (!originalCustomerCapacities.TryGetValue(key, out var baseCapacity))
            {
                baseCapacity = registration.customerCapacity;
                originalCustomerCapacities[key] = baseCapacity;
            }

            var desiredCapacity = baseCapacity > 0
                ? Mathf.Max(baseCapacity, Mathf.CeilToInt(baseCapacity * multiplier))
                : baseCapacity;
            registration.customerCapacity = desiredCapacity;
            lastAppliedCustomerCapacities[key] = desiredCapacity;
        }

        private static string GetBusinessType(BuildingRegistration registration)
        {
            return registration.businessTypeName ?? string.Empty;
        }

        private void ApplyPromotionBoost(float multiplier)
        {
            var gameVariables = SaveGameManager.Current?.gameVariables;
            if (gameVariables == null)
                return;

            if (!originalBaseCustomerPromotionMultiplier.HasValue)
                originalBaseCustomerPromotionMultiplier = gameVariables.baseCustomerPromotionMultiplier;

            var targetMultiplier = 1f + (multiplier - 1f) * AdditionalPromotionBoostPerMultiplierStep;
            var targetValue = originalBaseCustomerPromotionMultiplier.Value * targetMultiplier;

            if (!Mathf.Approximately(gameVariables.baseCustomerPromotionMultiplier, targetValue))
                gameVariables.baseCustomerPromotionMultiplier = targetValue;
        }

        private void RestorePromotionBoost()
        {
            var gameVariables = SaveGameManager.Current?.gameVariables;
            if (gameVariables == null || !originalBaseCustomerPromotionMultiplier.HasValue)
                return;

            gameVariables.baseCustomerPromotionMultiplier = originalBaseCustomerPromotionMultiplier.Value;
        }

        private static bool CanUseCustomerEntries()
        {
            return UpdateAllCustomerEntriesMethod != null &&
                   GetEntriesByAddressMethod != null &&
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

        private static void MultiplyEntries(IList entries, float multiplier)
        {
            if (multiplier <= 1f || entries.Count == 0)
                return;

            var originalEntries = new object[entries.Count];
            for (var i = 0; i < entries.Count; i++)
                originalEntries[i] = entries[i]!;

            var wholeCopies = Mathf.FloorToInt(multiplier) - 1;
            for (var duplicateIndex = 1; duplicateIndex <= wholeCopies; duplicateIndex++)
            {
                foreach (var sourceEntry in originalEntries)
                {
                    var clone = CloneCustomerEntry(sourceEntry, duplicateIndex);
                    if (clone != null)
                        entries.Add(clone);
                }
            }

            var targetCount = Mathf.CeilToInt(originalEntries.Length * multiplier);
            var partialClonesNeeded = Mathf.Max(0, targetCount - entries.Count);
            for (var index = 0; index < partialClonesNeeded && index < originalEntries.Length; index++)
            {
                var clone = CloneCustomerEntry(originalEntries[index], wholeCopies + 1);
                if (clone != null)
                    entries.Add(clone);
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

        private static bool ShouldEntriesBeCreated(ModContext? context, BuildingRegistration registration)
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

        private static IList? GetBusinessCustomerEntries(ModContext? context, BuildingRegistration registration, string phase)
        {
            if (GetEntriesByAddressMethod == null)
                return null;

            try
            {
                return GetEntriesByAddressMethod.Invoke(null, new object[] { registration.Address }) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryUpdateAllCustomerEntries(ModContext? context)
        {
            try
            {
                UpdateAllCustomerEntriesMethod?.Invoke(null, null);
                return true;
            }
            catch
            {
                return false;
            }
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
