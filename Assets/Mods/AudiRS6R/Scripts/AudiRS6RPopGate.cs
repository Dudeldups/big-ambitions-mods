#nullable enable
using System;
using System.Collections.Generic;

internal enum AudiRS6RPopEvent { None, ThrottleLift, Downshift }

// Pure event detector/scheduler. Throttle is driver input, not the engine's
// automatic shift-cut throttle. Randomness shapes a detected event only.
internal sealed class AudiRS6RPopGate
{
    private readonly Random random;
    private readonly Queue<ThrottlePoint> history = new Queue<ThrottlePoint>();
    private struct ThrottlePoint { internal double Time; internal float Throttle; }
    private bool valid, armed;
    private double previousTime, loadSince, nextPop, cooldownUntil, downshiftUntil;
    private float previousThrottle, previousRpm, downshiftBase, downshiftPeak;
    private int previousGear, remaining;
    private AudiRS6RPopEvent burstReason;
    internal bool OverrunActive => remaining > 0;
    internal float Intensity { get; private set; }
    internal int BurstId { get; private set; }
    internal int BurstSize { get; private set; }
    internal int DecisionId { get; private set; }
    internal string Decision { get; private set; } = "none";
    internal float Probability { get; private set; }
    internal float RpmJump { get; private set; }

    internal AudiRS6RPopGate(int? seed = null)
    {
        random = seed.HasValue ? new Random(seed.Value) : new Random();
        Reset();
    }

    private static float Smooth(float low, float high, float value)
    {
        var x = Math.Max(0f, Math.Min(1f, (value-low)/(high-low)));
        return x*x*(3f-2f*x);
    }
    internal static float RpmFactor(float rpm) => Smooth(1400f, 5500f, rpm);
    internal static float GearFactor(int gear, float rpm) => gear <= 1 ? 1f : gear == 2 ? .95f :
        gear == 3 ? .8f : gear == 4 ? .35f : .06f + .30f*Smooth(5700f, 7000f, rpm);

    // Strong driver lifts deserve a clear response even after an automatic
    // upshift. Keep moderate-RPM high-gear cruising rare; downshifts retain
    // their separate calibration and still require a measured RPM increase.
    internal static float LiftGearFactor(int gear, float rpm) => gear <= 1 ? 1f :
        gear == 2 ? .98f : gear == 3 ? .92f : gear == 4 ?
        .40f + .40f*Smooth(4000f, 6200f, rpm) :
        .06f + .64f*Smooth(4200f, 6500f, rpm);

    internal void Reset()
    {
        valid = armed = false;
        history.Clear();
        remaining = 0;
        loadSince = double.NaN;
        downshiftUntil = nextPop = double.PositiveInfinity;
        cooldownUntil = 0;
    }

    private void TryBurst(AudiRS6RPopEvent reason, double time, float chance, float strength, int maxPops)
    {
        DecisionId++;
        Probability = chance;
        if (time < cooldownUntil || remaining > 0) { Decision = reason+":cooldown"; return; }
        if (chance <= 0 || random.NextDouble() >= chance) { Decision = reason+":silent"; return; }
        Intensity = .4f + .6f*strength;
        remaining = 1;
        if (maxPops >= 2 && random.NextDouble() < .8d*strength) remaining++;
        if (maxPops >= 3 && strength > .8f && random.NextDouble() < .4d) remaining++;
        BurstSize = remaining;
        BurstId++;
        burstReason = reason;
        Decision = reason+":burst";
        nextPop = time + .035d + random.NextDouble()*.065d;
        cooldownUntil = time + 1.0d + random.NextDouble()*.35d;
    }

    internal AudiRS6RPopEvent Sample(bool active, double time, float rpm, float throttle, int gear, float speedKph = 0f)
    {
        if (!active || gear <= 0)
        {
            Reset();
            return AudiRS6RPopEvent.None;
        }
        throttle = Math.Max(0f, Math.Min(1f, throttle));
        var continuous = valid && time >= previousTime && time-previousTime <= .5d;
        if (!continuous) Reset();
        while (history.Count > 0 && time-history.Peek().Time > .22d) history.Dequeue();

        if (throttle >= .45f)
        {
            if (double.IsNaN(loadSince)) loadSince = time;
            if (time-loadSince >= .12d) armed = true;
        }
        else loadSince = double.NaN;

        if (continuous && gear < previousGear)
        {
            downshiftBase = previousRpm;
            downshiftPeak = rpm;
            downshiftUntil = time + .2d;
        }
        else if (continuous && gear > previousGear)
            downshiftUntil = double.PositiveInfinity;
        if (!double.IsPositiveInfinity(downshiftUntil))
        {
            downshiftPeak = Math.Max(downshiftPeak, rpm);
            if (time >= downshiftUntil)
            {
                RpmJump = Math.Max(0f, downshiftPeak-downshiftBase);
                var strength = Smooth(200f, 1600f, RpmJump)*RpmFactor(downshiftPeak);
                var chance = .85f*strength*GearFactor(gear, downshiftPeak)*Smooth(4f, 25f, speedKph);
                TryBurst(AudiRS6RPopEvent.Downshift, time, chance, strength, 2);
                downshiftUntil = double.PositiveInfinity;
            }
        }

        if (continuous && armed && throttle <= .2f && previousThrottle > .2f)
        {
            armed = false; // Consume even a quiet/slow lift; zero throttle cannot retrigger.
            var peak = new ThrottlePoint { Time = time, Throttle = throttle };
            foreach (var point in history)
                if (point.Throttle >= peak.Throttle) peak = point;
            var drop = peak.Throttle-throttle;
            var rate = drop / (float)Math.Max(.04d, time-peak.Time);
            var abrupt = Smooth(.30f, .75f, drop)*Smooth(1.5f, 5f, rate);
            var strength = abrupt*RpmFactor(rpm);
            RpmJump = 0f;
            TryBurst(AudiRS6RPopEvent.ThrottleLift, time, .99f*strength*LiftGearFactor(gear,rpm), strength, 3);
        }
        // Store at most ~23 samples regardless of render rate.
        if (history.Count == 0 || time-lastHistoryTime >= .01d)
        {
            history.Enqueue(new ThrottlePoint { Time=time, Throttle=throttle });
            lastHistoryTime = time;
        }
        previousTime = time;
        previousThrottle = throttle;
        previousRpm = rpm;
        previousGear = gear;
        valid = true;
        if (rpm < 1400f || (throttle > .4f && burstReason == AudiRS6RPopEvent.ThrottleLift)) remaining = 0;
        if (remaining <= 0 || time < nextPop) return AudiRS6RPopEvent.None;
        remaining--;
        nextPop = time + .085d + random.NextDouble()*.12d;
        return burstReason;
    }
    private double lastHistoryTime;
}
