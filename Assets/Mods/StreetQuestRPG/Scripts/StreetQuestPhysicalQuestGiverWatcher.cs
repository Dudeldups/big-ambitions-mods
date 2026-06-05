using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestPhysicalQuestGiverWatcher : MonoBehaviour
    {
        private const float OverlayRetryIntervalSeconds = 0.1f;
        private const float SpawnRetryIntervalSeconds = 1f;

        private CallDialogType _dialogType;
        private float _elapsedSeconds;
        private float _nextOverlayRetryAtSeconds;
        private float _nextSpawnRetryAtSeconds;

        public void Initialize(CallDialogType dialogType)
        {
            _dialogType = dialogType;
            _elapsedSeconds = 0f;
            _nextOverlayRetryAtSeconds = 0f;
            _nextSpawnRetryAtSeconds = 0f;
        }

        private void Update()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;

            if (Input.GetKeyDown(KeyCode.F8))
                StreetQuestShared.LogCoordinateSnapshot();

            if (Input.GetKeyDown(KeyCode.F9))
                StreetQuestShared.MoveSpawnedQuestGiverToPlayer();

            if (_elapsedSeconds >= _nextSpawnRetryAtSeconds)
            {
                StreetQuestShared.EnsureSpawnedOutdoorQuestGiver();
                _nextSpawnRetryAtSeconds = _elapsedSeconds + SpawnRetryIntervalSeconds;
            }

            if (_elapsedSeconds >= _nextOverlayRetryAtSeconds)
            {
                StreetQuestShared.TryPatchSellerStandOverlayButtons(_dialogType);
                _nextOverlayRetryAtSeconds = _elapsedSeconds + OverlayRetryIntervalSeconds;
            }
        }
    }
}
