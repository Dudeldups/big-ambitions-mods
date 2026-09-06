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
    private const float PitchOffset = 0.60f;
    private const float PitchRange = 0.95f;
    private const float BaseVolume = 0.28f;
    private const float VolumeRange = 0.35f;
    private const float MaxDistortion = 0.08f;
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
        // this Audi's component values; do not replace clips or bypass player volume.
        engineSound.pitchOffset = PitchOffset;
        engineSound.pitchRange = PitchRange;
        engineSound.baseVolume = BaseVolume;
        engineSound.volumeRange = VolumeRange;
        engineSound.maxDistortion = MaxDistortion;
        sounds.lowPassFrequency = CabinLowPass;

        // The native camera events apply/reset cabin filtering. Refresh once if
        // initialization happens while already inside, including after save load.
        RefreshActiveCabinFilter();

        var source = engineSound.source;
        Info($"configured pitchOffset={F(PitchOffset)} pitchRange={F(PitchRange)} " +
             $"baseVolume={F(BaseVolume)} volumeRange={F(VolumeRange)} maxDistortion={F(MaxDistortion)} " +
             $"cabinLowPass={F(CabinLowPass)} clip='{source.clip.name}' " +
             $"length={F(source.clip.length)}s channels={source.clip.channels} hz={source.clip.frequency} " +
             $"mixer='{source.outputAudioMixerGroup?.audioMixer?.name ?? "none"}' " +
             $"group='{source.outputAudioMixerGroup?.name ?? "none"}'.");
        return true;
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
        Info($"sample reason={reason} controlled={vehicle!.controlledByPlayer} " +
             $"cameraInside={physics.CameraInsideVehicle} rpmEstimate={F(engine.RPMPercent * engine.revLimiterRPM)} " +
             $"rpmPercent={F(engine.RPMPercent)} throttle={F(engine.ThrottlePosition)} load={F(engine.Load)} " +
             $"playing={source.isPlaying} pitch={F(source.pitch)} volume={F(source.volume)} " +
             $"masterVolume={F(sounds!.masterVolume)} listenerVolume={F(AudioListener.volume)} " +
             $"mixer='{mixer?.name ?? "none"}' cutoff={ReadMixer(mixer, "lowPassFrequency")} " +
             $"Q={ReadMixer(mixer, "lowPassQ")} distortion={ReadMixer(mixer, "engineDistortion")} " +
             $"attenuation={ReadMixer(mixer, "attenuation")} " +
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
        if (original == null || engineSound == null || sounds == null)
            return;
        original.Restore(engineSound, sounds);
        RefreshActiveCabinFilter();
    }

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
