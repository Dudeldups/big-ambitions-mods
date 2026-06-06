using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestPhysicalQuestGiverWatcher : MonoBehaviour
    {
        private const float SpawnRetryIntervalSeconds = 2f;

        private float _elapsedSeconds;
        private float _nextSpawnRetryAtSeconds;
        private bool _spawnEnsured;

        public void Initialize(CallDialogType dialogType)
        {
            StreetQuestShared.SetQuestDialogType(dialogType);
            _elapsedSeconds = 0f;
            _nextSpawnRetryAtSeconds = 0f;
            _spawnEnsured = false;
        }

        private void Update()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;

            if (Input.GetKeyDown(KeyCode.F8))
                StreetQuestShared.LogCoordinateSnapshot();

            if (Input.GetKeyDown(KeyCode.F9))
                StreetQuestShared.MoveSpawnedQuestGiverToPlayer();

            if (!_spawnEnsured && _elapsedSeconds >= _nextSpawnRetryAtSeconds)
            {
                _spawnEnsured = StreetQuestShared.EnsureSpawnedOutdoorQuestGiver();
                _nextSpawnRetryAtSeconds = _elapsedSeconds + SpawnRetryIntervalSeconds;
            }
        }
    }
}
