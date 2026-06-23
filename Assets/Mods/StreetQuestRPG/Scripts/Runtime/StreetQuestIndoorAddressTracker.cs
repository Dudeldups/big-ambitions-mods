using System;
using System.Reflection;
using Buildings;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static class StreetQuestIndoorAddressTracker
    {
        private const BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const float ExteriorCandidateRefreshIntervalSeconds = 0.75f;
        private const float ExteriorCandidateRecentWindowSeconds = 4f;
        private const float ExteriorProbeRadius = 10f;
        private const int ExteriorProbeMaxColliders = 24;
        private const int ExteriorProbeMaxParentDepth = 8;

        private static string _lastExteriorAddressCandidate = string.Empty;
        private static float _lastExteriorAddressCandidateTime = float.NegativeInfinity;
        private static readonly System.Collections.Generic.Dictionary<string, Vector3> ExteriorAddressAnchorsByAddress =
            new System.Collections.Generic.Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private static string _currentIndoorAddress = string.Empty;
        private static bool _wasIndoorLastTick;
        private static bool _hasTicked;
        private static bool _awaitingIndoorResumeFromSave;
        private static bool _wasCityMapOpenLastTick;
        private static float _nextExteriorCandidateRefreshAtSeconds;
        private static string _lastLoggedExteriorCandidate = string.Empty;
        private static string _lastLoggedIndoorTransitionCandidate = string.Empty;
        private static string _lastLoggedResolvedAddress = string.Empty;
        private static string _lastLoggedClearedReason = string.Empty;

        internal static string CurrentIndoorAddress => _currentIndoorAddress ?? string.Empty;

        internal static bool IsIndoors => _wasIndoorLastTick;

        internal static void Reset()
        {
            _lastExteriorAddressCandidate = string.Empty;
            _lastExteriorAddressCandidateTime = float.NegativeInfinity;
            ExteriorAddressAnchorsByAddress.Clear();
            _currentIndoorAddress = string.Empty;
            _wasIndoorLastTick = false;
            _hasTicked = false;
            _awaitingIndoorResumeFromSave = false;
            _wasCityMapOpenLastTick = false;
            _nextExteriorCandidateRefreshAtSeconds = 0f;
            _lastLoggedExteriorCandidate = string.Empty;
            _lastLoggedIndoorTransitionCandidate = string.Empty;
            _lastLoggedResolvedAddress = string.Empty;
            _lastLoggedClearedReason = string.Empty;
        }

        internal static bool Tick(float elapsedSeconds)
        {
            var isCityMapOpen = StreetQuestShared.IsCityMapOpenForGameplayChecks();
            if (isCityMapOpen)
            {
                _wasCityMapOpenLastTick = true;
                return false;
            }

            if (_wasCityMapOpenLastTick)
            {
                _wasCityMapOpenLastTick = false;
                return false;
            }

            var isIndoor = StreetQuestShared.IsIndoorGameplayContextActive();
            var shouldRefreshCharacters = false;

            if (!_hasTicked)
            {
                _hasTicked = true;

                if (TryRestorePersistedIndoorAddress(out var persistedAddress))
                {
                    _wasIndoorLastTick = true;
                    _awaitingIndoorResumeFromSave = !isIndoor;
                    shouldRefreshCharacters = SetCurrentIndoorAddress(persistedAddress);
                    LogIndoorAddressResolved(
                        persistedAddress,
                        _awaitingIndoorResumeFromSave ? "save_state_pending_indoor_resume" : "save_state");
                    return shouldRefreshCharacters;
                }

                _wasIndoorLastTick = isIndoor;

                if (!isIndoor)
                    UpdateExteriorCandidateIfDue(elapsedSeconds);

                return false;
            }

            if (_awaitingIndoorResumeFromSave)
            {
                if (!isIndoor)
                    return false;

                _awaitingIndoorResumeFromSave = false;
                _wasIndoorLastTick = true;
                _lastLoggedClearedReason = string.Empty;
                return false;
            }

            if (isIndoor != _wasIndoorLastTick)
            {
                _wasIndoorLastTick = isIndoor;
                if (isIndoor)
                {
                    _lastLoggedClearedReason = string.Empty;
                    var candidateAgeSeconds = GetCandidateAgeSeconds(elapsedSeconds);
                    LogIndoorTransitionDetected(_lastExteriorAddressCandidate, candidateAgeSeconds);
                    if (TryRestorePersistedIndoorAddress(out var persistedAddress))
                    {
                        shouldRefreshCharacters = SetCurrentIndoorAddress(persistedAddress);
                        LogIndoorAddressResolved(persistedAddress, "save_state");
                    }
                    else if (TryPromoteRecentExteriorCandidate(elapsedSeconds, out var resolvedAddress))
                    {
                        shouldRefreshCharacters = SetCurrentIndoorAddress(resolvedAddress);
                        LogIndoorAddressResolved(resolvedAddress, "last_exterior_candidate");
                    }
                    else
                    {
                        shouldRefreshCharacters = SetCurrentIndoorAddress(string.Empty);
                        _lastLoggedResolvedAddress = string.Empty;
                    }
                }
                else
                {
                    shouldRefreshCharacters = SetCurrentIndoorAddress(string.Empty);
                    StreetQuestShared.PersistIndoorBuildingAddressKey(string.Empty);
                    LogIndoorAddressCleared("returned_outdoor");
                    UpdateExteriorCandidateIfDue(elapsedSeconds, force: true);
                }
            }

            if (!isIndoor)
                UpdateExteriorCandidateIfDue(elapsedSeconds);

            return shouldRefreshCharacters;
        }

        internal static bool TryGetCurrentIndoorAddress(out string addressKey)
        {
            addressKey = CurrentIndoorAddress;
            return !string.IsNullOrWhiteSpace(addressKey);
        }

        internal static bool SetCurrentIndoorAddress(string addressKey)
        {
            var normalized = NormalizeAddressKey(addressKey);
            if (string.Equals(_currentIndoorAddress, normalized, StringComparison.Ordinal))
                return false;

            _currentIndoorAddress = normalized;
            StreetQuestShared.PersistIndoorBuildingAddressKey(normalized);
            StreetQuestCharacterRuntimeResolver.ClearCache();
            return true;
        }

        internal static bool TryGetExteriorAddressAnchor(string addressKey, out Vector3 worldPosition)
        {
            worldPosition = default;
            var normalized = NormalizeAddressKey(addressKey);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return ExteriorAddressAnchorsByAddress.TryGetValue(normalized, out worldPosition);
        }

        private static bool TryRestorePersistedIndoorAddress(out string addressKey)
        {
            addressKey = StreetQuestShared.GetPersistedIndoorBuildingAddressKey();
            return !string.IsNullOrWhiteSpace(addressKey);
        }

        private static void UpdateExteriorCandidateIfDue(float elapsedSeconds, bool force = false)
        {
            if (!force && elapsedSeconds < _nextExteriorCandidateRefreshAtSeconds)
                return;

            _nextExteriorCandidateRefreshAtSeconds = elapsedSeconds + ExteriorCandidateRefreshIntervalSeconds;
            if (!TryFindExteriorAddressCandidate(out var addressKey, out var sourceDescription, out var bestDistance, out var anchorPosition))
                return;

            ExteriorAddressAnchorsByAddress[addressKey] = anchorPosition;

            if (string.Equals(_lastExteriorAddressCandidate, addressKey, StringComparison.Ordinal))
            {
                _lastExteriorAddressCandidateTime = elapsedSeconds;
                return;
            }

            _lastExteriorAddressCandidate = addressKey;
            _lastExteriorAddressCandidateTime = elapsedSeconds;
            var playerPosition = PlayerHelper.PlayerController != null
                ? PlayerHelper.PlayerController.transform.position
                : Vector3.zero;
            LogExteriorAddressCandidateChanged(addressKey, sourceDescription, bestDistance, playerPosition);
        }

        private static bool TryPromoteRecentExteriorCandidate(float elapsedSeconds, out string addressKey)
        {
            addressKey = string.Empty;
            if (string.IsNullOrWhiteSpace(_lastExteriorAddressCandidate))
                return false;

            if (elapsedSeconds - _lastExteriorAddressCandidateTime > ExteriorCandidateRecentWindowSeconds)
                return false;

            addressKey = _lastExteriorAddressCandidate;
            return true;
        }

        private static bool TryFindExteriorAddressCandidate(
            out string addressKey,
            out string sourceDescription,
            out float bestDistance,
            out Vector3 anchorPosition)
        {
            addressKey = string.Empty;
            sourceDescription = string.Empty;
            bestDistance = float.PositiveInfinity;
            anchorPosition = default;
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return false;

            var playerPosition = playerController.transform.position;
            var colliders = Physics.OverlapSphere(playerPosition, ExteriorProbeRadius);
            if (colliders == null || colliders.Length == 0)
                return false;

            var bestDistanceSquared = float.PositiveInfinity;
            var bestAddress = string.Empty;
            var bestSourceDescription = string.Empty;
            var bestAnchorPosition = Vector3.zero;
            var visitedColliders = 0;

            foreach (var collider in colliders)
            {
                if (collider?.transform == null)
                    continue;

                visitedColliders++;
                if (!TryResolveAddressFromExteriorTransformChain(collider.transform, out var candidateAddress, out var candidateSourceDescription))
                {
                    if (visitedColliders >= ExteriorProbeMaxColliders)
                        break;

                    continue;
                }

                var closestPoint = collider.ClosestPoint(playerPosition);
                var distanceSquared = (closestPoint - playerPosition).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestAddress = candidateAddress;
                    bestSourceDescription = candidateSourceDescription;
                    bestAnchorPosition = closestPoint;
                }

                if (visitedColliders >= ExteriorProbeMaxColliders)
                    break;
            }

            addressKey = NormalizeAddressKey(bestAddress);
            sourceDescription = bestSourceDescription;
            bestDistance = float.IsPositiveInfinity(bestDistanceSquared) ? float.PositiveInfinity : Mathf.Sqrt(bestDistanceSquared);
            anchorPosition = bestAnchorPosition;
            return !string.IsNullOrWhiteSpace(addressKey);
        }

        private static bool TryResolveAddressFromExteriorTransformChain(Transform transform, out string addressKey, out string sourceDescription)
        {
            addressKey = string.Empty;
            sourceDescription = string.Empty;
            var current = transform;
            var depth = 0;
            while (current != null && depth < ExteriorProbeMaxParentDepth)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null || !IsViewBlockingEntityPart(component))
                        continue;

                    if (TryResolveAddressFromViewBlockingEntityPart(component, out addressKey))
                    {
                        sourceDescription = GetHierarchyPath(component.transform);
                        return true;
                    }
                }

                current = current.parent;
                depth++;
            }

            return false;
        }

        private static bool TryResolveAddressFromViewBlockingEntityPart(Component component, out string addressKey)
        {
            addressKey = string.Empty;
            var cityBuildingController = GetMemberValue(component, "cityBuildingController");
            if (cityBuildingController == null)
                return false;

            var buildingRegistration = GetMemberValue(cityBuildingController, "buildingRegistration");
            if (buildingRegistration == null)
                return false;

            var address = GetMemberValue(buildingRegistration, "Address");
            return TryNormalizeAddress(address, out addressKey);
        }

        private static bool IsViewBlockingEntityPart(Component component)
        {
            var componentTypeName = component.GetType().Name;
            return string.Equals(componentTypeName, "ViewBlockingEntityPart", StringComparison.Ordinal) ||
                   string.Equals(component.GetType().FullName, "Entities.ViewBlockingEntityPart", StringComparison.Ordinal);
        }

        private static bool TryNormalizeAddress(object addressValue, out string addressKey)
        {
            addressKey = string.Empty;
            switch (addressValue)
            {
                case null:
                    return false;
                case Address address:
                    addressKey = NormalizeAddressKey(address.ToString());
                    return !string.IsNullOrWhiteSpace(addressKey);
                case string text:
                    addressKey = NormalizeAddressKey(text);
                    return !string.IsNullOrWhiteSpace(addressKey);
                default:
                    addressKey = NormalizeAddressKey(addressValue.ToString());
                    return !string.IsNullOrWhiteSpace(addressKey);
            }
        }

        private static string NormalizeAddressKey(string addressKey)
        {
            return string.IsNullOrWhiteSpace(addressKey)
                ? string.Empty
                : addressKey.Trim().ToLowerInvariant();
        }

        private static float GetCandidateAgeSeconds(float elapsedSeconds)
        {
            if (string.IsNullOrWhiteSpace(_lastExteriorAddressCandidate) ||
                float.IsNegativeInfinity(_lastExteriorAddressCandidateTime))
                return -1f;

            return Mathf.Max(0f, elapsedSeconds - _lastExteriorAddressCandidateTime);
        }

        private static void LogExteriorAddressCandidateChanged(
            string addressKey,
            string sourceDescription,
            float bestDistance,
            Vector3 playerPosition)
        {
            if (string.Equals(_lastLoggedExteriorCandidate, addressKey, StringComparison.Ordinal))
                return;

            _lastLoggedExteriorCandidate = addressKey ?? string.Empty;
            StreetQuestShared.LogDebug(
                $"ExteriorAddressCandidateChanged address={addressKey} source={sourceDescription} distance={bestDistance:0.00} playerPosition={FormatVector3(playerPosition)}");
        }

        private static void LogIndoorTransitionDetected(string lastCandidate, float candidateAgeSeconds)
        {
            var candidateText = string.IsNullOrWhiteSpace(lastCandidate) ? "<none>" : lastCandidate;
            var signature = $"{candidateText}|{candidateAgeSeconds:0.00}";
            if (string.Equals(_lastLoggedIndoorTransitionCandidate, signature, StringComparison.Ordinal))
                return;

            _lastLoggedIndoorTransitionCandidate = signature;
            var candidateAgeText = candidateAgeSeconds < 0f ? "<none>" : $"{candidateAgeSeconds:0.00}s";
            StreetQuestShared.LogDebug(
                $"IndoorTransitionDetected lastCandidate={candidateText} candidateAge={candidateAgeText}");
        }

        private static void LogIndoorAddressResolved(string addressKey, string source)
        {
            if (string.Equals(_lastLoggedResolvedAddress, addressKey, StringComparison.Ordinal))
                return;

            _lastLoggedResolvedAddress = addressKey ?? string.Empty;
            _lastLoggedClearedReason = string.Empty;
            StreetQuestShared.LogDebug(
                $"IndoorAddressResolved address={addressKey} source={source}");
        }

        private static void LogIndoorAddressCleared(string reason)
        {
            if (string.Equals(_lastLoggedClearedReason, reason, StringComparison.Ordinal))
                return;

            _lastLoggedClearedReason = reason ?? string.Empty;
            _lastLoggedResolvedAddress = string.Empty;
            StreetQuestShared.LogDebug($"IndoorAddressCleared reason={reason}");
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var names = new System.Collections.Generic.Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"{value.x:0.00}, {value.y:0.00}, {value.z:0.00}";
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            var type = instance.GetType();
            while (type != null)
            {
                var property = type.GetProperty(memberName, ReflectionFlags);
                if (property != null)
                    return property.GetValue(instance);

                var field = type.GetField(memberName, ReflectionFlags);
                if (field != null)
                    return field.GetValue(instance);

                type = type.BaseType;
            }

            return null;
        }
    }
}
