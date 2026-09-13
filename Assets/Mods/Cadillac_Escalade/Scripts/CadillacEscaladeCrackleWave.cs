#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

internal static class CadillacEscaladeCrackleWave
{
    private const int SampleRate = 44100;
    private const float Duration = 4f;

    internal static AudioClip Create()
    {
        // A large naturally aspirated V8 has a distinct loping exhaust texture
        // under load, not one isolated pop every second. Keep the beats uneven
        // and separated enough to avoid a rapid sports-car crackle.
        var intervals = new[]
            { .16f, .19f, .15f, .21f, .17f, .18f, .20f, .14f, .18f, .17f, .22f, .16f };
        var pulseFrequencies = new[] { 46f, 51f, 44f, 49f, 47f, 52f, 45f, 50f };
        var pulseAmplitudes = new[] { .90f, 1f, .82f, .95f, .87f, 1.04f, .85f, .97f };
        var pulseTimes = new List<float>(24);
        var pulseTime = .06f;
        var intervalIndex = 0;
        while (pulseTime < Duration)
        {
            pulseTimes.Add(pulseTime);
            pulseTime += intervals[intervalIndex++ % intervals.Length];
        }
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
            for (var pulseIndex = 0; pulseIndex < pulseTimes.Count; pulseIndex++)
            {
                var sincePulse = time - pulseTimes[pulseIndex];
                if (sincePulse < 0f)
                    sincePulse += Duration;
                // Let adjacent beats overlap. A large V8 exhaust retains body
                // between firing accents instead of dropping into digital-
                // sounding silence before every following beat.
                var envelope = (1f - Mathf.Exp(-sincePulse * 24f)) *
                               Mathf.Exp(-sincePulse * 5.8f) *
                               pulseAmplitudes[pulseIndex % pulseAmplitudes.Length];
                var frequency = pulseFrequencies[pulseIndex % pulseFrequencies.Length];
                var fundamental = Mathf.Sin(2f * Mathf.PI * frequency * sincePulse);
                var secondHarmonic = Mathf.Sin(4f * Mathf.PI * frequency * sincePulse + .20f);
                var thirdHarmonic = Mathf.Sin(6f * Mathf.PI * frequency * sincePulse + .35f);
                sample += envelope *
                          (.70f * fundamental + .18f * secondHarmonic +
                           .04f * thirdHarmonic + .06f * filteredNoise);
            }
            // The smooth onset and conservative scale keep the generated clip
            // comfortably below full scale without a hard limiter.
            samples[index] = sample * .55f;
        }

        var clip = AudioClip.Create(
            "Cadillac Escalade loping high-displacement V8 load burble",
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
