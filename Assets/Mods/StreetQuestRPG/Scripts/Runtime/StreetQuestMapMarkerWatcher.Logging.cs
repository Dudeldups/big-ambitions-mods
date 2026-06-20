using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestMapMarkerWatcher
    {
        private void LogLifecycleState(string state)
        {
            if (string.Equals(_lastLifecycleState, state, StringComparison.Ordinal))
                return;

            _lastLifecycleState = state;
            DebugLog($"MapMarkerWatcher: {state}");
        }
        private void LogKnownCharacters(IReadOnlyCollection<string> knownCharacterIds)
        {
            var snapshot = knownCharacterIds == null || knownCharacterIds.Count == 0
                ? "<none>"
                : string.Join(", ", knownCharacterIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            if (string.Equals(_lastKnownCharacterSnapshot, snapshot, StringComparison.Ordinal))
                return;

            _lastKnownCharacterSnapshot = snapshot;
            DebugLog($"Map marker known NPCs: {snapshot}");
        }
        private void LogMarkerState(string characterId, bool isVisible, string reason)
        {
            _markerVisibilityStates.TryGetValue(characterId, out var previousVisibility);
            _markerStatusReasons.TryGetValue(characterId, out var previousReason);

            if (previousVisibility == isVisible && string.Equals(previousReason, reason, StringComparison.Ordinal))
                return;

            _markerVisibilityStates[characterId] = isVisible;
            _markerStatusReasons[characterId] = reason;
            DebugLog($"Map marker characterId={characterId} visible={isVisible} reason={reason}");
        }
        private void MaybeLogVerbose(string message)
        {
            if (!EnableMarkerDebugLogging)
                return;

            if (_elapsedSeconds < _nextVerboseLogAtSeconds)
                return;

            _nextVerboseLogAtSeconds = _elapsedSeconds + 2f;
            StreetQuestShared.LogDebug($"MapMarkerWatcher: {message}");
        }
        private static void DebugLog(string message)
        {
            if (!EnableMarkerDebugLogging || string.IsNullOrWhiteSpace(message))
                return;

            StreetQuestShared.LogDebug(message);
        }
    }
}
