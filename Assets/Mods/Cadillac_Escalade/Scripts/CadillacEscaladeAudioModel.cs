#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class CadillacEscaladeAudioModel
{
    internal const float IdlePitch = .52f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .30f;
    internal const float EngineThrottleVolume = .28f;
    internal const float CrackleIdleVolume = .002f;
    internal const float CrackleLoadVolume = .008f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) => .20f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the dedicated low-speed idle bed out gradually so the synthesized
    // V8 layers retain a heavy, relaxed idle-to-load transition.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .02f * limiter) / Math.Max(1f, .14f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 80f : layer == 1 ? 220f : 480f;
    // Four firing events per revolution gives approximately 40 Hz at 600 rpm
    // and 413 Hz at the 6,200 rpm limiter.
    internal static float TargetHz(float normalized) =>
        (float)(37d * Math.Pow(385d / 37d, Clamp01(normalized)));
    internal static float Pitch(float normalized, int layer) => TargetHz(normalized) / ReferenceHz(layer);

    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 2f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower) return (float)Math.Cos(blend * Math.PI * .5d);
        if (layer == lower + 1) return (float)Math.Sin(blend * Math.PI * .5d);
        return 0f;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}
