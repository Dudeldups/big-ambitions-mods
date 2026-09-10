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
    private readonly List<AudioClip> ownedClips = new List<AudioClip>(2);
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private GameObject? audioHost;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private float originalBaseVolume;
    private float originalMaxDistortion;
    private float originalPitchOffset;
    private float originalPitchRange;
    private float originalVolumeRange;
    private bool configured;
    private bool failed;
    private bool paused;
    private int attempts;
    private float nextAttempt;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    private void LateUpdate()
    {
        if (vehicle == null || failed)
            return;

        try
        {
            if (!configured)
            {
                if (attempts >= 20 || Time.unscaledTime < nextAttempt)
                    return;

                attempts++;
                nextAttempt = Time.unscaledTime + .5f;
                if (!TryConfigure())
                {
                    if (attempts == 20)
                        Warn("native vehicle audio state unavailable after 20 attempts.");
                    return;
                }
            }

            UpdatePlayback();
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"audio tuning failed; donor audio restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        var native = engineSound?.source;
        if (physics == null || engineSound == null || native == null || native.clip == null ||
            native.outputAudioMixerGroup == null || context == null)
            return false;

        originalBaseVolume = engineSound.baseVolume;
        originalMaxDistortion = engineSound.maxDistortion;
        originalPitchOffset = engineSound.pitchOffset;
        originalPitchRange = engineSound.pitchRange;
        originalVolumeRange = engineSound.volumeRange;
        ApplyEngineTuning();

        audioHost = new GameObject("CadillacEscalade_Horn");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;
        var hornTemplate = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (hornTemplate == null || hornTemplate.outputAudioMixerGroup == null)
            hornTemplate = native;
        hornSource = CreateSource(audioHost, LoadClip("HornLow"), hornTemplate);
        hornSupportSource = CreateSource(audioHost, LoadClip("HornHigh"), hornTemplate);

        configured = true;
        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade audio configured vehicle={vehicle.GetInstanceID()}, nativeEngine=true, " +
            $"baseVolume={engineSound.baseVolume:0.00}, distortion={engineSound.maxDistortion:0.00}, " +
            $"pitch={engineSound.pitchOffset:0.00}+{engineSound.pitchRange:0.00}, " +
            $"volumeRange={engineSound.volumeRange:0.00}, hornVoices=low/high@" +
            $"{CadillacEscaladeAudioModel.HornLowVolume:0.00}/" +
            $"{CadillacEscaladeAudioModel.HornHighVolume:0.00}.");
        return true;
    }

    private void ApplyEngineTuning()
    {
        if (engineSound == null)
            return;
        engineSound.baseVolume = CadillacEscaladeAudioModel.NativeBaseVolume;
        engineSound.maxDistortion = CadillacEscaladeAudioModel.NativeMaxDistortion;
        engineSound.pitchOffset = CadillacEscaladeAudioModel.NativePitchOffset;
        engineSound.pitchRange = CadillacEscaladeAudioModel.NativePitchRange;
        engineSound.volumeRange = CadillacEscaladeAudioModel.NativeVolumeRange;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = CadillacEscaladeWave.Load(
            Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private static AudioSource CreateSource(GameObject host, AudioClip clip, AudioSource template)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
        source.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.rolloffMode = template.rolloffMode;
        source.priority = template.priority;
        source.dopplerLevel = 0f;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || audioHost == null || hornSource == null || hornSupportSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");

        var native = engineSound?.source;
        if (native != null)
            audioHost.transform.position = native.transform.position;

        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            paused = shouldPause;
            if (paused)
                StopHorn();
        }

        var active = vehicle!.controlledByPlayer && !paused;
        UpdateHorn(active && physics.input.Horn, Mathf.Clamp01(physics.soundManager.masterVolume));
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null)
            return;

        UpdateHornVoice(
            hornSource,
            pressed,
            pressed ? master * CadillacEscaladeAudioModel.HornLowVolume : 0f);
        UpdateHornVoice(
            hornSupportSource,
            pressed,
            pressed ? master * CadillacEscaladeAudioModel.HornHighVolume : 0f);
    }

    private static void UpdateHornVoice(AudioSource source, bool pressed, float target)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !source.isPlaying)
            source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void StopHorn()
    {
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
    }

    private void RestoreEngineTuning()
    {
        if (!configured || engineSound == null)
            return;
        engineSound.baseVolume = originalBaseVolume;
        engineSound.maxDistortion = originalMaxDistortion;
        engineSound.pitchOffset = originalPitchOffset;
        engineSound.pitchRange = originalPitchRange;
        engineSound.volumeRange = originalVolumeRange;
    }

    private void Warn(string message) =>
        context?.Logger.Warn($"CadillacEscalade audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDisable()
    {
        StopHorn();
        RestoreEngineTuning();
        paused = false;
    }

    private void OnEnable()
    {
        if (configured)
            ApplyEngineTuning();
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null)
            Destroy(audioHost);
        audioHost = null;
        hornSource = null;
        hornSupportSource = null;
        foreach (var clip in ownedClips)
            if (clip != null)
                Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
