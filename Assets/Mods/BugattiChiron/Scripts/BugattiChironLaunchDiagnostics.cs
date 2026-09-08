#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BugattiChironLaunchDiagnostics : MonoBehaviour
{
    private const int MaximumSamples = 4;
    private const int MaximumEngineStartAttempts = 6;
    private const float DormantEngineGracePeriod = 0.08f;
    private const float EngineRestartDelay = 0.1f;
    private const float EngineStartRetryDelay = 0.4f;
    private const float MinimumHealthyEngineRpm = 100f;
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
    private bool engineRestartPending;
    private bool engineStartConfirmedLogged;
    private bool engineFailureLogged;
    private float dormantThrottleDetectedAt = -1f;

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
            engineRestartPending = false;
            engineStartConfirmedLogged = false;
            engineFailureLogged = false;
            dormantThrottleDetectedAt = -1f;
            return;
        }

        var throttle = Mathf.Abs(physics.input.Throttle);
        var speed = body.velocity.magnitude;
        if (!wasControlled)
        {
            wasControlled = true;
            engineStartAttempts = 0;
            engineRestartPending = false;
            engineStartConfirmedLogged = false;
            engineFailureLogged = false;
            dormantThrottleDetectedAt = -1f;
            nextEngineStartAttempt = 0f;
        }
        UpdateEngineStart(throttle);

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

    private void UpdateEngineStart(float throttle)
    {
        var engine = physics!.powertrain.engine;
        var rpm = CurrentRpm();
        if (rpm >= MinimumHealthyEngineRpm)
        {
            if (engineStartAttempts > 0 && !engineStartConfirmedLogged)
            {
                engineStartConfirmedLogged = true;
                context?.Logger.Info(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: restart confirmed " +
                    $"after {engineStartAttempts} request(s), rpm={rpm:F0}.");
            }
            engineStartAttempts = 0;
            engineRestartPending = false;
            engineFailureLogged = false;
            dormantThrottleDetectedAt = -1f;
            return;
        }

        if (engineRestartPending)
        {
            if (Time.unscaledTime < nextEngineStartAttempt)
                return;
            engineRestartPending = false;
            engineStartAttempts++;
            nextEngineStartAttempt = Time.unscaledTime + EngineStartRetryDelay;
            dormantThrottleDetectedAt = Time.unscaledTime;
            engine.StartEngine();
            context?.Logger.Info(
                $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: requested restart " +
                $"attempt={engineStartAttempts}/{MaximumEngineStartAttempts} " +
                $"running={engine.IsRunning} ignition={engine.ignition} rpm={rpm:F0}.");
            return;
        }

        if (throttle < 0.05f)
        {
            dormantThrottleDetectedAt = -1f;
            return;
        }
        if (dormantThrottleDetectedAt < 0f)
        {
            dormantThrottleDetectedAt = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime < nextEngineStartAttempt ||
            Time.unscaledTime - dormantThrottleDetectedAt < DormantEngineGracePeriod)
            return;
        if (engineStartAttempts >= MaximumEngineStartAttempts)
        {
            if (!engineFailureLogged)
            {
                engineFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: remained dormant after " +
                    $"{engineStartAttempts} restart requests while throttle={throttle:F2}; " +
                    $"running={engine.IsRunning} ignition={engine.ignition} canRun={engine.canRun} " +
                    $"rpm={rpm:F0}.");
            }
            return;
        }
        if (!engine.canRun)
        {
            if (!engineFailureLogged)
            {
                engineFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: dormant but cannot restart " +
                    $"while throttle={throttle:F2}; ignition={engine.ignition} canRun={engine.canRun}.");
            }
            return;
        }

        engine.StopEngine();
        engineRestartPending = true;
        nextEngineStartAttempt = Time.unscaledTime + EngineRestartDelay;
        context?.Logger.Info(
            $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: reset dormant engine before " +
            $"restart attempt={engineStartAttempts + 1}; throttle={throttle:F2} " +
            $"running={engine.IsRunning} rpm={rpm:F0}.");
    }

    private float CurrentRpm()
    {
        var engine = physics!.powertrain.engine;
        return engine.RPMPercent * engine.revLimiterRPM;
    }
}
