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
        var intervals = new[] { .16f, .19f, .15f, .21f, .17f, .18f };
        var pulseFrequencies = new[] { 46f, 51f, 44f, 49f, 47f, 52f };
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
                var envelope = Mathf.Exp(-sincePulse * 6f);
                var frequency = pulseFrequencies[pulseIndex % pulseFrequencies.Length];
                var fundamental = Mathf.Sin(2f * Mathf.PI * frequency * sincePulse);
                var secondHarmonic = Mathf.Sin(4f * Mathf.PI * frequency * sincePulse + .35f);
                var thirdHarmonic = Mathf.Sin(6f * Mathf.PI * frequency * sincePulse + .62f);
                sample += envelope *
                          (.46f * fundamental + .34f * secondHarmonic +
                           .12f * thirdHarmonic + .08f * filteredNoise);
            }
            samples[index] = Mathf.Clamp(sample * .60f, -.90f, .90f);
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
