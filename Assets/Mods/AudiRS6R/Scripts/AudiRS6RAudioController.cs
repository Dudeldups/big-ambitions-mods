#nullable enable
using System;
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
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioClip? drivingClip;
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
        var wave = AudiRS6RWave.Read(Path.Combine(context.ModRootPath, "Config", "Audio", "AudiDrivingLoop.wav"));
        // This asset is already pitch-stabilized and joined; do not crossfade its
        // seam again or load the audition preview containing a deliberate pause.
        drivingClip = AudioClip.Create("AudiDrivingLoop", wave.Samples.Length / wave.Channels,
            wave.Channels, wave.Frequency, false);
        if (!drivingClip.SetData(wave.Samples, 0)) throw new InvalidDataException("Cannot create driving loop.");
        Info($"loaded prepared driving loop frames={drivingClip.samples} length={F(drivingClip.length)}s " +
             $"hz={drivingClip.frequency} channels={drivingClip.channels} additionalProcessing=none.");
        audioHost = new GameObject("AudiRS6R_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;
        layers = new[] { CreateLayer(native.clip), CreateLayer(drivingClip) };
        engineSound.maxDistortion = 0f;
        configured = true;
        Info($"configured revision=6 idle='{native.clip.name}' idlePitch=1 idleVolume=0.24 driving=AudiDrivingLoop " +
             $"mixer='{native.outputAudioMixerGroup.audioMixer.name}' group='{native.outputAudioMixerGroup.name}'. " +
             "drivingPitch=0.65..2.1; Car idle retained. Clip-switch shortcut removed.");
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
            Info("driver entered: prepared RPM-driven loop active; idle=Car at original pitch.");
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
            // The steady loop has no complete rev sequence to fight the game RPM.
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
                 $"driveBlend={F(driveBlend)} drivingClip=AudiDrivingLoop " +
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
        if (drivingClip != null) Destroy(drivingClip);
        drivingClip = null;
    }

    private void OnDestroy() => Cleanup();
}
