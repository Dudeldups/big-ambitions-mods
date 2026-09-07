#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class AudiRS6RAudioModel
{
    internal const float IdlePitch = 1f;
    internal const float PopVolume = .48f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .15f) / .65f);
    internal static float IdleVolume(float drivingBlend) => .24f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    // Restore revision 7's idle-to-driving thresholds (1180..2440 RPM with
    // 900 RPM idle / 7000 RPM limiter). Throttle cannot change idle gain/pitch.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .04f * limiter) / Math.Max(1f, .18f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 96f : layer == 1 ? 176f : 320f;
    internal static float TargetHz(float normalized) => (float)(80d * Math.Pow(180d / 80d, Clamp01(normalized)));
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

internal enum AudiRS6RPopEvent { None, Overrun }

internal sealed class AudiRS6RPopGate
{
    private bool previousValid;
    private readonly Random random;
    private double previousTime;
    private double lastLoad;
    private double nextPop;
    internal bool OverrunActive { get; private set; }

    internal AudiRS6RPopGate(int? seed = null)
    {
        random = seed.HasValue ? new Random(seed.Value) : new Random();
        Reset();
    }

    internal static float Rate(int gear) => gear == 1 ? 2.8f : gear == 2 ? 2.2f :
        gear == 3 ? 1.3f : gear == 4 ? .65f : .35f;

    private double Delay(int gear) => .16d - Math.Log(Math.Max(1e-9d, 1d - random.NextDouble())) / Rate(gear);

    internal void Reset()
    {
        previousValid = false;
        lastLoad = double.NegativeInfinity;
        nextPop = double.PositiveInfinity;
        OverrunActive = false;
    }

    internal AudiRS6RPopEvent Sample(bool active, double time, float rpm, float throttle, int gear)
    {
        if (!active || gear <= 0)
        {
            Reset();
            return AudiRS6RPopEvent.None;
        }
        var continuous = previousValid && time >= previousTime && time - previousTime <= .5d;
        if (!continuous) Reset();
        if (rpm >= 2500f && throttle >= .4f) lastLoad = time;
        var eligible = continuous && rpm >= 1800f && throttle <= .22f && time - lastLoad <= 2.4d;
        var result = AudiRS6RPopEvent.None;
        if (!eligible)
        {
            OverrunActive = false;
            nextPop = double.PositiveInfinity;
        }
        else if (!OverrunActive)
        {
            OverrunActive = true;
            // A shift or throttle edge only opens an opportunity; it never
            // directly triggers a pop. Sample once, not once per render frame.
            nextPop = time + Delay(gear);
        }
        else if (time >= nextPop)
        {
            result = AudiRS6RPopEvent.Overrun;
            nextPop = time + Delay(gear);
        }
        previousValid = true;
        previousTime = time;
        return result;
    }
}
