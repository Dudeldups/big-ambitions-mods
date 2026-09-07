using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using PhysicsController = NWH.VehiclePhysics2.VehicleController;

public sealed class AudiRS6REngineAudio : MonoBehaviour
{
    public const string BundleKey = "AssetBundles/audirs6r-engine.unity3d";
    public static readonly string[] ClipPaths = {
        "Assets/Mods/AudiRS6R/Audio/Passage01/EngineLow.wav",
        "Assets/Mods/AudiRS6R/Audio/Passage01/EngineMid.wav",
        "Assets/Mods/AudiRS6R/Audio/Passage01/EngineHigh.wav" };
    internal static AudioClip[] Clips;
    private PhysicsController physics;
    private VehicleController vehicle;
    private ModContext context;
    private AudioSource[] sources;
    private readonly Dictionary<AudioSource, bool> mutedSources = new Dictionary<AudioSource, bool>();
    private float rpm, envelope;
    private bool paused;

    public void Initialize(VehicleController owner, ModContext modContext)
    {
        vehicle = owner;
        context = modContext;
        physics = owner.GetComponent<PhysicsController>();
        if (physics == null) context?.Logger.Warn("AudiRS6R: passage_01 audio cannot find vehicle physics.");
    }

    public static Vector3 Weights(float rpm)
    {
        float position = Mathf.Clamp01(rpm) * 2f;
        return position <= 1f
            ? new Vector3(Mathf.Sqrt(1f-position), Mathf.Sqrt(position), 0f)
            : new Vector3(0f, Mathf.Sqrt(2f-position), Mathf.Sqrt(position-1f));
    }

    // Artistic calibration: these excerpts have no measured crankshaft RPM.
    // Keep each layer near its recorded pitch at its crossfade anchor.
    public static float Pitch(float rpm, int layer)
        => Mathf.Pow(2f, (Mathf.Clamp01(rpm) - layer * .5f) * .8f);

    private void LateUpdate()
    {
        if (physics == null || vehicle == null || Clips == null || Clips.Length != 3) return;
        var sound = physics.soundManager;
        if (sound.engineMixerGroup == null) return;
        if (sources == null)
        {
            foreach (var clip in Clips) if (clip == null) return;
            sources = new AudioSource[3];
            for (int i = 0; i < 3; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                sources[i] = source;
                source.playOnAwake = false;
                source.loop = true;
                source.clip = Clips[i];
                source.volume = 0;
                source.outputAudioMixerGroup = sound.engineMixerGroup;
                source.spatialBlend = sound.spatialBlend;
                source.dopplerLevel = 0;
                source.minDistance = 2;
                source.maxDistance = 45;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.priority = 10;
            }
            context?.Logger.Info($"AudiRS6R: passage_01 engine audio ready, vehicle='{vehicle.vehicleInstance?.id}', clips=3.");
        }
        Mute(sound.engineRunningComponent.source);
        Mute(sound.engineFanComponent.source);
        if (Time.timeScale <= 0 || AudioListener.pause)
        {
            if (!paused) foreach (var source in sources) source.Pause();
            paused = true;
            return;
        }
        if (paused) { foreach (var source in sources) source.UnPause(); paused = false; }
        var engine = physics.powertrain.engine;
        bool running = physics.isActiveAndEnabled && vehicle.controlledByPlayer && engine.ignition && engine.IsRunning && engine.canRun;
        rpm = Mathf.Lerp(rpm, engine.RPMPercent, 1f - Mathf.Exp(-Time.deltaTime / .10f));
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
        var weights = Weights(rpm);
        float volume = envelope * (.25f + .35f * Mathf.Clamp01(engine.ThrottlePosition)) * sound.masterVolume;
        for (int i = 0; i < 3; i++)
        {
            sources[i].pitch = Pitch(rpm, i);
            sources[i].volume = weights[i] * volume;
            if (envelope > 0) { if (!sources[i].isPlaying) sources[i].Play(); }
            else sources[i].Stop();
        }
    }

    private void Mute(AudioSource source)
    {
        if (source == null) return;
        if (!mutedSources.ContainsKey(source)) mutedSources.Add(source, source.mute);
        source.mute = true;
    }

    public void StopAndRestore()
    {
        if (sources != null) foreach (var source in sources) if (source != null) source.Stop();
        foreach (var entry in mutedSources) if (entry.Key != null) entry.Key.mute = entry.Value;
        mutedSources.Clear();
        rpm = envelope = 0;
        paused = false;
    }
    private void OnDisable() => StopAndRestore();
    private void OnDestroy()
    {
        StopAndRestore();
        if (sources != null) foreach (var source in sources) if (source != null) Destroy(source);
    }
}
