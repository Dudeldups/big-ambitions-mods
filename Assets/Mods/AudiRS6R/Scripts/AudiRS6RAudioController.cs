#nullable enable
using System;
using System.Globalization;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.Audio;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class AudiRS6RAudioController : MonoBehaviour
{
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private float originalDistortion;
    private bool savedMute;
    private bool ownsMute;
    private bool configured;
    private bool failed;
    private bool paused;
    private bool wasControlled;
    private bool busMutedReported;
    private int attempts;
    private int lastState = -1;
    private float nextAttempt;
    private float nextLog;
    private float driveBlend;
    private float envelope;

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
            Warn($"layered audio failed; native sound restored: {ex.GetType().Name}: {ex.Message}");
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
        audioHost = new GameObject("AudiRS6R_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;
        // Both layers borrow the bundled Car clip; its lifetime belongs to the game.
        layers = new[] { CreateLayer(native.clip), CreateLayer(native.clip) };
        engineSound.maxDistortion = 0f;
        configured = true;
        Info($"configured revision=7 idle='{native.clip.name}' idlePitch=1 idleVolume=0.24 driving='{native.clip.name}' " +
             $"mixer='{native.outputAudioMixerGroup.audioMixer.name}' group='{native.outputAudioMixerGroup.name}'. " +
             "drivingPitch=0.65..2.1 distortion=0; original Car clip used for both layers.");
        return true;
    }

    private AudioSource CreateLayer(AudioClip clip)
    {
        var source = audioHost!.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
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
        if (physics == null || native == null || layers == null || audioHost == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = native.transform.position;
        var controlled = vehicle!.controlledByPlayer;
        if (controlled && !wasControlled)
            Info($"driver entered: RPM-driven audio active using '{layers[1].clip.name}'; idle retained at original pitch.");
        wasControlled = controlled;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            foreach (var source in layers)
                if (shouldPause) source.Pause(); else source.UnPause();
            paused = shouldPause;
            Info($"audio pause={paused} timeScale={F(Time.timeScale)} listenerPause={AudioListener.pause}.");
        }
        if (controlled)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();

        if (!paused)
        {
            var rpm = Mathf.Clamp01(engine.RPMPercent);
            var idle = engine.idleRPM / Mathf.Max(1f, engine.revLimiterRPM);
            var targetBlend = Mathf.InverseLerp(idle + .04f, idle + .22f, rpm);
            driveBlend = Mathf.MoveTowards(driveBlend, targetBlend, Time.deltaTime * 4f);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var gain = envelope * Mathf.Clamp01(physics.soundManager.masterVolume);
            var driveVolume = gain * (.26f + .18f * Mathf.Clamp01(engine.ThrottlePosition));
            // Keep the driving response separate from the original-pitch idle layer.
            var pitch = DrivingPitch(rpm, idle);
            layers[0].pitch = 1f;
            layers[0].volume = gain * .24f * Mathf.Sqrt(1f - driveBlend);
            layers[1].pitch = pitch;
            layers[1].volume = driveVolume * Mathf.Sqrt(driveBlend);
            foreach (var source in layers)
            {
                source.mute = controlled && savedMute;
                if (envelope <= 0f) source.Stop();
                else if (!source.isPlaying) source.Play();
            }
        }
        var state = (controlled ? 1 : 0) | (running ? 2 : 0) | (paused ? 4 : 0);
        if (state != lastState || (controlled && Time.unscaledTime >= nextLog))
        {
            lastState = state;
            nextLog = Time.unscaledTime + 5f;
            var mixer = layers[0].outputAudioMixerGroup?.audioMixer;
            Info($"sample controlled={controlled} running={running} paused={paused} timeScale={F(Time.timeScale)} " +
                 $"rpmEstimate={F(engine.RPMPercent * engine.revLimiterRPM)} throttle={F(engine.ThrottlePosition)} " +
                 $"driveBlend={F(driveBlend)} drivingClip='{layers[1].clip.name}' " +
                 $"idle[{Status(layers[0])}] driving[{Status(layers[1])}] " +
                 $"engineDb={ReadMixer(mixer, "engine")} fxDb={ReadMixer(mixer, "fx")} " +
                 $"masterDb={ReadMixer(mixer, "attenuation")} listenerVolume={F(AudioListener.volume)}.");
            var busMuted = running && !paused && mixer != null && mixer.GetFloat("engine", out var db) && db <= -79f;
            if (busMuted && !busMutedReported) Warn("engine mixer bus is muted while running; layers may be inaudible.");
            busMutedReported = busMuted;
        }
    }

    internal static float DrivingPitch(float rpmPercent, float idlePercent)
    {
        var normalized = Math.Max(0d, Math.Min(1d, (rpmPercent - idlePercent) / Math.Max(.01d, 1d - idlePercent)));
        return (float)(.65d + 1.45d * normalized);
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

    private void OnDisable()
    {
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        RestoreMute();
        envelope = driveBlend = 0f;
        paused = wasControlled = false;
        lastState = -1;
    }

    private void Cleanup()
    {
        OnDisable();
        if (configured && engineSound != null) engineSound.maxDistortion = originalDistortion;
        configured = false;
        if (audioHost != null) Destroy(audioHost);
        audioHost = null;
        layers = null;
    }

    private void OnDestroy() => Cleanup();
}
