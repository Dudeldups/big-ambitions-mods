using System;
using System.Collections.Generic;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        internal static bool TryResolveAddressWorldAnchor(string buildingAddressKey, out Vector3 worldPosition)
        {
            return TryResolveAddressWorldAnchor(buildingAddressKey, out worldPosition, out _);
        }

        internal static bool TryResolveAddressWorldAnchor(string buildingAddressKey, out Vector3 worldPosition, out string source)
        {
            worldPosition = default;
            source = string.Empty;
            if (string.IsNullOrWhiteSpace(buildingAddressKey))
                return false;

            Address address;
            try
            {
                address = BuildingHelper.ParseAddressString(buildingAddressKey);
            }
            catch
            {
                return false;
            }

            if (address == null)
                return false;

            if (TryResolveWorldPositionFromSaveRegistration(buildingAddressKey, address, out worldPosition))
            {
                source = "save_registration";
                return true;
            }

            try
            {
                var building = BuildingHelper.GetBuilding(address);
                if (TryResolveWorldPositionFromCandidate(building, out worldPosition))
                {
                    source = "building";
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                var registration = BuildingHelper.GetBuildingRegistration(address);
                if (TryResolveWorldPositionFromCandidate(registration, out worldPosition))
                {
                    source = "registration";
                    return true;
                }

                if (registration?.Address != null &&
                    TryResolveWorldPositionFromCandidate(registration.Address, out worldPosition))
                {
                    source = "registration_address";
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryResolveWorldPositionFromSaveRegistration(
            string buildingAddressKey,
            Address parsedAddress,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null || saveGame.BuildingRegistrations.Count == 0)
                return false;

            var normalizedKey = NormalizeAddressText(buildingAddressKey);
            var parsedAddressText = NormalizeAddressText(parsedAddress);

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null || !registration.HasValidAddress || registration.Address == null)
                    continue;

                var registrationAddressText = NormalizeAddressText(registration.Address);
                if (!string.Equals(registrationAddressText, normalizedKey, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(registrationAddressText, parsedAddressText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveWorldPositionFromCandidate(registration, out worldPosition) ||
                    TryResolveWorldPositionFromCandidate(registration.Address, out worldPosition))
                {
                    return true;
                }

                try
                {
                    var building = BuildingHelper.GetBuilding(registration.Address);
                    if (TryResolveWorldPositionFromCandidate(building, out worldPosition))
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryResolveWorldPositionFromCandidate(object candidate, out Vector3 worldPosition)
        {
            worldPosition = default;
            return TryResolveDirectWorldPositionFromCandidate(candidate, out worldPosition) ||
                   TryResolveWorldPositionFromCandidateRecursive(candidate, new HashSet<object>(), 0, out worldPosition);
        }

        private static bool TryResolveWorldPositionFromCandidateRecursive(
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

                if (TryResolveDirectWorldPositionFromCandidate(value, out worldPosition) ||
                    TryResolveWorldPositionFromCandidateRecursive(value, visited, depth + 1, out worldPosition))
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

                if (TryResolveDirectWorldPositionFromCandidate(value, out worldPosition) ||
                    TryResolveWorldPositionFromCandidateRecursive(value, visited, depth + 1, out worldPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveDirectWorldPositionFromCandidate(object candidate, out Vector3 worldPosition)
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

            foreach (var memberName in new[] { "position", "Position", "worldPosition", "WorldPosition" })
            {
                if (!TryReadMemberValueForWorldAnchor(candidate, memberName, out var positionValue))
                    continue;

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

        private static bool TryReadMemberValueForWorldAnchor(object instance, string memberName, out object value)
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

        private static string NormalizeAddressText(object addressLike)
        {
            if (addressLike == null)
                return string.Empty;

            try
            {
                var text = addressLike.ToString();
                return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
