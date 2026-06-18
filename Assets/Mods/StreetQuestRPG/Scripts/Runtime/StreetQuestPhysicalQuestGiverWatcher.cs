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
        private bool _spawnEnsured;
        private bool _bootstrapEnsured;
        private StreetQuestDebugOverlay _debugOverlay;

        public void Initialize()
        {
            _elapsedSeconds = 0f;
            _nextSpawnRetryAtSeconds = 0f;
            _nextObjectiveTickAtSeconds = 0f;
            _nextBootstrapRetryAtSeconds = 0f;
            _spawnEnsured = false;
            _bootstrapEnsured = false;
            _debugOverlay = GetComponent<StreetQuestDebugOverlay>();
            if (_debugOverlay == null && StreetQuestDebugSettings.Enabled)
                _debugOverlay = gameObject.AddComponent<StreetQuestDebugOverlay>();
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
                _spawnEnsured = StreetQuestShared.EnsureSpawnedOutdoorQuestGiver();
                _nextSpawnRetryAtSeconds = _elapsedSeconds + SpawnRetryIntervalSeconds;
            }

            if (_elapsedSeconds >= _nextObjectiveTickAtSeconds)
            {
                StreetQuestShared.TickWorldObjectives();
                _nextObjectiveTickAtSeconds = _elapsedSeconds + 0.5f;
            }
        }
    }
}
