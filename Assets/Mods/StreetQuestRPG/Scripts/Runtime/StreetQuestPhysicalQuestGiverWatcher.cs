using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    [DefaultExecutionOrder(-10000)]
    internal sealed class StreetQuestPhysicalQuestGiverWatcher : MonoBehaviour
    {
        private const float SpawnRetryIntervalSeconds = 2f;
        private const float DebugBootstrapRetryIntervalSeconds = 2f;
        private const float DebugBootstrapRetryWindowSeconds = 20f;
        private const float IndoorAddressResolveRetryIntervalSeconds = 0.2f;
        private const float IndoorAddressResolveRetryWindowSeconds = 2.4f;

        private float _elapsedSeconds;
        private float _nextSpawnRetryAtSeconds;
        private float _nextObjectiveTickAtSeconds;
        private float _nextBootstrapRetryAtSeconds;
        private float _nextIndoorAddressResolveAtSeconds;
        private float _indoorAddressResolveWindowEndsAtSeconds;
        private int _lastScheduleHourKey;
        private bool _initialized;
        private bool _spawnEnsured;
        private bool _bootstrapEnsured;
        private bool _lastIndoorGameplayContextActive;
        private bool _hasObservedIndoorGameplayContext;
        private bool _pendingIndoorAddressResolve;
        private StreetQuestDebugOverlay _debugOverlay;
        private StreetQuestDebugBuildingLogger _debugBuildingLogger;

        public void Initialize()
        {
            if (_initialized)
                return;

            ResetRuntimeState();
            _elapsedSeconds = 0f;
            _nextObjectiveTickAtSeconds = 0f;
            _debugOverlay = GetComponent<StreetQuestDebugOverlay>();
            if (_debugOverlay == null && StreetQuestDebugSettings.Enabled)
                _debugOverlay = gameObject.AddComponent<StreetQuestDebugOverlay>();
            _debugBuildingLogger = GetComponent<StreetQuestDebugBuildingLogger>();
            if (_debugBuildingLogger == null && StreetQuestDebugSettings.Enabled)
                _debugBuildingLogger = gameObject.AddComponent<StreetQuestDebugBuildingLogger>();
            _initialized = true;
        }

        internal void ResetRuntimeState()
        {
            _nextSpawnRetryAtSeconds = 0f;
            _nextBootstrapRetryAtSeconds = 0f;
            _nextIndoorAddressResolveAtSeconds = 0f;
            _indoorAddressResolveWindowEndsAtSeconds = 0f;
            _lastScheduleHourKey = int.MinValue;
            _spawnEnsured = false;
            _bootstrapEnsured = false;
            _lastIndoorGameplayContextActive = false;
            _hasObservedIndoorGameplayContext = false;
            _pendingIndoorAddressResolve = false;
        }

        private void Update()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;
            _debugOverlay?.TickToggle();
            if (_debugOverlay != null && _debugOverlay.ShouldBlockGameplayInput())
                Input.ResetInputAxes();

            if (!_bootstrapEnsured)
                _bootstrapEnsured = StreetQuestRuntimeBootstrap.EnsureCityRuntimeReady();

            if (StreetQuestDebugSettings.Enabled &&
                !_spawnEnsured &&
                _elapsedSeconds <= DebugBootstrapRetryWindowSeconds &&
                _elapsedSeconds >= _nextBootstrapRetryAtSeconds)
            {
                StreetQuestRuntimeBootstrap.EnsureCityRuntimeReady();
                _nextBootstrapRetryAtSeconds = _elapsedSeconds + DebugBootstrapRetryIntervalSeconds;
            }

            if (!_spawnEnsured && _elapsedSeconds >= _nextSpawnRetryAtSeconds)
            {
                StreetQuestShared.EnsureSpawnedOutdoorQuestGiver();
                StreetQuestShared.UpdateScheduleVisibilitySnapshot();
                _spawnEnsured = _bootstrapEnsured;
                _nextSpawnRetryAtSeconds = _elapsedSeconds + SpawnRetryIntervalSeconds;
            }

            if (_spawnEnsured &&
                StreetQuestShared.TryGetCurrentGameHourKey(out var hourKey) &&
                hourKey != _lastScheduleHourKey)
            {
                _lastScheduleHourKey = hourKey;
                StreetQuestShared.RefreshSchedulesIfVisibilityChanged();
            }

            if (_elapsedSeconds >= _nextObjectiveTickAtSeconds)
            {
                StreetQuestShared.TickWorldObjectives();
                _nextObjectiveTickAtSeconds = _elapsedSeconds + 0.5f;
            }

            UpdateIndoorBuildingContext();
        }

        private void UpdateIndoorBuildingContext()
        {
            var indoorGameplayContextActive = StreetQuestShared.IsIndoorGameplayContextActive();
            if (!_hasObservedIndoorGameplayContext)
            {
                _hasObservedIndoorGameplayContext = true;
                _lastIndoorGameplayContextActive = indoorGameplayContextActive;
                if (indoorGameplayContextActive)
                    BeginIndoorAddressResolve();
                else
                    ClearIndoorBuildingContextAndRefresh();
            }
            else if (indoorGameplayContextActive != _lastIndoorGameplayContextActive)
            {
                _lastIndoorGameplayContextActive = indoorGameplayContextActive;
                if (indoorGameplayContextActive)
                    BeginIndoorAddressResolve();
                else
                    ClearIndoorBuildingContextAndRefresh();
            }

            if (!_pendingIndoorAddressResolve || _elapsedSeconds < _nextIndoorAddressResolveAtSeconds)
                return;

            if (_elapsedSeconds > _indoorAddressResolveWindowEndsAtSeconds)
            {
                _pendingIndoorAddressResolve = false;
                return;
            }

            _nextIndoorAddressResolveAtSeconds = _elapsedSeconds + IndoorAddressResolveRetryIntervalSeconds;
            if (!StreetQuestShared.TryResolveCurrentIndoorBuildingAddress(out var addressKey))
                return;

            _pendingIndoorAddressResolve = false;
            StreetQuestShared.SetCurrentIndoorBuildingAddressKey(addressKey);
            if (_spawnEnsured)
                StreetQuestShared.RefreshSpawnedCharacters();
        }

        private void BeginIndoorAddressResolve()
        {
            _pendingIndoorAddressResolve = true;
            _nextIndoorAddressResolveAtSeconds = _elapsedSeconds;
            _indoorAddressResolveWindowEndsAtSeconds = _elapsedSeconds + IndoorAddressResolveRetryWindowSeconds;
        }

        private void ClearIndoorBuildingContextAndRefresh()
        {
            _pendingIndoorAddressResolve = false;
            if (!StreetQuestShared.SetCurrentIndoorBuildingAddressKey(string.Empty))
                return;

            if (_spawnEnsured)
                StreetQuestShared.RefreshSpawnedCharacters();
        }
    }
}
