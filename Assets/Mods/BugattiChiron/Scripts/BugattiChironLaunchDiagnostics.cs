#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BugattiChironLaunchDiagnostics : MonoBehaviour
{
    private const int MaximumSamples = 12;
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private bool timing;
    private float requestTime;
    private float requestRpm;
    private float requestThrottle;
    private int requestGear;
    private int samples;
    private bool dormantEngineLogged;

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
            dormantEngineLogged = false;
            return;
        }

        var throttle = physics.input.Throttle;
        var speed = body.velocity.magnitude;
        LogDormantEngine(throttle);
        if (samples >= MaximumSamples)
            return;
        if (!timing)
        {
            if (Mathf.Abs(throttle) < 0.25f || speed > 0.15f)
                return;
            timing = true;
            requestTime = Time.unscaledTime;
            requestRpm = CurrentRpm();
            requestThrottle = throttle;
            requestGear = physics.powertrain.transmission.Gear;
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
                $"throttle={requestThrottle:F2}->{throttle:F2} gear={requestGear}->" +
                $"{physics.powertrain.transmission.Gear} rpm={requestRpm:F0}->{CurrentRpm():F0} " +
                $"longitudinalSpeed={LongitudinalSpeed():F2}mps running=" +
                $"{physics.powertrain.engine.IsRunning}.");
        }
        else if (Mathf.Abs(throttle) < 0.1f)
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
                $"throttle={requestThrottle:F2}->{throttle:F2} gear={requestGear}->" +
                $"{physics.powertrain.transmission.Gear} rpm={requestRpm:F0}->{CurrentRpm():F0} " +
                $"longitudinalSpeed={LongitudinalSpeed():F2}mps running=" +
                $"{physics.powertrain.engine.IsRunning} ignition={physics.powertrain.engine.ignition} " +
                $"canRun={physics.powertrain.engine.canRun}.");
        }
    }

    private float CurrentRpm()
    {
        var engine = physics!.powertrain.engine;
        return engine.RPMPercent * engine.revLimiterRPM;
    }

    private float LongitudinalSpeed() =>
        Vector3.Dot(body!.velocity, vehicle!.transform.forward);

    private void LogDormantEngine(float throttle)
    {
        var engine = physics!.powertrain.engine;
        var rpm = CurrentRpm();
        if (Mathf.Abs(throttle) >= 0.25f && (!engine.IsRunning || rpm < 200f))
        {
            if (dormantEngineLogged)
                return;
            dormantEngineLogged = true;
            context?.Logger.Warn(
                $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: throttle={throttle:F2} " +
                $"but engine is dormant rpm={rpm:F0} running={engine.IsRunning} " +
                $"ignition={engine.ignition} canRun={engine.canRun}.");
            return;
        }

        if (Mathf.Abs(throttle) < 0.1f || rpm >= 400f)
            dormantEngineLogged = false;
    }
}
