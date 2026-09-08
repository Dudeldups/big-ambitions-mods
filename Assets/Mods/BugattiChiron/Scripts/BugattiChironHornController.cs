#nullable enable
using System;
using System.IO;
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class BugattiChironHornController : MonoBehaviour
{
    private const int MaximumAttempts = 20;
    private const float HornVolume = 1f;
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private GameObject? hornHost;
    private AudioSource? hornSource;
    private AudioClip? hornClip;
    private int attempts;
    private float nextAttempt;
    private bool configured;
    private bool failed;

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
                if (attempts >= MaximumAttempts || Time.unscaledTime < nextAttempt)
                    return;
                attempts++;
                nextAttempt = Time.unscaledTime + 0.5f;
                if (!TryConfigure())
                {
                    if (attempts == MaximumAttempts)
                        Warn("native audio routing was unavailable after 20 attempts.");
                    return;
                }
            }

            UpdateHorn();
        }
        catch (Exception exception)
        {
            failed = true;
            Cleanup();
            Warn($"horn failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        var engineSource = physics?.soundManager.engineRunningComponent?.source;
        var template = physics?.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (template?.outputAudioMixerGroup == null)
            template = engineSource;
        if (physics == null || template?.outputAudioMixerGroup == null || context == null)
            return false;

        hornClip = BugattiChironWave.Load(Path.Combine(context.ModRootPath, "Config", "Audio", "Horn.wav"));
        hornHost = new GameObject("BugattiChiron_Horn");
        hornHost.transform.SetParent(vehicle.transform, false);
        hornHost.transform.position = template.transform.position;
        hornSource = hornHost.AddComponent<AudioSource>();
        hornSource.playOnAwake = false;
        hornSource.loop = true;
        hornSource.clip = hornClip;
        hornSource.volume = 0f;
        hornSource.outputAudioMixerGroup = template.outputAudioMixerGroup;
        hornSource.spatialBlend = template.spatialBlend;
        hornSource.minDistance = template.minDistance;
        hornSource.maxDistance = template.maxDistance;
        hornSource.rolloffMode = template.rolloffMode;
        hornSource.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        hornSource.dopplerLevel = 0f;
        hornSource.priority = template.priority;
        configured = true;
        context.Logger.Info(
            $"BugattiChiron horn vehicle={vehicle.GetInstanceID()}: configured self-contained Audi horn sample " +
            "with additional sample gain; source volume is already at the Unity maximum.");
        return true;
    }

    private void UpdateHorn()
    {
        if (physics == null || hornSource == null)
            throw new InvalidOperationException("Configured horn source or vehicle was removed.");
        var pressed = vehicle!.controlledByPlayer && Time.timeScale > 0f && !AudioListener.pause && physics.input.Horn;
        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        var target = pressed ? master * HornVolume : 0f;
        hornSource.volume = Mathf.MoveTowards(hornSource.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !hornSource.isPlaying)
            hornSource.Play();
        else if (!pressed && hornSource.volume <= 0f && hornSource.isPlaying)
            hornSource.Stop();
    }

    private void Warn(string message) =>
        context?.Logger.Warn($"BugattiChiron horn vehicle={vehicle?.GetInstanceID()}: {message}");

    private void Cleanup()
    {
        if (hornSource != null)
            hornSource.Stop();
        if (hornHost != null)
            Destroy(hornHost);
        if (hornClip != null)
            Destroy(hornClip);
        hornSource = null;
        hornHost = null;
        hornClip = null;
        configured = false;
    }

    private void OnDisable() => Cleanup();

    private void OnDestroy() => Cleanup();
}
