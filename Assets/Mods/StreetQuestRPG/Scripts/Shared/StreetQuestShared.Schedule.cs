using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private static readonly Dictionary<string, BuildingRegistration> CachedNearestScheduleBuildingByCharacterId =
            new Dictionary<string, BuildingRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> LastKnownScheduleVisibilityByCharacterId =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static bool IsScheduleActive(StreetQuestCharacterDefinition character)
        {
            if (character == null)
                return false;

            return IsScheduleActive(character.schedule, character);
        }

        internal static bool TryGetCurrentGameHourKey(out int hourKey)
        {
            hourKey = 0;
            if (!TryGetCurrentGameTime(out var day, out var hour, out _))
                return false;

            hourKey = (day * 24) + hour;
            return true;
        }

        internal static void ClearScheduleCaches()
        {
            CachedNearestScheduleBuildingByCharacterId.Clear();
            LastKnownScheduleVisibilityByCharacterId.Clear();
        }

        internal static void UpdateScheduleVisibilitySnapshot()
        {
            LastKnownScheduleVisibilityByCharacterId.Clear();
            foreach (var character in StreetQuestCharacterCatalog.All)
            {
                if (character == null || string.IsNullOrWhiteSpace(character.id) || character.schedule == null)
                    continue;

                var visible = IsScheduleActive(character);
                LastKnownScheduleVisibilityByCharacterId[character.id] = visible;
                LogSchedule($"snapshot character={character.id} mode={character.schedule.Mode} visible={visible}");
            }
        }

        internal static bool RefreshSchedulesIfVisibilityChanged()
        {
            var stopwatch = Stopwatch.StartNew();
            var anyChanged = false;
            var anyVisibleNow = false;
            var changes = new List<string>();
            foreach (var character in StreetQuestCharacterCatalog.All)
            {
                if (character == null || string.IsNullOrWhiteSpace(character.id) || character.schedule == null)
                    continue;

                var visibleNow = IsScheduleActive(character);
                if (!LastKnownScheduleVisibilityByCharacterId.TryGetValue(character.id, out var visibleBefore))
                {
                    LastKnownScheduleVisibilityByCharacterId[character.id] = visibleNow;
                    continue;
                }

                if (visibleBefore == visibleNow)
                    continue;

                LastKnownScheduleVisibilityByCharacterId[character.id] = visibleNow;
                if (visibleNow)
                {
                    EnsureSpawnedCharacter(character);
                    anyVisibleNow = true;
                }
                else
                {
                    DestroySpawnedCharacter(character.id);
                }

                changes.Add($"{character.id}:{visibleBefore}->{visibleNow}");
                anyChanged = true;
            }

            stopwatch.Stop();
            if (!anyChanged)
            {
                LogSchedule($"hourly refresh no-op elapsedMs={stopwatch.ElapsedMilliseconds}");
                return false;
            }

            if (anyVisibleNow)
                EnsureQuestGiverCtaBehaviorInstalled();

            LogSchedule($"hourly refresh changed={string.Join(", ", changes)} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return true;
        }

        private static bool IsScheduleActive(
            StreetQuestCharacterScheduleDefinition schedule,
            StreetQuestCharacterDefinition character)
        {
            if (schedule == null)
                return true;

            switch (schedule.Mode)
            {
                case StreetQuestCharacterScheduleMode.Always:
                    return true;
                case StreetQuestCharacterScheduleMode.DailyWindow:
                    var withinWindow = TryIsWithinDailyWindow(schedule.startHour, schedule.endHour);
                    LogSchedule(
                        $"evaluate character={character?.id ?? "<null>"} mode=DailyWindow start={schedule.startHour} end={schedule.endHour} visible={withinWindow}");
                    return withinWindow;
                case StreetQuestCharacterScheduleMode.BuildingOpenStatus:
                    var foundByAddress = TryGetBuildingScheduleStatus(schedule, character, out var isOpenByAddress);
                    LogSchedule(
                        $"evaluate character={character?.id ?? "<null>"} mode=BuildingOpenStatus found={foundByAddress} visible={isOpenByAddress} address={schedule.address ?? "<null>"}");
                    return foundByAddress && isOpenByAddress;
                case StreetQuestCharacterScheduleMode.NearestBuildingOpenStatus:
                    var foundByNearest = TryGetNearestBuildingScheduleStatus(schedule, character, out var isOpenByNearest);
                    LogSchedule(
                        $"evaluate character={character?.id ?? "<null>"} mode=NearestBuildingOpenStatus found={foundByNearest} visible={isOpenByNearest} maxDistance={schedule.nearestBuildingMaxDistance:F1}");
                    return foundByNearest && isOpenByNearest;
                default:
                    return true;
            }
        }

        private static bool TryIsWithinDailyWindow(int startHour, int endHour)
        {
            if (!TryGetCurrentGameTime(out _, out var hour, out _))
                return true;

            startHour = Mathf.Clamp(startHour, 0, 23);
            endHour = Mathf.Clamp(endHour, 0, 24);

            if (startHour == endHour)
                return true;

            if (startHour < endHour)
                return hour >= startHour && hour < endHour;

            return hour >= startHour || hour < endHour;
        }

        private static bool TryGetBuildingScheduleStatus(
            StreetQuestCharacterScheduleDefinition schedule,
            StreetQuestCharacterDefinition character,
            out bool isOpen)
        {
            isOpen = false;
            if (schedule == null)
                return false;

            var registration = ResolveBuildingRegistration(schedule, character);
            if (registration == null)
                return false;

            isOpen = IsBuildingRegistrationOpen(registration);
            return true;
        }

        private static bool TryGetNearestBuildingScheduleStatus(
            StreetQuestCharacterScheduleDefinition schedule,
            StreetQuestCharacterDefinition character,
            out bool isOpen)
        {
            isOpen = false;
            if (character == null)
                return false;

            var registration = ResolveNearestBuildingRegistration(character, schedule?.nearestBuildingMaxDistance ?? 40f);
            if (registration == null)
                return false;

            isOpen = IsBuildingRegistrationOpen(registration);
            return true;
        }

        private static bool TryGetCurrentGameTime(out int day, out int hour, out float minute)
        {
            day = 0;
            hour = 0;
            minute = 0f;

            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return false;

            day = saveGame.Day;
            hour = saveGame.Hour;
            minute = saveGame.Minute;
            return true;
        }

        private static BuildingRegistration ResolveBuildingRegistration(
            StreetQuestCharacterScheduleDefinition schedule,
            StreetQuestCharacterDefinition character)
        {
            if (!string.IsNullOrWhiteSpace(schedule?.address))
            {
                try
                {
                    var address = BuildingHelper.ParseAddressString(schedule.address);
                    if (address != null)
                        return BuildingHelper.GetBuildingRegistration(address);
                }
                catch
                {
                }
            }

            return ResolveNearestBuildingRegistration(character, schedule?.nearestBuildingMaxDistance ?? 40f);
        }

        private static BuildingRegistration ResolveNearestBuildingRegistration(
            StreetQuestCharacterDefinition character,
            float maxDistance)
        {
            if (character == null)
                return null;

            if (!string.IsNullOrWhiteSpace(character.id) &&
                CachedNearestScheduleBuildingByCharacterId.TryGetValue(character.id, out var cachedRegistration) &&
                cachedRegistration != null)
            {
                return cachedRegistration;
            }

            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null || saveGame.BuildingRegistrations.Count == 0)
                return null;

            var targetPosition = character.PositionOr(Vector3.zero);
            var bestDistanceSquared = Mathf.Max(1f, maxDistance) * Mathf.Max(1f, maxDistance);
            BuildingRegistration bestRegistration = null;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null || !registration.HasValidAddress)
                    continue;

                var building = BuildingHelper.GetBuilding(registration.Address);
                if (!TryResolveWorldPosition(building, out var registrationPosition) &&
                    !TryResolveWorldPosition(registration.Address, out registrationPosition) &&
                    !TryResolveWorldPosition(registration, out registrationPosition))
                {
                    continue;
                }

                var distanceSquared = (registrationPosition - targetPosition).sqrMagnitude;
                if (distanceSquared > bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestRegistration = registration;
            }

            if (!string.IsNullOrWhiteSpace(character.id) && bestRegistration != null)
                CachedNearestScheduleBuildingByCharacterId[character.id] = bestRegistration;

            return bestRegistration;
        }

        private static bool IsBuildingRegistrationOpen(BuildingRegistration registration)
        {
            if (registration == null)
                return false;

            try
            {
                if (TryGetCurrentGameTime(out _, out var hour, out _) &&
                    registration.OpensWithinHours(hour))
                {
                    return true;
                }

                object openStatus = registration.GetOpenStatus();
                if (openStatus is bool boolStatus)
                    return boolStatus;

                var statusText = openStatus?.ToString();
                if (string.IsNullOrWhiteSpace(statusText))
                    return false;

                return statusText.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 &&
                       statusText.IndexOf("closed", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadMemberValue(object instance, string memberName, out object value)
        {
            value = null;
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var type = instance.GetType();
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

        private static bool TryResolveWorldPosition(object candidate, out Vector3 worldPosition)
        {
            worldPosition = default;
            return TryResolveDirectWorldPosition(candidate, out worldPosition) ||
                   TryResolveWorldPositionRecursive(candidate, new HashSet<object>(), 0, out worldPosition);
        }

        private static bool TryResolveWorldPositionRecursive(
            object candidate,
            HashSet<object> visited,
            int depth,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (candidate == null || depth > 2 || !visited.Add(candidate))
                return false;

            foreach (var field in candidate.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try
                {
                    value = field.GetValue(candidate);
                }
                catch
                {
                    continue;
                }

                if (TryResolveDirectWorldPosition(value, out worldPosition) ||
                    TryResolveWorldPositionRecursive(value, visited, depth + 1, out worldPosition))
                {
                    return true;
                }
            }

            foreach (var property in candidate.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
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

                if (TryResolveDirectWorldPosition(value, out worldPosition) ||
                    TryResolveWorldPositionRecursive(value, visited, depth + 1, out worldPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveDirectWorldPosition(object candidate, out Vector3 worldPosition)
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

            if (!TryReadMemberValue(candidate, "position", out var positionValue))
                return false;

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
                default:
                    return false;
            }
        }
    }
}
