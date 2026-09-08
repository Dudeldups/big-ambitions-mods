#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BugattiChironLaunchDiagnostics : MonoBehaviour
{
    private const int MaximumSamples = 4;
    private const int MaximumEngineStartAttempts = 6;
    private const float EngineStartRetryDelay = 0.4f;
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private bool timing;
    private float requestTime;
    private float requestRpm;
    private int samples;
    private int engineStartAttempts;
    private float nextEngineStartAttempt;
    private bool wasControlled;
    private bool engineFailureLogged;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>();
        context = modContext;
    }

    private void Update()
    {
        if (vehicle == null || physics == null || body == null)
            return;
        if (!vehicle.controlledByPlayer)
        {
            timing = false;
            wasControlled = false;
            engineStartAttempts = 0;
            engineFailureLogged = false;
            return;
        }

        var throttle = Mathf.Abs(physics.input.Throttle);
        var speed = body.velocity.magnitude;
        if (!wasControlled)
        {
            wasControlled = true;
            TryWakeEngine("vehicle-entry");
        }
        else if (!physics.powertrain.engine.IsRunning && throttle >= 0.05f && speed <= 0.5f)
        {
            TryWakeEngine("stationary-throttle");
        }

        if (samples >= MaximumSamples)
            return;
        if (!timing)
        {
            if (throttle < 0.25f || speed > 0.15f)
                return;
            timing = true;
            requestTime = Time.unscaledTime;
            requestRpm = CurrentRpm();
            return;
        }

        var elapsed = Time.unscaledTime - requestTime;
        if (speed >= 0.5f)
        {
            samples++;
            timing = false;
            context?.Logger.Info(
                $"BugattiChiron launch vehicle={vehicle.GetInstanceID()}: " +
                $"sample={samples}/{MaximumSamples} requestTo0.5mps={elapsed:F3}s " +
                $"rpm={requestRpm:F0}->{CurrentRpm():F0} gear={physics.powertrain.transmission.Gear} " +
                $"running={physics.powertrain.engine.IsRunning}.");
        }
        else if (throttle < 0.1f)
        {
            timing = false;
        }
        else if (elapsed >= 2f)
        {
            samples++;
            timing = false;
            context?.Logger.Warn(
                $"BugattiChiron launch vehicle={vehicle.GetInstanceID()}: " +
                $"sample={samples}/{MaximumSamples} no movement after {elapsed:F1}s " +
                $"rpm={requestRpm:F0}->{CurrentRpm():F0} gear={physics.powertrain.transmission.Gear} " +
                $"running={physics.powertrain.engine.IsRunning} ignition={physics.powertrain.engine.ignition} " +
                $"canRun={physics.powertrain.engine.canRun}.");
        }
    }

    private void TryWakeEngine(string reason)
    {
        var engine = physics!.powertrain.engine;
        if (engine.IsRunning)
        {
            engineStartAttempts = 0;
            engineFailureLogged = false;
            return;
        }
        if (Time.unscaledTime < nextEngineStartAttempt)
            return;
        if (!engine.canRun)
        {
            if (!engineFailureLogged)
            {
                engineFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: cannot start " +
                    $"reason='{reason}' ignition={engine.ignition} canRun={engine.canRun}.");
            }
            return;
        }
        if (engineStartAttempts >= MaximumEngineStartAttempts)
        {
            if (!engineFailureLogged)
            {
                engineFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: remained dormant after " +
                    $"{engineStartAttempts} start requests reason='{reason}'.");
            }
            return;
        }

        engineStartAttempts++;
        nextEngineStartAttempt = Time.unscaledTime + EngineStartRetryDelay;
        engine.StartEngine();
        context?.Logger.Info(
            $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: requested start " +
            $"attempt={engineStartAttempts}/{MaximumEngineStartAttempts} reason='{reason}' " +
            $"running={engine.IsRunning} ignition={engine.ignition}.");
    }

    private float CurrentRpm()
    {
        var engine = physics!.powertrain.engine;
        return engine.RPMPercent * engine.revLimiterRPM;
    }
}
