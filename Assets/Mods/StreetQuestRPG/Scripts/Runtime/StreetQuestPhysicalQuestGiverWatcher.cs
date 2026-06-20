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

        private float _elapsedSeconds;
        private float _nextSpawnRetryAtSeconds;
        private float _nextObjectiveTickAtSeconds;
        private float _nextBootstrapRetryAtSeconds;
        private int _lastScheduleHourKey;
        private bool _initialized;
        private bool _spawnEnsured;
        private bool _bootstrapEnsured;
        private StreetQuestDebugOverlay _debugOverlay;

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
            _initialized = true;
        }

        internal void ResetRuntimeState()
        {
            _nextSpawnRetryAtSeconds = 0f;
            _nextBootstrapRetryAtSeconds = 0f;
            _lastScheduleHourKey = int.MinValue;
            _spawnEnsured = false;
            _bootstrapEnsured = false;
            StreetQuestShared.LogSchedule("watcher runtime state reset");
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
                StreetQuestShared.LogSchedule($"hour tick previous={_lastScheduleHourKey} current={hourKey}");
                _lastScheduleHourKey = hourKey;
                StreetQuestShared.RefreshSchedulesIfVisibilityChanged();
            }

            if (_elapsedSeconds >= _nextObjectiveTickAtSeconds)
            {
                StreetQuestShared.TickWorldObjectives();
                _nextObjectiveTickAtSeconds = _elapsedSeconds + 0.5f;
            }
        }
    }
}
