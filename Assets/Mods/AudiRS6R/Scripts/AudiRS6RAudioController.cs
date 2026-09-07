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
    private readonly AudioClip?[] drivingClips = new AudioClip?[2];
    private float originalDistortion;
    private bool savedMute;
    private bool ownsMute;
    private bool configured;
    private bool failed;
    private bool paused;
    private bool wasControlled;
    private bool busMutedReported;
    private int selected;
    private int attempts;
    private int lastState = -1;
    private float nextAttempt;
    private float nextLog;
    private float driveBlend;
    private float selectionBlend;
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
        for (var i = 0; i < 2; i++)
        {
            var name = i == 0 ? "Audi2" : "Audi3";
            var wave = AudiRS6RWave.Read(Path.Combine(context.ModRootPath, "Config", "Audio", name + ".wav"));
            var pcm = wave.WithLoopJoin();
            var clip = AudioClip.Create(name, pcm.Length / wave.Channels, wave.Channels, wave.Frequency, false);
            drivingClips[i] = clip;
            if (!clip.SetData(pcm, 0)) throw new InvalidDataException($"Cannot create {name} audio clip.");
            Info($"loaded driving clip='{name}' originalFrames={wave.Samples.Length / wave.Channels} " +
                 $"loopFrames={clip.samples} length={F(clip.length)}s hz={clip.frequency} channels={clip.channels} joinMs=20.");
        }
        audioHost = new GameObject("AudiRS6R_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;
        layers = new[] { CreateLayer(native.clip), CreateLayer(drivingClips[0]!), CreateLayer(drivingClips[1]!) };
        engineSound.maxDistortion = 0f;
        configured = true;
        Info($"configured revision=5 idle='{native.clip.name}' idlePitch=1 driving=Audi2/Audi3 " +
             $"mixer='{native.outputAudioMixerGroup.audioMixer.name}' group='{native.outputAudioMixerGroup.name}'. " +
             "Ctrl+Alt+A switches driving recording; Car idle remains unchanged. Old dry modes removed.");
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
        {
            selected = 0;
            selectionBlend = 0f;
            Info("driver entered: driving clip=Audi2; Ctrl+Alt+A selects Audi3, then Audi2.");
        }
        wasControlled = controlled;
        if (controlled && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.A))
        {
            selected = 1 - selected;
            Info($"driving clip selected={(selected == 0 ? "Audi2" : "Audi3")}; idle=Car.");
            nextLog = 0f;
        }
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
            selectionBlend = Mathf.MoveTowards(selectionBlend, selected, Time.deltaTime * 5f);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var gain = envelope * Mathf.Clamp01(physics.soundManager.masterVolume);
            var driveVolume = gain * (.26f + .18f * Mathf.Clamp01(engine.ThrottlePosition));
            // Preserve authored rev patterns; use modest pitch movement in this test.
            var pitch = Mathf.Lerp(.95f, 1.3f, Mathf.InverseLerp(idle, 1f, rpm));
            layers[0].pitch = 1f;
            layers[0].volume = gain * .24f * Mathf.Sqrt(1f - driveBlend);
            layers[1].pitch = layers[2].pitch = pitch;
            layers[1].volume = driveVolume * Mathf.Sqrt(driveBlend) * Mathf.Sqrt(1f - selectionBlend);
            layers[2].volume = driveVolume * Mathf.Sqrt(driveBlend) * Mathf.Sqrt(selectionBlend);
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
                 $"driveBlend={F(driveBlend)} selected={(selected == 0 ? "Audi2" : "Audi3")} " +
                 $"idle[{Status(layers[0])}] audi2[{Status(layers[1])}] audi3[{Status(layers[2])}] " +
                 $"engineDb={ReadMixer(mixer, "engine")} fxDb={ReadMixer(mixer, "fx")} " +
                 $"masterDb={ReadMixer(mixer, "attenuation")} listenerVolume={F(AudioListener.volume)}.");
            var busMuted = running && !paused && mixer != null && mixer.GetFloat("engine", out var db) && db <= -79f;
            if (busMuted && !busMutedReported) Warn("engine mixer bus is muted while running; layers may be inaudible.");
            busMutedReported = busMuted;
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

    private void OnDisable()
    {
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        RestoreMute();
        envelope = driveBlend = selectionBlend = 0f;
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
        for (var i = 0; i < drivingClips.Length; i++)
        {
            if (drivingClips[i] != null) Destroy(drivingClips[i]);
            drivingClips[i] = null;
        }
    }

    private void OnDestroy() => Cleanup();
}
