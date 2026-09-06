#nullable enable
using System;
using System.Globalization;
using UnityEngine;

// Temporary listening diagnostic: isolate mixer coloration from RPM pitch changes.
// The native source keeps running silently so its timing and lifecycle remain authoritative.
internal sealed class AudiRS6RAudioComparison : IDisposable
{
    private readonly Action<string> info;
    private readonly Action<string> warn;
    private AudioSource? native;
    private AudioSource? dry;
    private bool savedMute;
    private bool ownsMute;
    private bool gainFailureReported;
    private bool wasControlled;
    private bool engineMuteReported;
    private int mode;

    public string Mode => mode == 0 ? "mixer/rpm" : mode == 1 ? "dry/rpm" : "dry/1x";

    public string PlaybackStatus => dry == null || mode == 0 ? "dry=inactive" :
        $"dryPlaying={dry.isPlaying} dryMuted={dry.mute} dryVolume={F(dry.volume)} " +
        $"dryPitch={F(dry.pitch)} nativeMuted={native?.mute} listenerPaused={AudioListener.pause}";

    public AudiRS6RAudioComparison(Action<string> info, Action<string> warn)
    {
        this.info = info;
        this.warn = warn;
    }

    public void Update(AudioSource? source, bool controlled)
    {
        if (!controlled || source == null || source.clip == null)
        {
            if (wasControlled)
                info("comparison reset to mixer/rpm on driver exit or missing source.");
            wasControlled = false;
            mode = 0;
            ReleaseMute();
            if (dry != null)
                dry.Stop();
            return;
        }
        if (!wasControlled)
            info("comparison ready mode=mixer/rpm; Ctrl+Alt+A cycles mixer/rpm -> dry/rpm -> dry/1x -> mixer/rpm.");
        wasControlled = true;
        if (native != source)
        {
            ReleaseMute();
            if (dry != null)
                UnityEngine.Object.Destroy(dry.gameObject);
            dry = null;
            native = source;
            mode = 0;
        }

        var changed = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                      (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.A);
        if (changed)
        {
            mode = (mode + 1) % 3;
            info($"comparison selected mode={Mode} nativePitch={F(source.pitch)} nativeVolume={F(source.volume)}.");
        }
        if (mode == 0)
        {
            ReleaseMute();
            if (dry != null)
                dry.Stop();
            return;
        }

        // This diagnostic bypasses the engine bus gain as well as its DSP. Its
        // -80 dB state otherwise silences the very source we need to audition.
        // Keep the live FX/Master gains, which carry the user's volume settings.
        // Do not unmute or modify the shared native mixer itself.
        var mixer = source.outputAudioMixerGroup?.audioMixer;
        if (mixer == null || !mixer.GetFloat("fx", out var fxDb) ||
            !mixer.GetFloat("attenuation", out var masterDb))
        {
            if (!gainFailureReported)
            {
                gainFailureReported = true;
                warn("comparison unavailable: cannot read native mixer gains; retaining normal playback.");
            }
            mode = 0;
            ReleaseMute();
            if (dry != null)
                dry.Stop();
            return;
        }
        gainFailureReported = false;
        var hasEngineGain = mixer.GetFloat("engine", out var engineGain);
        var engineDb = hasEngineGain ? F(engineGain) : "unavailable";
        var engineMuted = hasEngineGain && engineGain <= -79f;
        if (engineMuted && !engineMuteReported)
            info("comparison bypassing muted engine bus for dry audition; FX/Master/listener volume and pause still apply. " +
                 "Normal and dry playback are not level-matched while that bus is muted.");
        engineMuteReported = engineMuted;
        if (dry == null)
        {
            var host = new GameObject("AudiRS6R_DryAudioComparison");
            host.transform.SetParent(source.transform, false);
            dry = host.AddComponent<AudioSource>();
            dry.playOnAwake = false;
            dry.outputAudioMixerGroup = null;
            dry.bypassEffects = true;
            dry.bypassListenerEffects = true;
            dry.bypassReverbZones = true;
            dry.ignoreListenerVolume = source.ignoreListenerVolume;
            dry.ignoreListenerPause = source.ignoreListenerPause;
            dry.SetCustomCurve(AudioSourceCurveType.CustomRolloff, source.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            dry.SetCustomCurve(AudioSourceCurveType.SpatialBlend, source.GetCustomCurve(AudioSourceCurveType.SpatialBlend));
            dry.SetCustomCurve(AudioSourceCurveType.Spread, source.GetCustomCurve(AudioSourceCurveType.Spread));
            dry.rolloffMode = source.rolloffMode;
            info($"comparison dry source created; native mixer='{mixer.name}' group='{source.outputAudioMixerGroup?.name ?? "none"}', " +
                 "direct output with engine gain bypassed, mirrored FX/Master gains and native spatial settings.");
        }
        if (!ownsMute)
        {
            savedMute = source.mute;
            ownsMute = true;
        }
        source.mute = true;
        dry.mute = savedMute || fxDb <= -79f || masterDb <= -79f;
        dry.volume = CalculateDryVolume(source.volume, fxDb, masterDb);
        dry.pitch = mode == 2 ? 1f : source.pitch;
        dry.loop = source.loop;
        dry.spatialBlend = source.spatialBlend;
        dry.minDistance = source.minDistance;
        dry.maxDistance = source.maxDistance;
        dry.spread = source.spread;
        dry.panStereo = source.panStereo;
        dry.dopplerLevel = source.dopplerLevel;
        dry.priority = source.priority;
        if (dry.clip != source.clip)
            dry.clip = source.clip;
        if (!source.isPlaying || Time.timeScale == 0f)
            dry.Stop();
        else if (!dry.isPlaying || changed)
        {
            dry.timeSamples = source.timeSamples;
            dry.Play();
        }
        if (changed)
            info($"comparison active mode={Mode} {PlaybackStatus} engineGainBypassed=True " +
                 $"engineDb={engineDb} fxDb={F(fxDb)} masterDb={F(masterDb)} clip='{dry.clip.name}'.");
    }

    internal static float CalculateDryVolume(float sourceVolume, float fxDb, float masterDb)
    {
        if (fxDb <= -79f || masterDb <= -79f)
            return 0f;
        return (float)Math.Max(0d, Math.Min(1d, sourceVolume * Math.Pow(10d, (fxDb + masterDb) / 20d)));
    }

    private void ReleaseMute()
    {
        if (ownsMute && native != null)
            native.mute = savedMute;
        ownsMute = false;
    }

    public void Dispose()
    {
        ReleaseMute();
        if (dry != null)
        {
            dry.Stop();
            UnityEngine.Object.Destroy(dry.gameObject);
        }
        dry = null;
        mode = 0;
        wasControlled = false;
        engineMuteReported = false;
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
