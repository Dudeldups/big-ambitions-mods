#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BugattiChironEngineRecovery : MonoBehaviour
{
    private const int MaximumDormantEngineRestartAttempts = 3;
    private const float DormantEngineGracePeriod = 0.35f;
    private const float DormantEngineRestartDelay = 0.10f;
    private const float DormantEngineRetryDelay = 0.90f;
    private const float MaximumDormantEngineRecoverySpeed = 1.5f;
    private const float MinimumHealthyEngineRpm = 400f;
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private float dormantEngineDetectedAt = -1f;
    private float restartDormantEngineAt = -1f;
    private float nextDormantEngineCheckAt;
    private int dormantEngineRestartAttempts;
    private bool dormantEngineRecoveryActive;
    private bool dormantEngineFailureLogged;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>();
        context = modContext;
        ResetDormantEngineRecovery();
    }

    private void Update()
    {
        if (vehicle == null || physics == null || body == null)
            return;
        if (!vehicle.controlledByPlayer)
        {
            ResetDormantEngineRecovery();
            return;
        }

        var throttle = physics.input.Throttle;
        var speed = body.velocity.magnitude;
        RecoverDormantEngine(throttle, speed);
    }

    private float CurrentRpm()
    {
        var engine = physics!.powertrain.engine;
        return engine.RPMPercent * engine.revLimiterRPM;
    }

    private void RecoverDormantEngine(float throttle, float speedMetersPerSecond)
    {
        var engine = physics!.powertrain.engine;
        var rpm = CurrentRpm();

        if (restartDormantEngineAt >= 0f)
        {
            if (Time.unscaledTime < restartDormantEngineAt)
                return;

            restartDormantEngineAt = -1f;
            engine.StartEngine();
            nextDormantEngineCheckAt = Time.unscaledTime + DormantEngineRetryDelay;
            return;
        }

        if (rpm >= MinimumHealthyEngineRpm)
        {
            if (dormantEngineRecoveryActive)
            {
                context?.Logger.Info(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: dormant engine " +
                    $"recovered after {dormantEngineRestartAttempts} restart request(s); rpm={rpm:F0}.");
            }
            ResetDormantEngineRecovery();
            return;
        }

        var throttlePressed = Mathf.Abs(throttle) >= 0.25f;
        var nearStandstill = speedMetersPerSecond <= MaximumDormantEngineRecoverySpeed;
        var recoverableNativeState =
            engine.ignition && engine.canRun &&
            (engine.IsRunning || dormantEngineRecoveryActive);
        if (!throttlePressed || !nearStandstill || !recoverableNativeState)
        {
            dormantEngineDetectedAt = -1f;
            return;
        }

        if (Time.unscaledTime < nextDormantEngineCheckAt)
            return;

        if (dormantEngineDetectedAt < 0f)
        {
            dormantEngineDetectedAt = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - dormantEngineDetectedAt < DormantEngineGracePeriod)
            return;

        if (dormantEngineRestartAttempts >= MaximumDormantEngineRestartAttempts)
        {
            if (!dormantEngineFailureLogged)
            {
                dormantEngineFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: dormant engine " +
                    $"did not recover after {dormantEngineRestartAttempts} restart requests; " +
                    $"rpm={rpm:F0} running={engine.IsRunning} ignition={engine.ignition} " +
                    $"canRun={engine.canRun}.");
            }
            return;
        }

        dormantEngineRecoveryActive = true;
        dormantEngineRestartAttempts++;
        dormantEngineDetectedAt = -1f;
        engine.StopEngine();
        restartDormantEngineAt = Time.unscaledTime + DormantEngineRestartDelay;
        context?.Logger.Warn(
            $"BugattiChiron engine vehicle={vehicle?.GetInstanceID()}: resetting dormant native " +
            $"engine at full stop, restart={dormantEngineRestartAttempts}/" +
            $"{MaximumDormantEngineRestartAttempts}, throttle={throttle:F2}, rpm={rpm:F0}.");
    }

    private void ResetDormantEngineRecovery()
    {
        dormantEngineDetectedAt = -1f;
        restartDormantEngineAt = -1f;
        nextDormantEngineCheckAt = 0f;
        dormantEngineRestartAttempts = 0;
        dormantEngineRecoveryActive = false;
        dormantEngineFailureLogged = false;
    }
}
