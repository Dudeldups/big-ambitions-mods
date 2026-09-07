#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class AudiRS6RAudioModel
{
    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 96f : layer == 1 ? 176f : 320f;
    internal static float TargetHz(float normalized) => (float)(96d * Math.Pow(320d / 96d, Clamp01(normalized)));
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

internal enum AudiRS6RPopEvent { None, ThrottleRelease, Upshift }

internal sealed class AudiRS6RPopGate
{
    private bool previousValid;
    private int previousGear;
    private double previousTime;
    private double armedUntil;
    private double nextAllowed;

    internal void Reset()
    {
        previousValid = false;
        armedUntil = double.NegativeInfinity;
        nextAllowed = 0;
    }

    internal AudiRS6RPopEvent Sample(bool active, double time, float rpm, float throttle, int gear)
    {
        if (!active || gear <= 0)
        {
            Reset();
            return AudiRS6RPopEvent.None;
        }
        var result = AudiRS6RPopEvent.None;
        var continuous = previousValid && time >= previousTime && time - previousTime <= .5d;
        if (!continuous) armedUntil = double.NegativeInfinity;
        if (continuous && time <= armedUntil && time >= nextAllowed && rpm >= 2200f)
        {
            if (gear > previousGear) result = AudiRS6RPopEvent.Upshift;
            else if (throttle <= .18f) result = AudiRS6RPopEvent.ThrottleRelease;
        }
        if (result != AudiRS6RPopEvent.None)
        {
            nextAllowed = time + .85d;
            armedUntil = double.NegativeInfinity;
        }
        else if (rpm >= 3000f && throttle >= .55f)
            armedUntil = time + .65d;
        previousValid = true;
        previousGear = gear;
        previousTime = time;
        return result;
    }
}
