#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class CadillacEscaladeAudioController : MonoBehaviour
{
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? crackleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioClip? crackleClip;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted;
    private int attempts;
    private float nextAttempt, smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    private void LateUpdate()
    {
        if (vehicle == null || failed) return;
        try
        {
            if (!configured)
            {
                if (attempts >= 20 || Time.unscaledTime < nextAttempt) return;
                attempts++;
                nextAttempt = Time.unscaledTime + .5f;
                if (!TryConfigure())
                {
                    if (attempts == 20)
                        Warn("vehicle audio state unavailable after 20 attempts; custom audio was not initialized.");
                    return;
                }
            }
            UpdatePlayback();
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"layered audio failed; native Car sound restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (physics == null || context == null)
            return false;
        var engineTemplate = native ??
                             physics.soundManager.exhaustSourceGO?.GetComponent<AudioSource>() ??
                             physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (engineSound != null)
            originalDistortion = engineSound.maxDistortion;
        audioHost = new GameObject("CadillacEscalade_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        if (engineTemplate != null)
            audioHost.transform.position = engineTemplate.transform.position;
        else
            audioHost.transform.localPosition = new Vector3(0f, 0.70f, 0.55f);
        layers = new AudioSource[6];
        AudioClip? idleClip = null;
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i]);
            if (i == 0)
                idleClip = clip;
            layers[i] = CreateSource(audioHost, clip, true, engineTemplate);
            var loaded = LoadClip(EngineNames[i]+"Load");
            layers[i + 3] = CreateSource(audioHost, loaded, true, engineTemplate);
        }
        // Use the Escalade low-frequency recording as the idle bed. Dealer and
        // freshly loaded vehicles do not consistently expose the donor Car
        // source yet, so custom audio must not depend on that source or clip.
        idleSource = CreateSource(audioHost, idleClip!, true, engineTemplate);
        crackleClip = CadillacEscaladeCrackleWave.Create();
        var exhaustHost = new GameObject("CadillacEscalade_ExhaustCrackle");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        crackleSource = CreateSource(exhaustHost, crackleClip, true, engineTemplate);
        ConfigureCrackleFilters(exhaustHost);
        var hornHost = new GameObject("CadillacEscalade_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var otherSource = physics!.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (otherSource == null || otherSource.outputAudioMixerGroup == null) otherSource = native;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, otherSource);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, otherSource);
        if (engineSound != null)
            engineSound.maxDistortion = 0f;
        configured = true;
        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"engineLayers=7, engineGain={CadillacEscaladeAudioModel.EngineBaseVolume:0.00}.." +
            $"{CadillacEscaladeAudioModel.EngineBaseVolume + CadillacEscaladeAudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{CadillacEscaladeAudioModel.HornLowVolume:0.00}/" +
            $"{CadillacEscaladeAudioModel.HornHighVolume:0.00}, " +
            $"sourceDistance={(native != null ? native.minDistance : 2f):0.0}.." +
            $"{(native != null ? native.maxDistance : 55f):0.0}, " +
            "exhaust=continuous-subtle-crackle.");
        return true;
    }

    private static void ConfigureCrackleFilters(GameObject host)
    {
        var lowPass = host.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 4800f;
        lowPass.lowpassResonanceQ = 1.05f;
        var highPass = host.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 420f;
        highPass.highpassResonanceQ = 1.02f;
        var distortion = host.AddComponent<AudioDistortionFilter>();
        distortion.distortionLevel = .015f;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = CadillacEscaladeWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, AudioSource? template = null)
    {
        template ??= native;
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        if (template != null)
        {
            source.outputAudioMixerGroup = template.outputAudioMixerGroup;
            source.spatialBlend = template.spatialBlend;
            source.minDistance = template.minDistance;
            source.maxDistance = template.maxDistance;
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            source.rolloffMode = template.rolloffMode;
            source.priority = template.priority;
        }
        else
        {
            source.spatialBlend = 1f;
            source.minDistance = 2f;
            source.maxDistance = 55f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 128;
        }
        source.dopplerLevel = 0f;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || layers == null || audioHost == null || crackleSource == null ||
            hornSource == null || hornSupportSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        if (native != null)
            audioHost.transform.position = native.transform.position;
        var exhaust = physics.soundManager.exhaustSourceGO;
        crackleSource.transform.position = exhaust != null ? exhaust.transform.position :
            vehicle!.transform.TransformPoint(new Vector3(0f, .4f, -2f));
        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(engine.ThrottlePosition);
        }
        wasControlled = controlled;
        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            // Cancel any scheduled start too; resume schedules all held loops
            // together again instead of leaving a pre-start source paused.
            if (shouldPause) StopLayers();
            paused = shouldPause;
        }
        if (controlled && native != null)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        if (controlled && !paused)
            EnsureOwnedSourcesEnabled();
        UpdateHorn(controlled && !paused && physics.input.Horn, Mathf.Clamp01(physics.soundManager.masterVolume));
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            var normalized = CadillacEscaladeAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                CadillacEscaladeAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = CadillacEscaladeAudioModel.IdlePitch;
            idleSource.volume = envelope * master * CadillacEscaladeAudioModel.IdleVolume(driveBlend);
            idleSource.mute = controlled && savedMute;
            var gain = envelope * master * CadillacEscaladeAudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            loadBlend = CadillacEscaladeAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = CadillacEscaladeAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * CadillacEscaladeAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = controlled && savedMute;
            }
            var crackleLoad = Mathf.SmoothStep(0f, 1f, smoothThrottle);
            crackleSource.pitch = Mathf.Lerp(.90f, 1.22f, normalized);
            crackleSource.volume = envelope * master * Mathf.Lerp(
                CadillacEscaladeAudioModel.CrackleIdleVolume,
                CadillacEscaladeAudioModel.CrackleLoadVolume,
                crackleLoad);
            crackleSource.mute = controlled && savedMute;
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                idleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                crackleSource.PlayScheduled(start);
                voicesStarted = true;
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * CadillacEscaladeAudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * CadillacEscaladeAudioModel.HornHighVolume : 0f;
        UpdateHornVoice(hornSource, pressed, lowTarget);
        UpdateHornVoice(hornSupportSource, pressed, highTarget);
    }

    private void EnsureOwnedSourcesEnabled()
    {
        if (idleSource != null)
            idleSource.enabled = true;
        if (layers != null)
            foreach (var source in layers)
                if (source != null) source.enabled = true;
        if (crackleSource != null)
            crackleSource.enabled = true;
        if (hornSource != null)
            hornSource.enabled = true;
        if (hornSupportSource != null)
            hornSupportSource.enabled = true;
    }

    private static void UpdateHornVoice(AudioSource source, bool pressed, float target)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !source.isPlaying)
            source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void Warn(string message) => context?.Logger.Warn($"CadillacEscalade audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        if (crackleSource != null) crackleSource.Stop();
        voicesStarted = false;
    }

    private void OnDisable()
    {
        StopLayers();
        if (hornSource != null)
        {
            hornSource.Stop();
            hornSource.volume = 0f;
        }
        if (hornSupportSource != null)
        {
            hornSupportSource.Stop();
            hornSupportSource.volume = 0f;
        }
        RestoreMute();
        if (configured && engineSound != null) engineSound.maxDistortion = originalDistortion;
        envelope = smoothRpm = smoothThrottle = driveBlend = loadBlend = 0f;
        paused = wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && engineSound != null) engineSound.maxDistortion = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null) Destroy(audioHost);
        audioHost = null;
        layers = null;
        idleSource = null;
        crackleSource = null;
        hornSource = null;
        hornSupportSource = null;
        if (crackleClip != null) Destroy(crackleClip);
        crackleClip = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
