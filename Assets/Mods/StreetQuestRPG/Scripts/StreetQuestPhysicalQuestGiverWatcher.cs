using Dialogs;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestPhysicalQuestGiverWatcher : MonoBehaviour
    {
        private const float WarmupRetryIntervalSeconds = 1f;
        private const float WarmupDurationSeconds = 20f;
        private const float SteadyRetryIntervalSeconds = 5f;
        private const float OverlayRetryIntervalSeconds = 0.1f;

        private CallDialogType _dialogType;
        private float _elapsedSeconds;
        private float _nextRetryAtSeconds;
        private float _nextOverlayRetryAtSeconds;
        private bool _runtimeItemPatched;

        public void Initialize(CallDialogType dialogType)
        {
            _dialogType = dialogType;
            _elapsedSeconds = 0f;
            _nextRetryAtSeconds = 0f;
            _nextOverlayRetryAtSeconds = 0f;
            _runtimeItemPatched = false;
        }

        private void Update()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;

            if (_elapsedSeconds >= _nextOverlayRetryAtSeconds)
            {
                StreetQuestShared.TryPatchSellerStandOverlayButtons(_dialogType);
                _nextOverlayRetryAtSeconds = _elapsedSeconds + OverlayRetryIntervalSeconds;
            }

            if (_runtimeItemPatched || _elapsedSeconds < _nextRetryAtSeconds)
                return;

            var installResult = StreetQuestShared.TryInstallPhysicalQuestGiver(_dialogType);
            _runtimeItemPatched =
                (installResult & StreetQuestPhysicalQuestGiverInstallResult.RuntimeItem) != 0;
            _nextRetryAtSeconds = _elapsedSeconds +
                (_elapsedSeconds < WarmupDurationSeconds ? WarmupRetryIntervalSeconds : SteadyRetryIntervalSeconds);
        }
    }
}
