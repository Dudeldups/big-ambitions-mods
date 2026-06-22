using System;
using System.Collections.Generic;
using System.Reflection;
using Buildings;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const float IndoorBuildingProbeRadius = 18f;
        private const int IndoorBuildingProbeMaxColliders = 24;

        private static string _currentIndoorBuildingAddressKey = string.Empty;
        private static Type _cachedCinemachineBrainType;

        internal static void ResetIndoorBuildingContext()
        {
            _currentIndoorBuildingAddressKey = string.Empty;
        }

        internal static string GetCurrentIndoorBuildingAddressKey()
        {
            return _currentIndoorBuildingAddressKey ?? string.Empty;
        }

        internal static bool SetCurrentIndoorBuildingAddressKey(string addressKey)
        {
            var normalized = NormalizeAddressKey(addressKey);
            if (string.Equals(_currentIndoorBuildingAddressKey, normalized, StringComparison.Ordinal))
                return false;

            _currentIndoorBuildingAddressKey = normalized;
            StreetQuestCharacterRuntimeResolver.ClearCache();
            return true;
        }

        internal static bool DoesBuildingContextMatch(StreetQuestCharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.buildingAddress))
                return true;

            var requiredAddress = NormalizeAddressKey(definition.buildingAddress);
            var currentAddress = GetCurrentIndoorBuildingAddressKey();
            if (string.IsNullOrWhiteSpace(currentAddress))
                return false;

            return string.Equals(requiredAddress, currentAddress, StringComparison.Ordinal);
        }

        internal static bool IsIndoorGameplayContextActive()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            if (liveVirtualCamera == null)
                return false;

            var path = GetHierarchyPath(liveVirtualCamera.transform);
            return path.IndexOf("IndoorCam", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   path.IndexOf("VehicleCam", StringComparison.OrdinalIgnoreCase) < 0 &&
                   path.IndexOf("IndoorVehicle", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static bool TryResolveCurrentIndoorBuildingAddress(out string addressKey)
        {
            addressKey = string.Empty;
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return false;

            var playerPosition = playerController.transform.position;
            var colliders = Physics.OverlapSphere(playerPosition, IndoorBuildingProbeRadius);
            if (colliders == null || colliders.Length == 0)
                return false;

            string bestAddress = null;
            var bestDistanceSquared = float.PositiveInfinity;
            var visitedTargets = new HashSet<Transform>();

            foreach (var collider in colliders)
            {
                if (collider?.transform == null || !visitedTargets.Add(collider.transform))
                    continue;

                if (!TryResolveAddressFromTransformChain(collider.transform, out var candidateAddress))
                    continue;

                var closestPoint = collider.ClosestPoint(playerPosition);
                var distanceSquared = (closestPoint - playerPosition).sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                bestAddress = candidateAddress;

                if (visitedTargets.Count >= IndoorBuildingProbeMaxColliders)
                    break;
            }

            addressKey = NormalizeAddressKey(bestAddress);
            return !string.IsNullOrWhiteSpace(addressKey);
        }

        private static bool TryResolveAddressFromTransformChain(Transform transform, out string addressKey)
        {
            addressKey = string.Empty;
            for (var current = transform; current != null; current = current.parent)
            {
                if (!TryResolveAddressFromObject(current.gameObject, out addressKey) &&
                    !TryResolveAddressFromObject(current, out addressKey))
                {
                    continue;
                }

                return !string.IsNullOrWhiteSpace(addressKey);
            }

            return false;
        }

        private static bool TryResolveAddressFromObject(object source, out string addressKey)
        {
            addressKey = string.Empty;
            if (source == null)
                return false;

            if (TryExtractAddressKey(source, out addressKey))
                return true;

            if (source is GameObject gameObject)
            {
                foreach (var component in gameObject.GetComponents<Component>())
                {
                    if (component != null && TryExtractAddressKey(component, out addressKey))
                        return true;
                }

                return false;
            }

            if (source is Component componentSource &&
                componentSource.gameObject != null &&
                !ReferenceEquals(componentSource.gameObject, source))
            {
                return TryResolveAddressFromObject(componentSource.gameObject, out addressKey);
            }

            return false;
        }

        private static bool TryExtractAddressKey(object source, out string addressKey)
        {
            addressKey = string.Empty;
            if (source == null)
                return false;

            if (source is Address address)
            {
                addressKey = NormalizeAddressKey(address.ToString());
                return !string.IsNullOrWhiteSpace(addressKey);
            }

            if (TryReadAddressMember(source, "Address", out addressKey))
                return true;

            if (TryReadNestedAddressMember(source, "buildingRegistration", out addressKey))
                return true;

            if (TryReadNestedAddressMember(source, "cityBuildingController", out addressKey))
                return true;

            return false;
        }

        private static bool TryReadNestedAddressMember(object source, string memberName, out string addressKey)
        {
            addressKey = string.Empty;
            var nested = GetMemberValue(source, memberName);
            return nested != null && TryExtractAddressKey(nested, out addressKey);
        }

        private static bool TryReadAddressMember(object source, string memberName, out string addressKey)
        {
            addressKey = string.Empty;
            var value = GetMemberValue(source, memberName);
            if (value == null)
                return false;

            switch (value)
            {
                case Address address:
                    addressKey = NormalizeAddressKey(address.ToString());
                    return !string.IsNullOrWhiteSpace(addressKey);
                case string text when !string.IsNullOrWhiteSpace(text):
                    addressKey = NormalizeAddressKey(text);
                    return !string.IsNullOrWhiteSpace(addressKey);
                default:
                    return false;
            }
        }

        private static Component GetLiveVirtualCameraComponent()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
                return null;

            _cachedCinemachineBrainType ??= FindType("Cinemachine.CinemachineBrain");
            if (_cachedCinemachineBrainType == null)
                return null;

            var brain = mainCamera.GetComponent(_cachedCinemachineBrainType);
            if (brain == null)
                return null;

            var activeVirtualCamera = GetMemberValue(brain, "ActiveVirtualCamera");
            if (activeVirtualCamera is Component component)
                return component;

            var virtualCameraGameObject = GetMemberValue(activeVirtualCamera, "VirtualCameraGameObject") as GameObject;
            return virtualCameraGameObject != null ? virtualCameraGameObject.GetComponent<Component>() : null;
        }

        private static string NormalizeAddressKey(string addressKey)
        {
            return string.IsNullOrWhiteSpace(addressKey)
                ? string.Empty
                : addressKey.Trim().ToLowerInvariant();
        }

        private static string GetBuildingContextHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);

            return string.Join("/", names);
        }
    }
}
