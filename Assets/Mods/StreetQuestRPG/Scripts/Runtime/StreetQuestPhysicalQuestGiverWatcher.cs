using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestPhysicalQuestGiverWatcher : MonoBehaviour
    {
        private const float SpawnRetryIntervalSeconds = 2f;

        private float _elapsedSeconds;
        private float _nextSpawnRetryAtSeconds;
        private float _nextObjectiveTickAtSeconds;
        private bool _spawnEnsured;
        private StreetQuestDebugOverlay _debugOverlay;

        public void Initialize()
        {
            _elapsedSeconds = 0f;
            _nextSpawnRetryAtSeconds = 0f;
            _nextObjectiveTickAtSeconds = 0f;
            _spawnEnsured = false;
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

            if (Input.GetKeyDown(KeyCode.F8))
                StreetQuestShared.LogCoordinateSnapshot();

            if (Input.GetKeyDown(KeyCode.F9))
                StreetQuestShared.MoveSpawnedQuestGiverToPlayer();

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
