#nullable enable
using UnityEngine;

namespace VehicleRuntimeTuner.UI
{
    public sealed class VehicleRuntimeTunerStatusMessage
    {
        private float expiresAt;

        public string Text { get; private set; } = string.Empty;

        public bool HasVisibleMessage => !string.IsNullOrWhiteSpace(Text) && Time.unscaledTime < expiresAt;

        public void Show(string text, float durationSeconds = 4f)
        {
            Text = text;
            expiresAt = Time.unscaledTime + durationSeconds;
        }
    }
}
