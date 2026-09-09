#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BugattiChironLaunchDiagnostics : MonoBehaviour
{
    private const int MaximumBenchmarkRuns = 3;
    private const int MaximumDormantEngineRestartAttempts = 3;
    private const float DormantEngineGracePeriod = 0.35f;
    private const float DormantEngineRestartDelay = 0.10f;
    private const float DormantEngineRetryDelay = 0.90f;
    private const float MaximumDormantEngineRecoverySpeed = 1.5f;
    private const float MinimumHealthyEngineRpm = 400f;
    private static readonly float[] BenchmarkSpeedsKph = { 100f, 200f, 300f, 400f, 420f };
    private static readonly float[] PublishedTimes = { 2.4f, 6.1f, 13.1f, 32.6f, 0f };
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
    private bool benchmarkRunning;
    private float benchmarkStartedAt;
    private float benchmarkPeakSpeedKph;
    private float benchmarkStoppedAt;
    private int benchmarkIndex;
    private int benchmarkRuns;

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
        UpdateAccelerationBenchmark(throttle, speed);
    }

    private float CurrentRpm()
    {
        var engine = physics!.powertrain.engine;
        return engine.RPMPercent * engine.revLimiterRPM;
    }

    private float LongitudinalSpeed() =>
        Vector3.Dot(body!.velocity, vehicle!.transform.forward);

    private void UpdateAccelerationBenchmark(float throttle, float speedMetersPerSecond)
    {
        if (benchmarkRuns >= MaximumBenchmarkRuns)
            return;

        var speedKph = speedMetersPerSecond * 3.6f;
        if (!benchmarkRunning)
        {
            if (throttle < 0.9f || speedKph > 5f || LongitudinalSpeed() < -0.5f)
                return;
            benchmarkRunning = true;
            benchmarkStartedAt = Time.unscaledTime;
            benchmarkIndex = 0;
            benchmarkPeakSpeedKph = speedKph;
            benchmarkStoppedAt = 0f;
            context?.Logger.Info(
                $"BugattiChiron acceleration vehicle={vehicle?.GetInstanceID()}: " +
                $"benchmark run={benchmarkRuns + 1}/{MaximumBenchmarkRuns} started.");
            return;
        }

        benchmarkPeakSpeedKph = Mathf.Max(benchmarkPeakSpeedKph, speedKph);
        if (benchmarkPeakSpeedKph > 20f && speedKph < 5f)
        {
            if (benchmarkStoppedAt <= 0f)
                benchmarkStoppedAt = Time.unscaledTime;
            if (Time.unscaledTime - benchmarkStoppedAt >= 1.5f)
            {
                CancelBenchmark(speedKph, "vehicle stopped");
                return;
            }
        }
        else
            benchmarkStoppedAt = 0f;

        if (Time.unscaledTime - benchmarkStartedAt >= 120f)
        {
            CancelBenchmark(speedKph, "120s timeout");
            return;
        }

        while (benchmarkIndex < BenchmarkSpeedsKph.Length &&
               speedKph >= BenchmarkSpeedsKph[benchmarkIndex])
        {
            var elapsed = Time.unscaledTime - benchmarkStartedAt;
            var published = PublishedTimes[benchmarkIndex];
            context?.Logger.Info(
                $"BugattiChiron acceleration vehicle={vehicle?.GetInstanceID()}: " +
                $"run={benchmarkRuns + 1}/{MaximumBenchmarkRuns} " +
                $"0-{BenchmarkSpeedsKph[benchmarkIndex]:0}kph={elapsed:0.00}s " +
                (published > 0f ? $"published={published:0.0}s " : string.Empty) +
                $"gear={physics?.powertrain.transmission.Gear} rpm={CurrentRpm():0}.");
            benchmarkIndex++;
        }

        if (benchmarkIndex >= BenchmarkSpeedsKph.Length)
        {
            benchmarkRunning = false;
            benchmarkRuns++;
        }
    }

    private void CancelBenchmark(float speedKph, string reason)
    {
        context?.Logger.Info(
            $"BugattiChiron acceleration vehicle={vehicle?.GetInstanceID()}: " +
            $"benchmark run={benchmarkRuns + 1}/{MaximumBenchmarkRuns} cancelled " +
            $"reason='{reason}' current={speedKph:0.0}kph peak={benchmarkPeakSpeedKph:0.0}kph " +
            $"after {Time.unscaledTime - benchmarkStartedAt:0.00}s.");
        benchmarkRunning = false;
        benchmarkRuns++;
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
