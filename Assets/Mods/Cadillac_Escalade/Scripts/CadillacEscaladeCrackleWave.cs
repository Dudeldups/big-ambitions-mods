#nullable enable
using System;
using UnityEngine;

internal static class CadillacEscaladeCrackleWave
{
    private const int SampleRate = 44100;
    private const float Duration = 4f;

    internal static AudioClip Create()
    {
        var pulseTimes = new[] { .30f, 1.30f, 2.40f, 3.50f };
        var pulseFrequencies = new[] { 46f, 50f, 43f, 48f };
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        uint random = 0x45534341u;
        var filteredNoise = 0f;
        for (var index = 0; index < sampleCount; index++)
        {
            random = random * 1664525u + 1013904223u;
            var noise = ((random >> 8) / 8388607.5f) - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, .16f);
            var time = index / (float)SampleRate;
            var sample = 0f;
            for (var pulseIndex = 0; pulseIndex < pulseTimes.Length; pulseIndex++)
            {
                var sincePulse = time - pulseTimes[pulseIndex];
                if (sincePulse < 0f)
                    sincePulse += Duration;
                var envelope = Mathf.Exp(-sincePulse * 5.5f);
                var frequency = pulseFrequencies[pulseIndex];
                var fundamental = Mathf.Sin(2f * Mathf.PI * frequency * sincePulse);
                var secondHarmonic = Mathf.Sin(4f * Mathf.PI * frequency * sincePulse + .35f);
                var thirdHarmonic = Mathf.Sin(6f * Mathf.PI * frequency * sincePulse + .62f);
                sample += envelope *
                          (.46f * fundamental + .34f * secondHarmonic +
                           .12f * thirdHarmonic + .08f * filteredNoise);
            }
            // The source is mixed below the main engine bed, so give the clip
            // enough intrinsic level for its slow pulses to survive spatial
            // attenuation without turning them into sharp exhaust cracks.
            samples[index] = sample * .45f;
        }

        var clip = AudioClip.Create(
            "Cadillac Escalade spaced high-displacement V8 exhaust burble",
            sampleCount,
            1,
            SampleRate,
            false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural exhaust burble.");
        }

        return clip;
    }
}
