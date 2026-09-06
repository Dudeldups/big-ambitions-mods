#nullable enable
using System;
using System.Globalization;
using BAModAPI;
using NWH.VehiclePhysics2.Sound;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.Audio;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class AudiRS6RAudioController : MonoBehaviour
{
    private const float PitchOffset = 0.20f;
    private const float PitchRange = 2.08f;
    private const float BaseVolume = 0.28f;
    private const float VolumeRange = 0.35f;
    private const float MaxDistortion = 0f;
    private const float CabinLowPass = 6500f;
    private const int MaximumAttempts = 20;
    private const float SampleInterval = 5f;

    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private SoundManager? sounds;
    private EngineRunningComponent? engineSound;
    private ModContext? context;
    private OriginalSettings? original;
    private int attempts;
    private float nextAttempt;
    private float nextSample;
    private int lastState = -1;
    private bool failureReported;
    private bool sourceMissingReported;
    private AudioSource? loopSource;
    private AudioClip? originalClip;
    private AudioClip? smoothLoop;
    private AudiRS6RAudioComparison? comparison;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    private void LateUpdate()
    {
        if (vehicle == null)
            return;
        try
        {
            if (original == null)
            {
                if (attempts >= MaximumAttempts || Time.unscaledTime < nextAttempt)
                    return;
                attempts++;
                nextAttempt = Time.unscaledTime + 0.5f;
                if (!TryConfigure())
                {
                    if (attempts == 1 || attempts == MaximumAttempts)
                        Warn($"waiting for engine audio source; attempt={attempts}/{MaximumAttempts}.");
                    return;
                }
                // Read actual output after the next native engine-audio update.
                return;
            }

            if (physics == null || sounds == null || engineSound == null)
                return;
            comparison ??= new AudiRS6RAudioComparison(Info, Warn);
            comparison.Update(engineSound.source, vehicle.controlledByPlayer);
            var state = (vehicle.controlledByPlayer ? 1 : 0) | (physics.CameraInsideVehicle ? 2 : 0);
            var stateChanged = state != lastState;
            if (stateChanged || (vehicle.controlledByPlayer && Time.unscaledTime >= nextSample))
            {
                lastState = state;
                nextSample = Time.unscaledTime + SampleInterval;
                LogSample(stateChanged ? "state-change" : "driving");
            }
        }
        catch (Exception ex)
        {
            comparison?.Dispose();
            if (!failureReported)
            {
                failureReported = true;
                Warn($"audio tuning/diagnostics failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        sounds = physics?.soundManager;
        engineSound = sounds?.engineRunningComponent;
        if (engineSound?.source == null || engineSound.source.clip == null || sounds == null)
            return false;

        original = new OriginalSettings(engineSound, sounds);
        Info($"before pitchOffset={F(engineSound.pitchOffset)} pitchRange={F(engineSound.pitchRange)} " +
             $"baseVolume={F(engineSound.baseVolume)} volumeRange={F(engineSound.volumeRange)} " +
             $"maxDistortion={F(engineSound.maxDistortion)} cabinLowPass={F(sounds.lowPassFrequency)}.");

        // Keep the native RPM/load calculation and game audio routing. Tune only
        // this Audi's component values without bypassing player volume controls.
        engineSound.pitchOffset = PitchOffset;
        engineSound.pitchRange = PitchRange;
        engineSound.baseVolume = BaseVolume;
        engineSound.volumeRange = VolumeRange;
        engineSound.maxDistortion = MaxDistortion;
        sounds.lowPassFrequency = CabinLowPass;
        SmoothLoopJoin(engineSound.source);

        // The native camera events apply/reset cabin filtering. Refresh once if
        // initialization happens while already inside, including after save load.
        RefreshActiveCabinFilter();

        var source = engineSound.source;
        Info($"configured revision=4 pitchOffset={F(PitchOffset)} pitchRange={F(PitchRange)} " +
             $"baseVolume={F(BaseVolume)} volumeRange={F(VolumeRange)} maxDistortion={F(MaxDistortion)} " +
             $"cabinLowPass={F(CabinLowPass)} clip='{source.clip.name}' " +
             $"length={F(source.clip.length)}s channels={source.clip.channels} hz={source.clip.frequency} " +
             $"mixer='{source.outputAudioMixerGroup?.audioMixer?.name ?? "none"}' " +
             $"group='{source.outputAudioMixerGroup?.name ?? "none"}'.");
        return true;
    }

    private void SmoothLoopJoin(AudioSource source)
    {
        var clip = source.clip;
        // Bound the one-time allocation and leave streaming/unreadable clips alone.
        if (clip.length > 30f || clip.channels < 1 || clip.channels > 8 ||
            clip.loadType == AudioClipLoadType.Streaming)
        {
            Warn($"loop repair skipped for unsupported clip '{clip.name}'.");
            return;
        }
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0))
        {
            Warn($"loop repair could not read PCM data from '{clip.name}'; retaining original clip.");
            return;
        }

        double stepEnergy = 0;
        double seamEnergy = 0;
        for (var i = clip.channels; i < samples.Length; i++)
        {
            var delta = samples[i] - samples[i - clip.channels];
            stepEnergy += delta * delta;
        }
        for (var channel = 0; channel < clip.channels; channel++)
        {
            var delta = samples[channel] - samples[samples.Length - clip.channels + channel];
            seamEnergy += delta * delta;
        }
        var stepRms = (float)Math.Sqrt(stepEnergy / Math.Max(1, samples.Length - clip.channels));
        var seamRms = (float)Math.Sqrt(seamEnergy / clip.channels);
        Info($"loop inspection clip='{clip.name}' seamRms={F(seamRms)} " +
             $"adjacentStepRms={F(stepRms)} seamRatio={F(seamRms / Mathf.Max(stepRms, 0.000001f))}.");

        var fadeFrames = Math.Min((int)(clip.frequency * 0.02f), clip.samples / 4);
        if (fadeFrames < 2 || seamRms < stepRms * 4f || seamRms < 0.001f)
        {
            Info("loop repair unnecessary; retaining original clip.");
            return;
        }

        // Overlap the final 20 ms with the beginning, then wrap into the sample
        // immediately after that beginning. Both ends of the join remain continuous.
        var outputFrames = clip.samples - fadeFrames;
        var middleFrames = clip.samples - 2 * fadeFrames;
        var output = new float[outputFrames * clip.channels];
        Array.Copy(samples, fadeFrames * clip.channels, output, 0, middleFrames * clip.channels);
        for (var frame = 0; frame < fadeFrames; frame++)
        {
            var blend = (float)frame / (fadeFrames - 1);
            for (var channel = 0; channel < clip.channels; channel++)
                output[(middleFrames + frame) * clip.channels + channel] = Mathf.Lerp(
                    samples[(outputFrames + frame) * clip.channels + channel],
                    samples[frame * clip.channels + channel], blend);
        }

        var repaired = AudioClip.Create("AudiRS6R_Car_SmoothLoop", outputFrames, clip.channels, clip.frequency, false);
        if (!repaired.SetData(output, 0))
        {
            Destroy(repaired);
            Warn("loop repair could not write PCM data; retaining original clip.");
            return;
        }
        loopSource = source;
        originalClip = clip;
        smoothLoop = repaired;
        ReplaceClip(source, repaired);
        var repairedSeam = 0f;
        for (var channel = 0; channel < clip.channels; channel++)
            repairedSeam = Mathf.Max(repairedSeam,
                Mathf.Abs(output[channel] - output[output.Length - clip.channels + channel]));
        Info($"loop repaired fadeMs={F(1000f * fadeFrames / clip.frequency)} " +
             $"length={F(repaired.length)}s boundaryPeak={F(repairedSeam)}; source asset unchanged.");
    }

    private static void ReplaceClip(AudioSource source, AudioClip clip)
    {
        var wasPlaying = source.isPlaying;
        var position = source.time;
        source.clip = clip;
        source.time = position % clip.length;
        if (wasPlaying)
            source.Play();
    }

    private void LogSample(string reason)
    {
        var source = engineSound?.source;
        if (source == null)
        {
            if (!sourceMissingReported)
            {
                sourceMissingReported = true;
                Warn("configured engine audio source is missing.");
            }
            return;
        }
        sourceMissingReported = false;
        var engine = physics!.powertrain.engine;
        var mixer = source.outputAudioMixerGroup?.audioMixer;
        var lowPass = source.GetComponent<AudioLowPassFilter>();
        Info($"sample reason={reason} comparison={comparison?.Mode ?? "mixer/rpm"} controlled={vehicle!.controlledByPlayer} " +
             $"{comparison?.PlaybackStatus ?? "dry=inactive"} " +
             $"cameraInside={physics.CameraInsideVehicle} rpmEstimate={F(engine.RPMPercent * engine.revLimiterRPM)} " +
             $"rpmPercent={F(engine.RPMPercent)} throttle={F(engine.ThrottlePosition)} load={F(engine.Load)} " +
             $"playing={source.isPlaying} pitch={F(source.pitch)} volume={F(source.volume)} " +
             $"clip='{source.clip?.name ?? "none"}' repairedLoopActive={smoothLoop != null && source.clip == smoothLoop} " +
             $"loop={source.loop} doppler={F(source.dopplerLevel)} " +
             $"masterVolume={F(sounds!.masterVolume)} listenerVolume={F(AudioListener.volume)} " +
             $"mixer='{mixer?.name ?? "none"}' cutoff={ReadMixer(mixer, "lowPassFrequency")} " +
             $"Q={ReadMixer(mixer, "lowPassQ")} distortion={ReadMixer(mixer, "engineDistortion")} " +
             $"attenuation={ReadMixer(mixer, "attenuation")} " +
             $"engineDb={ReadMixer(mixer, "engine")} fxDb={ReadMixer(mixer, "fx")} " +
             $"sourceLowPass={(lowPass != null && lowPass.enabled ? F(lowPass.cutoffFrequency) : "off")} " +
             $"spatialBlend={F(source.spatialBlend)} minDistance={F(source.minDistance)} maxDistance={F(source.maxDistance)}.");
    }

    private static string ReadMixer(AudioMixer? mixer, string parameter) =>
        mixer != null && mixer.GetFloat(parameter, out var value) ? F(value) : "unavailable";

    private void RefreshActiveCabinFilter()
    {
        // Cabin filtering is a native shared-mixer effect. Change its cutoff only
        // while this Audi is the occupied interior view; leave other mix controls alone.
        if (vehicle != null && vehicle.controlledByPlayer && physics != null &&
            physics.CameraInsideVehicle && sounds?.mixer != null &&
            !sounds.mixer.SetFloat("lowPassFrequency", sounds.lowPassFrequency))
            Warn("Active cabin mixer has no exposed lowPassFrequency parameter.");
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void Info(string message) =>
        context?.Logger.Info($"AudiRS6R audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void Warn(string message) =>
        context?.Logger.Warn($"AudiRS6R audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        comparison?.Dispose();
        if (loopSource != null && originalClip != null && loopSource.clip == smoothLoop)
            ReplaceClip(loopSource, originalClip);
        if (smoothLoop != null)
            Destroy(smoothLoop);
        if (original == null || engineSound == null || sounds == null)
            return;
        original.Restore(engineSound, sounds);
        RefreshActiveCabinFilter();
    }

    private void OnDisable() => comparison?.Dispose();

    private sealed class OriginalSettings
    {
        private readonly float pitchOffset;
        private readonly float pitchRange;
        private readonly float baseVolume;
        private readonly float volumeRange;
        private readonly float maxDistortion;
        private readonly float lowPassFrequency;

        public OriginalSettings(EngineRunningComponent engine, SoundManager manager)
        {
            pitchOffset = engine.pitchOffset;
            pitchRange = engine.pitchRange;
            baseVolume = engine.baseVolume;
            volumeRange = engine.volumeRange;
            maxDistortion = engine.maxDistortion;
            lowPassFrequency = manager.lowPassFrequency;
        }

        public void Restore(EngineRunningComponent engine, SoundManager manager)
        {
            engine.pitchOffset = pitchOffset;
            engine.pitchRange = pitchRange;
            engine.baseVolume = baseVolume;
            engine.volumeRange = volumeRange;
            engine.maxDistortion = maxDistortion;
            manager.lowPassFrequency = lowPassFrequency;
        }
    }
}
