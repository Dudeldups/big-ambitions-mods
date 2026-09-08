#nullable enable
using System;
using UnityEngine;

internal static class BigfootMonsterTruckEngineWave
{
    private const int SampleRate = 44100;
    private const float Duration = 2f;

    internal static AudioClip CreateRumble() => Create(
        "Bigfoot V8 low rumble",
        42f,
        new[] { 1f, 0.62f, 0.38f, 0.24f, 0.14f },
        0.22f);

    internal static AudioClip CreateRoar() => Create(
        "Bigfoot V8 supercharged roar",
        48f,
        new[] { 0.72f, 1f, 0.68f, 0.46f, 0.31f, 0.20f, 0.13f },
        0.34f);

    private static AudioClip Create(
        string name,
        float fundamental,
        float[] harmonics,
        float roughness)
    {
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        var peak = 0f;
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var phase = 2f * Mathf.PI * fundamental * time;
            var value = 0f;
            for (var harmonic = 0; harmonic < harmonics.Length; harmonic++)
                value += harmonics[harmonic] * Mathf.Sin(phase * (harmonic + 1));

            // Integer-cycle modulation gives the idle a loping mechanical pulse
            // while retaining a seamless two-second loop.
            var pulse = 1f - roughness + roughness *
                        (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 4f * time));
            value = (float)Math.Tanh(value * 0.82f) * pulse;
            samples[index] = value;
            peak = Mathf.Max(peak, Mathf.Abs(value));
        }

        var scale = peak > 0f ? 0.72f / peak : 1f;
        for (var index = 0; index < samples.Length; index++)
            samples[index] *= scale;

        var clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural engine audio.");
        }
        return clip;
    }
}
