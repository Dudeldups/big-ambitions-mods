using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestPhysicalQuestGiverWatcher : MonoBehaviour
    {
        private const float WarmupRetryIntervalSeconds = 1f;
        private const float WarmupDurationSeconds = 20f;
        private const float SteadyRetryIntervalSeconds = 5f;

        private CallDialogType _dialogType;
        private float _elapsedSeconds;
        private float _nextRetryAtSeconds;

        public void Initialize(CallDialogType dialogType)
        {
            _dialogType = dialogType;
            _elapsedSeconds = 0f;
            _nextRetryAtSeconds = 0f;
        }

        private void Update()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;
            if (_elapsedSeconds < _nextRetryAtSeconds)
                return;

            var installResult = StreetQuestShared.TryInstallPhysicalQuestGiver(_dialogType);
            if ((installResult & StreetQuestPhysicalQuestGiverInstallResult.RuntimeItem) != 0)
            {
                Destroy(gameObject);
                return;
            }

            _nextRetryAtSeconds = _elapsedSeconds +
                (_elapsedSeconds < WarmupDurationSeconds ? WarmupRetryIntervalSeconds : SteadyRetryIntervalSeconds);
        }
    }
}
