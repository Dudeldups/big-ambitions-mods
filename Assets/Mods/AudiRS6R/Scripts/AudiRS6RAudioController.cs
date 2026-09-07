#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.Audio;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class AudiRS6RAudioController : MonoBehaviour
{
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private readonly AudiRS6RPopGate popGate = new AudiRS6RPopGate();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? popSource;
    private AudioClip[]? popClips;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted;
    private bool busMutedReported;
    private int lastDecision;
    private float nextDecisionLog;
    private int attempts, popCount, lastState = -1;
    private float nextAttempt, nextLog, nextPopLog, smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;

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
                    if (attempts == 1 || attempts == 20)
                        Warn($"waiting for native engine audio; attempt={attempts}/20.");
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
        if (native == null || native.clip == null || native.outputAudioMixerGroup == null || context == null)
            return false;
        originalDistortion = engineSound!.maxDistortion;
        body = vehicle.GetComponent<Rigidbody>();
        if (body == null) Warn("vehicle Rigidbody unavailable; speed-dependent downshift pops will remain silent.");
        audioHost = new GameObject("AudiRS6R_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;
        // Borrow the original Car clip without processing or taking ownership.
        idleSource = CreateSource(audioHost, native.clip, true);
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i]);
            layers[i] = CreateSource(audioHost, clip, true);
            var loaded = LoadClip(EngineNames[i]+"Load");
            layers[i + 3] = CreateSource(audioHost, loaded, true);
            Info($"loaded layer={i} source=generated coast='{clip.name}' load='{loaded.name}' secondsCoast={F(clip.length)} secondsLoad={F(loaded.length)} " +
                 $"referenceHz={F(AudiRS6RAudioModel.ReferenceHz(i))}.");
        }
        popClips = new[] { LoadClip("ExhaustPop1"), LoadClip("ExhaustPop2"), LoadClip("ExhaustPop3") };
        var exhaustHost = new GameObject("ExhaustPops");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        popSource = CreateSource(exhaustHost, popClips[0], false);
        engineSound.maxDistortion = 0f;
        configured = true;
        Info($"configured revision=14 idle='{idleSource.clip.name}' idlePitch=1 idleVolume=0.24 " +
             $"driving=generatedGrowlWithBite targetHz=80..180 popVolume={F(AudiRS6RAudioModel.PopVolume)} distortion=0 mixer='{native.outputAudioMixerGroup.audioMixer.name}' " +
             $"group='{native.outputAudioMixerGroup.name}' pops=abruptLift/downshiftRpmJump/lowGearUpshift maxBurst=3 liftCooldown=0.8..1.05 driverThrottle=input.Throttle " +
             $"popTone=filteredExhaustBody popPitch={F(AudiRS6RAudioModel.PopPitchMin)}..{F(AudiRS6RAudioModel.PopPitchMax)} nativeFallback='{native.clip.name}'.");
        return true;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = AudiRS6RWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, bool loop)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = native!.outputAudioMixerGroup;
        source.spatialBlend = native.spatialBlend;
        source.minDistance = native.minDistance;
        source.maxDistance = native.maxDistance;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, native.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.rolloffMode = native.rolloffMode;
        source.dopplerLevel = 0f;
        source.priority = native.priority;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || native == null || layers == null || audioHost == null || popSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = native.transform.position;
        var exhaust = physics.soundManager.exhaustSourceGO;
        popSource.transform.position = exhaust != null ? exhaust.transform.position :
            vehicle!.transform.TransformPoint(new Vector3(0f, .4f, -2f));
        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(engine.ThrottlePosition);
            Info($"driver entered: original '{idleSource.clip.name}' idle at pitch=1 volume=0.24; driving=generatedGrowlWithBite; event-driven bursts active.");
        }
        wasControlled = controlled;
        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            // Cancel any scheduled start too; resume schedules all held loops
            // together again instead of leaving a pre-start source paused.
            if (shouldPause) StopLayers();
            // Transients are discarded on pause; never replay a stale pop on resume.
            popSource.Stop();
            popGate.Reset();
            paused = shouldPause;
            Info($"audio pause={paused} timeScale={F(Time.timeScale)} listenerPause={AudioListener.pause}.");
        }
        if (controlled)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();
        if (!running)
        {
            popSource.Stop();
            popGate.Reset();
        }

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var gear = physics.powertrain.transmission.Gear;
        var driverThrottle = Mathf.Clamp01(physics.input.Throttle);
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            var normalized = AudiRS6RAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                AudiRS6RAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = AudiRS6RAudioModel.IdlePitch;
            idleSource.volume = envelope * master * AudiRS6RAudioModel.IdleVolume(driveBlend);
            idleSource.mute = controlled && savedMute;
            var gain = envelope * master * (.24f + .18f * smoothThrottle) * Mathf.Sqrt(driveBlend);
            loadBlend = AudiRS6RAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = AudiRS6RAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * AudiRS6RAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = controlled && savedMute;
            }
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                idleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                voicesStarted = true;
            }
            var pop = popGate.Sample(running && !savedMute, Time.time, rawRpm, driverThrottle, gear, body == null ? 0f : body.velocity.magnitude*3.6f);
            if (popGate.DecisionId != lastDecision)
            {
                lastDecision = popGate.DecisionId;
                if (Time.unscaledTime >= nextDecisionLog)
                {
                    nextDecisionLog = Time.unscaledTime + .5f;
                    Info($"pop event decision={popGate.Decision} chance={F(popGate.Probability)} rpm={F(rawRpm)} " +
                         $"driverThrottle={F(driverThrottle)} fromGear={popGate.FromGear} gear={gear} eventRpm={F(popGate.EventRpm)} rpmJump={F(popGate.RpmJump)} currentBurst={popGate.BurstId} " +
                         $"scheduledPops={(popGate.Decision.EndsWith(":burst") ? popGate.BurstSize : 0)}.");
                }
            }
            if (pop != AudiRS6RPopEvent.None) PlayPop(pop, rawRpm, gear, master);
        }
        var state = (controlled ? 1 : 0) | (running ? 2 : 0) | (paused ? 4 : 0);
        if (state != lastState || (controlled && Time.unscaledTime >= nextLog))
        {
            lastState = state;
            nextLog = Time.unscaledTime + 5f;
            var mixer = layers[0].outputAudioMixerGroup?.audioMixer;
            Info($"sample controlled={controlled} running={running} paused={paused} " +
                 $"rpm={F(rawRpm)} smoothRpm={F(smoothRpm)} engineThrottle={F(engine.ThrottlePosition)} driverThrottle={F(driverThrottle)} gear={gear} " +
                 $"driveBlend={F(driveBlend)} idle='{idleSource.clip.name}'[{Status(idleSource)}] " +
                 $"low[{Status(layers[0])}] mid[{Status(layers[1])}] high[{Status(layers[2])}] pops={popCount} " +
                 $"loadBlend={F(loadBlend)} loadedLow[{Status(layers[3])}] loadedMid[{Status(layers[4])}] loadedHigh[{Status(layers[5])}] " +
                 $"burstActive={popGate.OverrunActive} " +
                 $"engineDb={ReadMixer(mixer, "engine")} fxDb={ReadMixer(mixer, "fx")} " +
                 $"masterDb={ReadMixer(mixer, "attenuation")} listenerVolume={F(AudioListener.volume)}.");
            var busMuted = running && !paused && mixer != null && mixer.GetFloat("engine", out var db) && db <= -79f;
            if (busMuted && !busMutedReported) Warn("engine mixer bus is muted while running; layers may be inaudible.");
            busMutedReported = busMuted;
        }
    }

    private void PlayPop(AudiRS6RPopEvent reason, float rpm, int gear, float master)
    {
        var clip = popClips![UnityEngine.Random.Range(0, popClips.Length)];
        popSource!.clip = clip;
        popSource.pitch = UnityEngine.Random.Range(AudiRS6RAudioModel.PopPitchMin, AudiRS6RAudioModel.PopPitchMax);
        popSource.volume = master * AudiRS6RAudioModel.PopVolume * popGate.Intensity * UnityEngine.Random.Range(.8f, 1.1f);
        popSource.mute = savedMute;
        popSource.PlayOneShot(clip);
        popCount++;
        if (Time.unscaledTime >= nextPopLog)
        {
            nextPopLog = Time.unscaledTime + 2f;
            Info($"exhaust pop reason={reason} burst={popGate.BurstId} intensity={F(popGate.Intensity)} rpm={F(rpm)} gear={gear} clip='{clip.name}' " +
                 $"volume={F(popSource.volume)} pitch={F(popSource.pitch)} playing={popSource.isPlaying} " +
                 $"mute={popSource.mute} minDistance={F(popSource.minDistance)} maxDistance={F(popSource.maxDistance)} " +
                 $"engineDb={ReadMixer(popSource.outputAudioMixerGroup?.audioMixer, "engine")} total={popCount}.");
        }
    }

    private static string Status(AudioSource source) =>
        $"playing={source.isPlaying} mute={source.mute} volume={F(source.volume)} pitch={F(source.pitch)}";
    private static string ReadMixer(AudioMixer? mixer, string parameter) =>
        mixer != null && mixer.GetFloat(parameter, out var value) ? F(value) : "unavailable";
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private void Info(string message) => context?.Logger.Info($"AudiRS6R audio vehicle={vehicle?.GetInstanceID()}: {message}");
    private void Warn(string message) => context?.Logger.Warn($"AudiRS6R audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        voicesStarted = false;
    }

    private void OnDisable()
    {
        StopLayers();
        if (popSource != null) popSource.Stop();
        popGate.Reset();
        RestoreMute();
        if (configured && engineSound != null) engineSound.maxDistortion = originalDistortion;
        envelope = smoothRpm = smoothThrottle = driveBlend = loadBlend = 0f;
        paused = wasControlled = false;
        lastState = -1;
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
        popSource = null;
        popClips = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy() => Cleanup();
}
