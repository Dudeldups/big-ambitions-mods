#nullable enable
using System;
using UnityEngine;

internal enum BMWM4G82ShiftPopEvent
{
    None,
    Upshift,
    Downshift,
}

// Detects genuine transmission changes from driver input and engine speed.
// Unlike the Audi's randomized overrun bursts, the BMW uses a short,
// deterministic two-stage snap on hard low-gear upshifts and a single deeper
// response after a measured downshift rev flare.
internal sealed class BMWM4G82ShiftPopGate
{
    private bool valid;
    private int previousGear;
    private float previousRpm;
    private float previousThrottle;
    private double previousTime;
    private double nextPop;
    private double cooldownUntil;
    private double downshiftUntil;
    private float downshiftBaseRpm;
    private float downshiftPeakRpm;
    private float burstIntensity;
    private int remaining;
    private int burstSize;
    private BMWM4G82ShiftPopEvent burstEvent;

    internal float Intensity { get; private set; }

    internal void Reset()
    {
        valid = false;
        previousGear = 0;
        previousRpm = 0f;
        previousThrottle = 0f;
        previousTime = 0d;
        nextPop = double.PositiveInfinity;
        cooldownUntil = 0d;
        downshiftUntil = double.PositiveInfinity;
        downshiftBaseRpm = 0f;
        downshiftPeakRpm = 0f;
        burstIntensity = 0f;
        remaining = 0;
        burstSize = 0;
        burstEvent = BMWM4G82ShiftPopEvent.None;
        Intensity = 0f;
    }

    internal BMWM4G82ShiftPopEvent Sample(
        bool active,
        double time,
        float rpm,
        float throttle,
        int gear,
        float speedKph)
    {
        if (!active || gear <= 0)
        {
            Reset();
            return BMWM4G82ShiftPopEvent.None;
        }

        throttle = Mathf.Clamp01(throttle);
        var continuous = valid && time >= previousTime && time - previousTime <= .5d;
        if (!continuous)
        {
            Reset();
            previousGear = gear;
            previousRpm = rpm;
            previousThrottle = throttle;
            previousTime = time;
            valid = true;
            return BMWM4G82ShiftPopEvent.None;
        }

        if (gear > previousGear)
        {
            downshiftUntil = double.PositiveInfinity;
            var rpmStrength = Smooth(3800f, 6900f, previousRpm);
            var loadStrength = Smooth(.42f, .92f, previousThrottle);
            var speedStrength = Smooth(12f, 45f, speedKph);
            var strength = rpmStrength * loadStrength * speedStrength;
            if (strength > .12f)
                Schedule(
                    BMWM4G82ShiftPopEvent.Upshift,
                    time,
                    .46f + .54f * strength,
                    previousGear <= 2 && strength > .55f ? 2 : 1);
        }
        else if (gear < previousGear)
        {
            downshiftBaseRpm = previousRpm;
            downshiftPeakRpm = rpm;
            downshiftUntil = time + .16d;
        }

        if (!double.IsPositiveInfinity(downshiftUntil))
        {
            downshiftPeakRpm = Math.Max(downshiftPeakRpm, rpm);
            if (time >= downshiftUntil)
            {
                var flare = Math.Max(0f, downshiftPeakRpm - downshiftBaseRpm);
                var strength = Smooth(260f, 1500f, flare) *
                               Smooth(1800f, 6000f, downshiftPeakRpm) *
                               Smooth(8f, 35f, speedKph);
                if (strength > .10f)
                    Schedule(
                        BMWM4G82ShiftPopEvent.Downshift,
                        time,
                        .38f + .48f * strength,
                        1);
                downshiftUntil = double.PositiveInfinity;
            }
        }

        previousGear = gear;
        previousRpm = rpm;
        previousThrottle = throttle;
        previousTime = time;
        valid = true;

        if (remaining <= 0 || time < nextPop)
            return BMWM4G82ShiftPopEvent.None;

        var emittedIndex = burstSize - remaining;
        Intensity = burstIntensity * (emittedIndex == 0 ? 1f : .52f);
        remaining--;
        nextPop = time + (burstEvent == BMWM4G82ShiftPopEvent.Upshift ? .082d : .11d);
        return burstEvent;
    }

    private void Schedule(
        BMWM4G82ShiftPopEvent popEvent,
        double time,
        float intensity,
        int count)
    {
        if (time < cooldownUntil || remaining > 0)
            return;
        burstEvent = popEvent;
        burstIntensity = Mathf.Clamp01(intensity);
        remaining = Math.Max(1, count);
        burstSize = remaining;
        nextPop = time + (popEvent == BMWM4G82ShiftPopEvent.Upshift ? .028d : .045d);
        cooldownUntil = time + (popEvent == BMWM4G82ShiftPopEvent.Upshift ? .42d : .58d);
    }

    private static float Smooth(float low, float high, float value)
    {
        var normalized = Mathf.Clamp01((value - low) / Math.Max(.0001f, high - low));
        return normalized * normalized * (3f - 2f * normalized);
    }
}

internal static class BMWM4G82ShiftPopWave
{
    private const int SampleRate = 44100;

    internal static AudioClip Create(int variant)
    {
        var duration = .19f + .015f * Mathf.Clamp(variant, 0, 2);
        var sampleCount = Mathf.CeilToInt(SampleRate * duration);
        var samples = new float[sampleCount];
        uint random = 0x534D3438u + (uint)(variant * 0x10203);
        var filteredNoise = 0f;
        var bodyFrequency = 76f + variant * 9f;
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            random = random * 1664525u + 1013904223u;
            var noise = ((random >> 8) / 8388607.5f) - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, .24f);

            var bodyEnvelope = Mathf.Exp(-time * (17f + variant));
            var snapEnvelope = Mathf.Exp(-time * 68f);
            var body = Mathf.Sin(2f * Mathf.PI * bodyFrequency * time + 13f * time * time);
            var metallic = Mathf.Sin(2f * Mathf.PI * (310f + variant * 35f) * time);
            samples[index] =
                bodyEnvelope * (.42f * body + .25f * filteredNoise) +
                snapEnvelope * (.22f * noise + .11f * metallic);
            samples[index] *= .48f;
        }

        var clip = AudioClip.Create(
            $"BMW M4 G82 shift exhaust pop {variant + 1}",
            sampleCount,
            1,
            SampleRate,
            false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural BMW shift pop.");
        }
        return clip;
    }
}
