using System;
using System.Collections.Generic;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private static readonly Dictionary<string, Vector3> CachedExteriorAddressAnchorsByAddress =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private static string _cachedExteriorAddressAnchorSignature = string.Empty;

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

            if (TryResolveWorldPositionFromSceneAddressAnchorCache(buildingAddressKey, out worldPosition))
            {
                source = "scene_address_anchor_cache";
                return true;
            }

            if (StreetQuestIndoorAddressTracker.TryGetExteriorAddressAnchor(buildingAddressKey, out worldPosition))
            {
                source = "exterior_candidate_anchor";
                return true;
            }

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

        private static bool TryResolveWorldPositionFromSceneAddressAnchorCache(
            string buildingAddressKey,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            EnsureSceneAddressAnchorCacheBuilt();
            var normalizedKey = NormalizeAddressText(buildingAddressKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return false;

            return CachedExteriorAddressAnchorsByAddress.TryGetValue(normalizedKey, out worldPosition);
        }

        private static void EnsureSceneAddressAnchorCacheBuilt()
        {
            var signature = BuildSceneAddressAnchorSignature();
            if (string.Equals(_cachedExteriorAddressAnchorSignature, signature, StringComparison.Ordinal) &&
                CachedExteriorAddressAnchorsByAddress.Count > 0)
            {
                return;
            }

            CachedExteriorAddressAnchorsByAddress.Clear();
            _cachedExteriorAddressAnchorSignature = signature;

            foreach (var collider in Resources.FindObjectsOfTypeAll<Collider>())
            {
                if (collider == null || collider.transform == null)
                    continue;

                if (!TryResolveAddressFromExteriorTransformChainForAnchorCache(collider.transform, out var addressKey))
                    continue;

                var normalizedKey = NormalizeAddressText(addressKey);
                if (string.IsNullOrWhiteSpace(normalizedKey))
                    continue;

                var candidatePosition = ResolveExteriorAnchorPosition(collider);
                var candidateScore = ScoreExteriorAnchorCandidate(collider.transform);
                if (CachedExteriorAddressAnchorsByAddress.TryGetValue(normalizedKey, out var existingPosition))
                {
                    var existingScore = ScoreCachedExteriorAnchor(existingPosition, normalizedKey);
                    if (existingScore >= candidateScore)
                        continue;
                }

                CachedExteriorAddressAnchorsByAddress[normalizedKey] = candidatePosition;
            }
        }

        private static string BuildSceneAddressAnchorSignature()
        {
            var scene = SceneManager.GetActiveScene();
            var buildingRegistrationCount = SaveGameManager.Current?.BuildingRegistrations?.Count ?? 0;
            return $"{scene.name}|{scene.buildIndex}|{buildingRegistrationCount}";
        }

        private static bool TryResolveAddressFromExteriorTransformChainForAnchorCache(
            Transform transform,
            out string addressKey)
        {
            addressKey = string.Empty;
            var current = transform;
            var depth = 0;
            while (current != null && depth < 8)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    var componentTypeName = component.GetType().Name;
                    if (!string.Equals(componentTypeName, "ViewBlockingEntityPart", StringComparison.Ordinal) &&
                        !string.Equals(component.GetType().FullName, "Entities.ViewBlockingEntityPart", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var cityBuildingController = GetMemberValueForAnchorCache(component, "cityBuildingController");
                    if (cityBuildingController == null)
                        continue;

                    var buildingRegistration = GetMemberValueForAnchorCache(cityBuildingController, "buildingRegistration");
                    if (buildingRegistration == null)
                        continue;

                    var address = GetMemberValueForAnchorCache(buildingRegistration, "Address");
                    if (TryNormalizeAddressForAnchorCache(address, out addressKey))
                        return true;
                }

                current = current.parent;
                depth++;
            }

            return false;
        }

        private static Vector3 ResolveExteriorAnchorPosition(Collider collider)
        {
            if (collider == null)
                return Vector3.zero;

            var bounds = collider.bounds;
            var position = bounds.center;
            position.y = collider.transform.position.y;
            return position;
        }

        private static int ScoreExteriorAnchorCandidate(Transform transform)
        {
            if (transform == null)
                return 0;

            var path = transform.name ?? string.Empty;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            if (path.IndexOf("GroundPlane", StringComparison.OrdinalIgnoreCase) >= 0)
                return 300;
            if (path.IndexOf("Entrance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("Entry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 200;
            }

            return 100;
        }

        private static int ScoreCachedExteriorAnchor(Vector3 existingPosition, string addressKey)
        {
            return CachedExteriorAddressAnchorsByAddress.ContainsKey(addressKey) ? 100 : 0;
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

        private static object GetMemberValueForAnchorCache(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            var type = instance.GetType();
            while (type != null)
            {
                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    try
                    {
                        return property.GetValue(instance, null);
                    }
                    catch
                    {
                    }
                }

                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(instance);
                    }
                    catch
                    {
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        private static bool TryNormalizeAddressForAnchorCache(object addressValue, out string addressKey)
        {
            addressKey = string.Empty;
            switch (addressValue)
            {
                case null:
                    return false;
                case Address address:
                    addressKey = NormalizeAddressText(address);
                    return !string.IsNullOrWhiteSpace(addressKey);
                case string text:
                    addressKey = NormalizeAddressText(text);
                    return !string.IsNullOrWhiteSpace(addressKey);
                default:
                    addressKey = NormalizeAddressText(addressValue);
                    return !string.IsNullOrWhiteSpace(addressKey);
            }
        }
    }
}
