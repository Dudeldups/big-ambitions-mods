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

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>();
        context = modContext;
    }

    private void Update()
    {
        if (vehicle == null || physics == null || body == null || samples >= MaximumSamples)
            return;
        if (!vehicle.controlledByPlayer)
        {
            timing = false;
            return;
        }

        var throttle = physics.input.Throttle;
        var speed = body.velocity.magnitude;
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
}
