using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using PhysicsController = NWH.VehiclePhysics2.VehicleController;

public sealed class AudiRS6REngineAudio : MonoBehaviour
{
    public const string BundleKey = "AssetBundles/audirs6r-engine.unity3d";
    public static readonly string[] ClipPaths = {
        "Assets/Mods/AudiRS6R/Audio/Passage01/EngineBody.wav" };
    internal static AudioClip[] Clips;
    private PhysicsController physics;
    private VehicleController vehicle;
    private ModContext context;
    private AudioSource source;
    private readonly Dictionary<AudioSource, bool> mutedSources = new Dictionary<AudioSource, bool>();
    private float rpm, throttle, envelope;
    private float originalDistortion;
    private bool distortionCaptured;
    private bool paused;

    public void Initialize(VehicleController owner, ModContext modContext)
    {
        vehicle = owner;
        context = modContext;
        physics = owner.GetComponent<PhysicsController>();
        if (physics == null) context?.Logger.Warn("AudiRS6R: passage_01 audio cannot find vehicle physics.");
    }

    private void LateUpdate()
    {
        if (physics == null || vehicle == null || Clips == null || Clips.Length != 1 || Clips[0] == null) return;
        var sound = physics.soundManager;
        if (sound.engineMixerGroup == null) return;
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.clip = Clips[0];
            source.volume = 0;
            source.outputAudioMixerGroup = sound.engineMixerGroup;
            source.spatialBlend = sound.spatialBlend;
            source.dopplerLevel = 0;
            source.minDistance = 2;
            source.maxDistance = 45;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.priority = 10;
            context?.Logger.Info($"AudiRS6R: passage_01 body-v2 audio ready, vehicle='{vehicle.vehicleInstance?.id}', voices=1, pitch=0.65..1.85.");
        }
        if (!distortionCaptured)
        {
            originalDistortion = sound.engineRunningComponent.maxDistortion;
            distortionCaptured = true;
        }
        // The muted native engine still drives mixer distortion. Disable its
        // added distortion for this vehicle; the recording already has exhaust texture.
        sound.engineRunningComponent.maxDistortion = 0;
        Mute(sound.engineRunningComponent.source);
        Mute(sound.engineFanComponent.source);
        Mute(sound.transmissionWhineComponent.source);
        if (Time.timeScale <= 0 || AudioListener.pause)
        {
            if (!paused) source.Pause();
            paused = true;
            return;
        }
        if (paused) { source.UnPause(); paused = false; }
        var engine = physics.powertrain.engine;
        bool running = physics.isActiveAndEnabled && vehicle.controlledByPlayer && engine.ignition && engine.IsRunning && engine.canRun;
        float smoothing = 1f - Mathf.Exp(-Time.deltaTime / .12f);
        rpm = Mathf.Lerp(rpm, Mathf.Clamp01(engine.RPMPercent), smoothing);
        throttle = Mathf.Lerp(throttle, Mathf.Clamp01(engine.ThrottlePosition), smoothing);
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
        source.pitch = AudiRS6REngineTone.Pitch(rpm);
        source.volume = envelope * (.25f + .35f * throttle) * sound.masterVolume;
        if (envelope > 0) { if (!source.isPlaying) source.Play(); }
        else source.Stop();
    }

    private void Mute(AudioSource source)
    {
        if (source == null) return;
        if (!mutedSources.ContainsKey(source)) mutedSources.Add(source, source.mute);
        source.mute = true;
    }

    public void StopAndRestore()
    {
        if (source != null) source.Stop();
        foreach (var entry in mutedSources) if (entry.Key != null) entry.Key.mute = entry.Value;
        mutedSources.Clear();
        if (distortionCaptured && physics != null)
            physics.soundManager.engineRunningComponent.maxDistortion = originalDistortion;
        distortionCaptured = false;
        rpm = throttle = envelope = 0;
        paused = false;
    }
    private void OnDisable() => StopAndRestore();
    private void OnDestroy()
    {
        StopAndRestore();
        if (source != null) Destroy(source);
    }
}
