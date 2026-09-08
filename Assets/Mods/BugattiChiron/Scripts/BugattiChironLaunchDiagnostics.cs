#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BugattiChironLaunchDiagnostics : MonoBehaviour
{
    private const int MaximumSamples = 4;
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private bool timing;
    private float requestTime;
    private float requestRpm;
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

        var throttle = Mathf.Abs(physics.input.Throttle);
        var speed = body.velocity.magnitude;
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
                $"rpm={requestRpm:F0}->{CurrentRpm():F0} gear={physics.powertrain.transmission.Gear}.");
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
                $"rpm={requestRpm:F0}->{CurrentRpm():F0} gear={physics.powertrain.transmission.Gear}.");
        }
    }

    private float CurrentRpm()
    {
        var engine = physics!.powertrain.engine;
        return engine.RPMPercent * engine.revLimiterRPM;
    }
}
