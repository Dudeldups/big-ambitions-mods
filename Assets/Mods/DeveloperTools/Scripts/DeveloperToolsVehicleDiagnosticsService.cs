#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BAModAPI;
using Helpers;
using Localizor;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;
using UnityEngine.Events;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsVehicleDiagnosticsService
    {
        private const float SampleIntervalSeconds = 0.2f;
        private const float StartSpeedKph = 2f;
        private const float StartThrottle = 0.9f;
        private const float AbortThrottle = 0.7f;
        private const float AbortThrottleSeconds = 0.75f;
        private const float TopSpeedWindowSeconds = 3f;
        private const float TopSpeedMinimumRunSeconds = 10f;
        private const float TopSpeedMaximumGainKph = 0.75f;
        private const float MaximumAccelerationRunSeconds = 120f;
        private const float Gravity = 9.80665f;
        private const float CollisionSettleSeconds = 0.25f;
        private const float RevLimiterLogIntervalSeconds = 1f;
        private const float DamageTolerance = 0.0001f;

        private static readonly float[] SpeedMilestonesKph =
            { 50f, 100f, 150f, 200f, 250f, 300f, 350f, 400f };

        private readonly ModContext context;
        private readonly List<PendingCollision> pendingCollisions = new List<PendingCollision>();
        private StreamWriter? writer;
        private VehicleController? vehicle;
        private PhysicsVehicle? physics;
        private Rigidbody? body;
        private DamageHandler? damageHandler;
        private UnityAction<Collision>? collisionListener;
        private string vehicleTypeName = string.Empty;
        private string vehicleDisplayName = string.Empty;
        private string vehicleId = string.Empty;
        private string outputPath = string.Empty;
        private float sessionStartedAt;
        private float segmentStartedAt;
        private float nextSampleAt;
        private float lastFixedAt;
        private Vector3 lastVelocity;
        private Vector3 lastPosition;
        private int previousGear;
        private bool previousRevLimiter;
        private bool accelerationArmed = true;
        private bool accelerationRunning;
        private float accelerationStartedAt;
        private float accelerationPreviousSpeed;
        private float accelerationPeakSpeed;
        private float lowThrottleSeconds;
        private float plateauWindowStartedAt;
        private float plateauWindowSpeed;
        private int nextMilestone;
        private int accelerationRunNumber;
        private int segmentNumber;
        private int collisionNumber;
        private int collisionCount;
        private int collisionIncidentCount;
        private int damagingCollisionCount;
        private int shiftCount;
        private float distanceMetres;
        private float topSpeedKph;
        private float maximumRpm;
        private float maximumAccelerationG;
        private float maximumBrakingG;
        private float maximumLateralG;
        private float airborneSeconds;
        private int cachedGroundedWheels;
        private int cachedWheelCount;
        private float cachedMaxLongitudinalSlip;
        private float cachedMaxLateralSlip;
        private float cachedAverageWheelLoad;
        private float lastRevLimiterLoggedAt;
        private bool failureReported;

        public DeveloperToolsVehicleDiagnosticsService(ModContext context) => this.context = context;

        public bool IsActive { get; private set; }

        public string StatusLabel
        {
            get
            {
                if (!IsActive)
                    return "Vehicle Diagnostics: Off";
                if (vehicle == null)
                    return "Vehicle Diagnostics: ACTIVE — waiting for a vehicle";
                return "Vehicle Diagnostics: ACTIVE — " + vehicleDisplayName +
                       " (segment " + segmentNumber + ")";
            }
        }

        public void Toggle(out string message)
        {
            if (IsActive)
                Stop("manual", out message);
            else
                Start(out message);
        }

        public void HandleEnterVehicle(VehicleController enteredVehicle)
        {
            if (IsActive && enteredVehicle != null)
                AttachVehicle(enteredVehicle);
        }

        public void HandleExitVehicle(VehicleController exitedVehicle)
        {
            if (IsActive && ReferenceEquals(vehicle, exitedVehicle))
                DetachVehicle("driver-exited");
        }

        public void HandleSceneChanged()
        {
            if (!IsActive)
                return;
            if (vehicle != null)
                DetachVehicle("scene-changed");
            TryAttachCurrentVehicle();
        }

        public void TickFixed()
        {
            if (!IsActive || vehicle == null || body == null)
                return;
            if (!vehicle.controlledByPlayer)
            {
                DetachVehicle("control-ended");
                return;
            }

            try
            {
                var now = Time.realtimeSinceStartup;
                var deltaTime = Mathf.Max(0.0001f, now - lastFixedAt);
                var sampleDue = now >= nextSampleAt;
                var snapshot = CaptureSnapshot(deltaTime, sampleDue);
                UpdateSegmentStatistics(snapshot, deltaTime);
                UpdateTransmissionEvents(snapshot);
                UpdateAccelerationRun(snapshot, deltaTime);
                FinalizeSettledCollisions(snapshot, now);
                if (sampleDue)
                {
                    WriteRow("sample", snapshot, string.Empty, false);
                    nextSampleAt = now + SampleIntervalSeconds;
                }

                lastFixedAt = now;
                lastVelocity = body.velocity;
                lastPosition = body.position;
            }
            catch (Exception exception)
            {
                if (failureReported)
                    return;
                failureReported = true;
                context.Logger.Warn(
                    "DeveloperTools: vehicle diagnostics sampling failed for " +
                    vehicleDisplayName + ": " + exception.GetBaseException().Message);
            }
        }

        public void Shutdown()
        {
            if (IsActive)
                Stop("mod-shutdown", out _);
        }

        private void Start(out string message)
        {
            try
            {
                var directory = Path.Combine(
                    Application.persistentDataPath,
                    "DeveloperTools",
                    "VehicleDiagnostics");
                Directory.CreateDirectory(directory);
                outputPath = Path.Combine(
                    directory,
                    "vehicle-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
                writer = new StreamWriter(
                    new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false),
                    65536);
                writer.WriteLine(
                    "utc,event,session_seconds,segment,vehicle_type,vehicle_name,vehicle_id," +
                    "speed_kph,top_speed_kph,longitudinal_g,lateral_g,vertical_g," +
                    "throttle,brake,steering,handbrake,gear,gear_name,rpm,rpm_percent," +
                    "engine_load,generated_power_kw,rev_limiter,saved_damage,physics_damage," +
                    "deformations,grounded_wheels,wheel_count,max_longitudinal_slip," +
                    "max_lateral_slip,average_wheel_load,fuel,distance_m,position_x,position_y,position_z,detail");
                writer.Flush();
                IsActive = true;
                sessionStartedAt = Time.realtimeSinceStartup;
                segmentNumber = 0;
                failureReported = false;
                WriteRow("session_started", default, "output=" + outputPath, true);
                TryAttachCurrentVehicle();
                message = vehicle == null
                    ? "Vehicle diagnostics started and armed. Enter a vehicle to begin sampling."
                    : "Vehicle diagnostics started for " + vehicleDisplayName + ".";
            }
            catch (Exception exception)
            {
                writer?.Dispose();
                writer = null;
                IsActive = false;
                message = "Could not start vehicle diagnostics: " + exception.GetBaseException().Message;
                context.Logger.Warn("DeveloperTools: " + message);
            }
        }

        private void Stop(string reason, out string message)
        {
            var completedPath = outputPath;
            if (vehicle != null)
                DetachVehicle(reason);
            WriteRow(
                "session_stopped",
                default,
                "reason=" + reason + ";segments=" + segmentNumber + ";output=" + completedPath,
                true);
            IsActive = false;
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            catch (Exception exception)
            {
                context.Logger.Warn(
                    "DeveloperTools: could not close the vehicle diagnostics report: " +
                    exception.GetBaseException().Message);
            }
            writer = null;
            pendingCollisions.Clear();
            message = "Vehicle diagnostics stopped. CSV report saved in the VehicleDiagnostics folder.";
        }

        private void TryAttachCurrentVehicle()
        {
            if (!PlayerHelper.IsUsingVehicle)
                return;
            var selected = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            if (selected != null && selected.controlledByPlayer)
                AttachVehicle(selected);
        }

        private void AttachVehicle(VehicleController enteredVehicle)
        {
            if (ReferenceEquals(vehicle, enteredVehicle))
                return;
            if (vehicle != null)
                DetachVehicle("vehicle-changed");

            vehicle = enteredVehicle;
            physics = enteredVehicle.GetComponent<PhysicsVehicle>() ??
                      enteredVehicle.GetComponentInChildren<PhysicsVehicle>(true);
            body = enteredVehicle.GetComponent<Rigidbody>() ??
                   enteredVehicle.GetComponentInParent<Rigidbody>();
            damageHandler = enteredVehicle.GetComponentInChildren<DamageHandler>(true);
            vehicleTypeName = enteredVehicle.vehicleInstance?.vehicleTypeName ??
                              enteredVehicle.vehicleType?.vehicleTypeName ?? enteredVehicle.name;
            vehicleDisplayName = Localize(vehicleTypeName);
            vehicleId = enteredVehicle.vehicleInstance?.id ?? enteredVehicle.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            segmentNumber++;
            segmentStartedAt = Time.realtimeSinceStartup;
            nextSampleAt = segmentStartedAt;
            lastFixedAt = segmentStartedAt;
            lastVelocity = body?.velocity ?? Vector3.zero;
            lastPosition = body?.position ?? enteredVehicle.transform.position;
            previousGear = physics?.powertrain?.transmission?.Gear ?? 0;
            previousRevLimiter = physics?.powertrain?.engine?.revLimiterActive ?? false;
            lastRevLimiterLoggedAt = float.NegativeInfinity;
            accelerationArmed = true;
            accelerationRunning = false;
            accelerationRunNumber = 0;
            collisionCount = collisionIncidentCount = damagingCollisionCount = shiftCount = 0;
            distanceMetres = topSpeedKph = maximumRpm = 0f;
            maximumAccelerationG = maximumBrakingG = maximumLateralG = airborneSeconds = 0f;
            cachedGroundedWheels = cachedWheelCount = 0;
            cachedMaxLongitudinalSlip = cachedMaxLateralSlip = cachedAverageWheelLoad = 0f;
            pendingCollisions.Clear();
            failureReported = false;

            if (physics?.onCollision != null)
            {
                collisionListener = HandleCollision;
                physics.onCollision.AddListener(collisionListener);
            }

            var snapshot = CaptureSnapshot(0.02f, true);
            WriteRow("vehicle_started", snapshot, BuildVehicleConfiguration(), true);
            WriteWheelConfiguration();
        }

        private void DetachVehicle(string reason)
        {
            if (vehicle == null)
                return;
            var snapshot = body != null ? CaptureSnapshot(0.02f, true) : default;
            if (accelerationRunning)
                FinishAcceleration(snapshot, reason);
            FinalizeAllCollisions(snapshot);
            WriteRow(
                "vehicle_summary",
                snapshot,
                "reason=" + reason +
                ";duration_s=" + Format(Time.realtimeSinceStartup - segmentStartedAt) +
                ";distance_m=" + Format(distanceMetres) +
                ";top_speed_kph=" + Format(topSpeedKph) +
                ";max_rpm=" + Format(maximumRpm) +
                ";max_accel_g=" + Format(maximumAccelerationG) +
                ";max_braking_g=" + Format(maximumBrakingG) +
                ";max_lateral_g=" + Format(maximumLateralG) +
                ";airborne_s=" + Format(airborneSeconds) +
                ";collision_contacts=" + collisionCount +
                ";collision_incidents=" + collisionIncidentCount +
                ";damage_applied_incidents=" + damagingCollisionCount +
                ";shifts=" + shiftCount,
                true);

            if (physics?.onCollision != null && collisionListener != null)
                physics.onCollision.RemoveListener(collisionListener);
            collisionListener = null;
            vehicle = null;
            physics = null;
            body = null;
            damageHandler = null;
            vehicleTypeName = vehicleDisplayName = vehicleId = string.Empty;
            pendingCollisions.Clear();
        }

        private TelemetrySnapshot CaptureSnapshot(float deltaTime, bool refreshWheelTelemetry = false)
        {
            var currentBody = body;
            var currentVehicle = vehicle;
            if (currentBody == null || currentVehicle == null)
                return default;

            var velocity = currentBody.velocity;
            var acceleration = (velocity - lastVelocity) / Mathf.Max(0.0001f, deltaTime);
            var forward = Vector3.ProjectOnPlane(currentVehicle.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f)
                forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var engine = physics?.powertrain?.engine;
            var transmission = physics?.powertrain?.transmission;
            if (refreshWheelTelemetry)
            {
                var wheels = physics?.powertrain?.wheels;
                var grounded = 0;
                var wheelCount = 0;
                var maxLongSlip = 0f;
                var maxLatSlip = 0f;
                var totalLoad = 0f;
                if (wheels != null)
                {
                    foreach (var wheel in wheels)
                    {
                        var api = wheel?.wheelUAPI;
                        if (api == null)
                            continue;
                        wheelCount++;
                        if (api.IsGrounded) grounded++;
                        maxLongSlip = Mathf.Max(maxLongSlip, Mathf.Abs(api.NormalizedLongitudinalSlip));
                        maxLatSlip = Mathf.Max(maxLatSlip, Mathf.Abs(api.NormalizedLateralSlip));
                        totalLoad += api.Load;
                    }
                }

                cachedGroundedWheels = grounded;
                cachedWheelCount = wheelCount;
                cachedMaxLongitudinalSlip = maxLongSlip;
                cachedMaxLateralSlip = maxLatSlip;
                cachedAverageWheelLoad = wheelCount > 0 ? totalLoad / wheelCount : 0f;
            }

            var rpm = engine == null ? 0f : engine.RPMPercent * engine.revLimiterRPM;
            var car = currentVehicle as CarController;
            return new TelemetrySnapshot
            {
                SpeedKph = velocity.magnitude * 3.6f,
                LongitudinalG = Vector3.Dot(acceleration, forward) / Gravity,
                LateralG = Vector3.Dot(acceleration, right) / Gravity,
                VerticalG = Vector3.Dot(acceleration, Vector3.up) / Gravity,
                Throttle = physics?.input?.Throttle ?? 0f,
                Brake = physics?.input?.Brakes ?? 0f,
                Steering = physics?.input?.Steering ?? 0f,
                Handbrake = physics?.input?.Handbrake ?? 0f,
                Gear = transmission?.Gear ?? 0,
                GearName = transmission?.GearName ?? string.Empty,
                Rpm = rpm,
                RpmPercent = engine?.RPMPercent ?? 0f,
                EngineLoad = engine?.Load ?? 0f,
                GeneratedPower = engine?.generatedPower ?? 0f,
                RevLimiter = engine?.revLimiterActive ?? false,
                SavedDamage = currentVehicle.vehicleInstance?.damage ?? 0f,
                PhysicsDamage = damageHandler?.Damage ?? 0f,
                Deformations = currentVehicle.vehicleInstance?.deformations?.Count ?? 0,
                GroundedWheels = cachedGroundedWheels,
                WheelCount = cachedWheelCount,
                MaxLongitudinalSlip = cachedMaxLongitudinalSlip,
                MaxLateralSlip = cachedMaxLateralSlip,
                AverageWheelLoad = cachedAverageWheelLoad,
                Fuel = car?.GetCurrentFuel() ?? currentVehicle.vehicleInstance?.fuel ?? 0f,
                Position = currentBody.position
            };
        }

        private void UpdateSegmentStatistics(TelemetrySnapshot snapshot, float deltaTime)
        {
            distanceMetres += Vector3.Distance(body!.position, lastPosition);
            topSpeedKph = Mathf.Max(topSpeedKph, snapshot.SpeedKph);
            maximumRpm = Mathf.Max(maximumRpm, snapshot.Rpm);
            maximumAccelerationG = Mathf.Max(maximumAccelerationG, snapshot.LongitudinalG);
            maximumBrakingG = Mathf.Max(maximumBrakingG, -snapshot.LongitudinalG);
            maximumLateralG = Mathf.Max(maximumLateralG, Mathf.Abs(snapshot.LateralG));
            if (snapshot.WheelCount > 0 && snapshot.GroundedWheels == 0)
                airborneSeconds += deltaTime;
        }

        private void UpdateTransmissionEvents(TelemetrySnapshot snapshot)
        {
            if (snapshot.Gear != previousGear)
            {
                shiftCount++;
                WriteRow(
                    "gear_changed",
                    snapshot,
                    "from=" + previousGear + ";to=" + snapshot.Gear +
                    ";acceleration_run=" + (accelerationRunning ? accelerationRunNumber : 0),
                    true);
                previousGear = snapshot.Gear;
            }
            var now = Time.realtimeSinceStartup;
            if (snapshot.RevLimiter && !previousRevLimiter &&
                now - lastRevLimiterLoggedAt >= RevLimiterLogIntervalSeconds)
            {
                WriteRow("rev_limiter", snapshot, "entered=true", true);
                lastRevLimiterLoggedAt = now;
            }
            previousRevLimiter = snapshot.RevLimiter;
        }

        private void UpdateAccelerationRun(TelemetrySnapshot snapshot, float deltaTime)
        {
            if (!accelerationRunning)
            {
                if (snapshot.SpeedKph <= StartSpeedKph && snapshot.Throttle < 0.2f)
                    accelerationArmed = true;
                if (accelerationArmed && snapshot.SpeedKph <= StartSpeedKph &&
                    snapshot.Throttle >= StartThrottle)
                    StartAcceleration(snapshot);
                return;
            }

            accelerationPeakSpeed = Mathf.Max(accelerationPeakSpeed, snapshot.SpeedKph);
            CaptureAccelerationMilestones(snapshot);
            lowThrottleSeconds = snapshot.Throttle < AbortThrottle
                ? lowThrottleSeconds + deltaTime
                : 0f;
            var elapsed = Time.realtimeSinceStartup - accelerationStartedAt;
            if (lowThrottleSeconds >= AbortThrottleSeconds)
            {
                FinishAcceleration(snapshot, "throttle-released");
                return;
            }
            if (elapsed >= MaximumAccelerationRunSeconds)
            {
                FinishAcceleration(snapshot, "timeout");
                return;
            }
            if (elapsed < TopSpeedMinimumRunSeconds ||
                Time.realtimeSinceStartup - plateauWindowStartedAt < TopSpeedWindowSeconds)
                return;

            var gain = accelerationPeakSpeed - plateauWindowSpeed;
            if (snapshot.Throttle >= StartThrottle && accelerationPeakSpeed >= 30f &&
                gain <= TopSpeedMaximumGainKph)
            {
                FinishAcceleration(snapshot, "top-speed-plateau");
                return;
            }
            plateauWindowStartedAt = Time.realtimeSinceStartup;
            plateauWindowSpeed = accelerationPeakSpeed;
        }

        private void StartAcceleration(TelemetrySnapshot snapshot)
        {
            accelerationRunning = true;
            accelerationArmed = false;
            accelerationRunNumber++;
            accelerationStartedAt = Time.realtimeSinceStartup;
            accelerationPreviousSpeed = snapshot.SpeedKph;
            accelerationPeakSpeed = snapshot.SpeedKph;
            lowThrottleSeconds = 0f;
            plateauWindowStartedAt = accelerationStartedAt;
            plateauWindowSpeed = snapshot.SpeedKph;
            nextMilestone = 0;
            WriteRow(
                "acceleration_started",
                snapshot,
                "run=" + accelerationRunNumber +
                ";configured_max_speed_kph=" + (vehicle?.vehicleType?.maxSpeed ?? 0),
                true);
        }

        private void CaptureAccelerationMilestones(TelemetrySnapshot snapshot)
        {
            while (nextMilestone < SpeedMilestonesKph.Length &&
                   snapshot.SpeedKph >= SpeedMilestonesKph[nextMilestone])
            {
                var target = SpeedMilestonesKph[nextMilestone];
                var span = snapshot.SpeedKph - accelerationPreviousSpeed;
                var fraction = span > 0.001f
                    ? Mathf.Clamp01((target - accelerationPreviousSpeed) / span)
                    : 1f;
                var elapsed = Time.realtimeSinceStartup - accelerationStartedAt;
                var milestoneTime = elapsed - Time.fixedDeltaTime + Time.fixedDeltaTime * fraction;
                WriteRow(
                    "acceleration_milestone",
                    snapshot,
                    "run=" + accelerationRunNumber +
                    ";target_kph=" + Format(target) +
                    ";elapsed_s=" + Format(milestoneTime),
                    true);
                nextMilestone++;
            }
            accelerationPreviousSpeed = snapshot.SpeedKph;
        }

        private void FinishAcceleration(TelemetrySnapshot snapshot, string reason)
        {
            if (!accelerationRunning)
                return;
            WriteRow(
                "acceleration_summary",
                snapshot,
                "run=" + accelerationRunNumber +
                ";reason=" + reason +
                ";elapsed_s=" + Format(Time.realtimeSinceStartup - accelerationStartedAt) +
                ";peak_speed_kph=" + Format(accelerationPeakSpeed) +
                ";milestones=" + nextMilestone + "/" + SpeedMilestonesKph.Length,
                true);
            accelerationRunning = false;
            lowThrottleSeconds = 0f;
        }

        private void HandleCollision(Collision collision)
        {
            if (!IsActive || vehicle == null || collision == null)
                return;
            try
            {
                collisionNumber++;
                collisionCount++;
                var contactPoint = Vector3.zero;
                var contactNormal = Vector3.zero;
                if (collision.contactCount > 0)
                {
                    var contact = collision.GetContact(0);
                    contactPoint = contact.point;
                    contactNormal = contact.normal;
                }
                var validForDamage = DamageHandler.IsCollisionValid(collision);
                var pending = new PendingCollision
                {
                    Number = collisionNumber,
                    SettleAt = Time.realtimeSinceStartup + CollisionSettleSeconds,
                    Other = collision.gameObject?.name ?? "unknown",
                    OtherLayer = collision.gameObject?.layer ?? -1,
                    RelativeSpeedKph = collision.relativeVelocity.magnitude * 3.6f,
                    Impulse = collision.impulse.magnitude,
                    ContactCount = collision.contactCount,
                    ContactPoint = contactPoint,
                    ContactNormal = contactNormal,
                    ValidForDamage = validForDamage,
                    BeforeSavedDamage = vehicle.vehicleInstance?.damage ?? 0f,
                    BeforePhysicsDamage = damageHandler?.Damage ?? 0f,
                    BeforeDeformations = vehicle.vehicleInstance?.deformations?.Count ?? 0
                };
                pendingCollisions.Add(pending);
                WriteRow(
                    "collision_contact",
                    CaptureSnapshot(0.02f, true),
                    BuildCollisionContactDetail(pending),
                    true);
            }
            catch (Exception exception)
            {
                context.Logger.Warn(
                    "DeveloperTools: collision diagnostics failed for " + vehicleDisplayName +
                    ": " + exception.GetBaseException().Message);
            }
        }

        private void FinalizeSettledCollisions(TelemetrySnapshot snapshot, float now)
        {
            while (pendingCollisions.Count > 0 && now >= pendingCollisions[0].SettleAt)
            {
                var incidentLimit = pendingCollisions[0].SettleAt + CollisionSettleSeconds;
                var incidentCount = 1;
                while (incidentCount < pendingCollisions.Count &&
                       pendingCollisions[incidentCount].SettleAt <= incidentLimit)
                {
                    incidentCount++;
                }

                var incident = pendingCollisions.GetRange(0, incidentCount);
                pendingCollisions.RemoveRange(0, incidentCount);
                FinalizeCollisionIncident(incident, snapshot);
            }
        }

        private void FinalizeAllCollisions(TelemetrySnapshot snapshot)
        {
            if (pendingCollisions.Count > 0)
                FinalizeCollisionIncident(pendingCollisions, snapshot);
            pendingCollisions.Clear();
        }

        private void FinalizeCollisionIncident(IReadOnlyList<PendingCollision> incident, TelemetrySnapshot after)
        {
            if (incident.Count == 0)
                return;

            var first = incident[0];
            var savedDelta = after.SavedDamage - first.BeforeSavedDamage;
            var physicsDelta = after.PhysicsDamage - first.BeforePhysicsDamage;
            var deformationDelta = after.Deformations - first.BeforeDeformations;
            var validContacts = incident.Count(item => item.ValidForDamage);
            var damageApplied = validContacts > 0 &&
                                (savedDelta > DamageTolerance ||
                                 physicsDelta > DamageTolerance || deformationDelta > 0);
            collisionIncidentCount++;
            if (damageApplied) damagingCollisionCount++;
            WriteRow(
                "collision_result",
                after,
                "incident=" + collisionIncidentCount +
                ";contacts=" + incident.Count +
                ";collision_ids=" + string.Join("|", incident.Select(item => item.Number)) +
                ";valid_contacts=" + validContacts +
                ";valid_for_damage=" + (validContacts > 0) +
                ";damage_applied=" + damageApplied +
                ";saved_damage_delta=" + Format(savedDelta) +
                ";physics_damage_delta=" + Format(physicsDelta) +
                ";deformation_delta=" + deformationDelta,
                true);
        }

        private string BuildCollisionContactDetail(PendingCollision pending) =>
            "collision=" + pending.Number +
            ";other=" + pending.Other +
            ";layer=" + pending.OtherLayer +
            ";relative_speed_kph=" + Format(pending.RelativeSpeedKph) +
            ";impulse=" + Format(pending.Impulse) +
            ";contacts=" + pending.ContactCount +
            ";point=" + FormatVector(pending.ContactPoint) +
            ";normal=" + FormatVector(pending.ContactNormal) +
            ";valid_for_damage=" + pending.ValidForDamage;

        private string BuildVehicleConfiguration()
        {
            var type = vehicle?.vehicleType;
            var engine = physics?.powertrain?.engine;
            var transmission = physics?.powertrain?.transmission;
            var rigidbody = body;
            var gears = transmission?.gears == null
                ? string.Empty
                : string.Join("|", transmission.gears.Select(Format));
            var colliderBounds = vehicle?.vehicleCollider == null
                ? "unavailable"
                : FormatVector(vehicle.vehicleCollider.bounds.size);
            return "object=" + (vehicle?.name ?? "unknown") +
                   ";configured_max_speed_kph=" + (type?.maxSpeed ?? 0) +
                   ";configured_engine_power=" + Format(type?.enginePower ?? 0f) +
                   ";configured_brake_force=" + Format(type?.brakeForce ?? 0f) +
                   ";turn_radius=" + (type?.turnRadius ?? 0) +
                   ";mass_kg=" + Format(rigidbody?.mass ?? 0f) +
                   ";drag=" + Format(rigidbody?.drag ?? 0f) +
                   ";angular_drag=" + Format(rigidbody?.angularDrag ?? 0f) +
                   ";center_of_mass=" + FormatVector(rigidbody?.centerOfMass ?? Vector3.zero) +
                   ";collider_size=" + colliderBounds +
                   ";wheelbase_m=" + Format(physics?.wheelbase ?? 0f) +
                   ";idle_rpm=" + Format(engine?.idleRPM ?? 0f) +
                   ";rev_limit_rpm=" + Format(engine?.revLimiterRPM ?? 0f) +
                   ";estimated_peak_power=" + Format(engine?.EstimatedPeakPower ?? 0f) +
                   ";estimated_peak_power_rpm=" + Format(engine?.EstimatedPeakPowerRPM ?? 0f) +
                   ";estimated_peak_torque=" + Format(engine?.EstimatedPeakTorque ?? 0f) +
                   ";estimated_peak_torque_rpm=" + Format(engine?.EstimatedPeakTorqueRPM ?? 0f) +
                   ";forward_gears=" + (transmission?.forwardGearCount ?? 0) +
                   ";final_drive=" + Format(transmission?.finalGearRatio ?? 0f) +
                   ";upshift_rpm=" + Format(transmission?.UpshiftRPM ?? 0f) +
                   ";downshift_rpm=" + Format(transmission?.DownshiftRPM ?? 0f) +
                   ";shift_duration_s=" + Format(transmission?.shiftDuration ?? 0f) +
                   ";gear_ratios=" + gears +
                   ";damage_handler=" + (damageHandler != null) +
                   ";damage_threshold_mps=" + Format((damageHandler?.decelerationThreshold ?? 0f) / 100f) +
                   ";mesh_deform=" + (damageHandler?.meshDeform ?? false) +
                   ";visual_only_damage=" + (damageHandler?.visualOnly ?? false);
        }

        private void WriteWheelConfiguration()
        {
            var wheels = physics?.powertrain?.wheels;
            if (wheels == null)
                return;
            for (var index = 0; index < wheels.Count; index++)
            {
                var api = wheels[index]?.wheelUAPI;
                if (api == null)
                    continue;
                WriteRow(
                    "wheel_configuration",
                    CaptureSnapshot(0.02f, true),
                    "wheel=" + index +
                    ";radius_m=" + Format(api.Radius) +
                    ";width_m=" + Format(api.Width) +
                    ";mass_kg=" + Format(api.Mass) +
                    ";spring_length_m=" + Format(api.SpringMaxLength) +
                    ";spring_force=" + Format(api.SpringMaxForce) +
                    ";bump_rate=" + Format(api.DamperBumpRate) +
                    ";rebound_rate=" + Format(api.DamperReboundRate) +
                    ";longitudinal_grip=" + Format(api.LongitudinalFrictionGrip) +
                    ";lateral_grip=" + Format(api.LateralFrictionGrip),
                    false);
            }
            writer?.Flush();
        }

        private void WriteRow(
            string eventName,
            TelemetrySnapshot snapshot,
            string detail,
            bool echoToPlayerLog)
        {
            if (writer == null)
                return;
            var sessionSeconds = IsActive
                ? Time.realtimeSinceStartup - sessionStartedAt
                : 0f;
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                Csv(eventName),
                Format(sessionSeconds),
                segmentNumber.ToString(CultureInfo.InvariantCulture),
                Csv(vehicleTypeName),
                Csv(vehicleDisplayName),
                Csv(vehicleId),
                Format(snapshot.SpeedKph),
                Format(topSpeedKph),
                Format(snapshot.LongitudinalG),
                Format(snapshot.LateralG),
                Format(snapshot.VerticalG),
                Format(snapshot.Throttle),
                Format(snapshot.Brake),
                Format(snapshot.Steering),
                Format(snapshot.Handbrake),
                snapshot.Gear.ToString(CultureInfo.InvariantCulture),
                Csv(snapshot.GearName),
                Format(snapshot.Rpm),
                Format(snapshot.RpmPercent),
                Format(snapshot.EngineLoad),
                Format(snapshot.GeneratedPower),
                snapshot.RevLimiter ? "1" : "0",
                Format(snapshot.SavedDamage),
                Format(snapshot.PhysicsDamage),
                snapshot.Deformations.ToString(CultureInfo.InvariantCulture),
                snapshot.GroundedWheels.ToString(CultureInfo.InvariantCulture),
                snapshot.WheelCount.ToString(CultureInfo.InvariantCulture),
                Format(snapshot.MaxLongitudinalSlip),
                Format(snapshot.MaxLateralSlip),
                Format(snapshot.AverageWheelLoad),
                Format(snapshot.Fuel),
                Format(distanceMetres),
                Format(snapshot.Position.x),
                Format(snapshot.Position.y),
                Format(snapshot.Position.z),
                Csv(detail)
            }));
            if (!echoToPlayerLog)
                return;
            writer.Flush();
            context.Logger.Info(
                "DeveloperTools: vehicle diagnostics " + eventName +
                (string.IsNullOrEmpty(vehicleDisplayName) ? string.Empty : " vehicle=" + vehicleDisplayName) +
                (string.IsNullOrEmpty(vehicleTypeName) ? string.Empty : " type=" + vehicleTypeName) +
                (string.IsNullOrEmpty(detail) ? string.Empty : " " + detail));
        }

        private static string Format(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string FormatVector(Vector3 value) =>
            Format(value.x) + "|" + Format(value.y) + "|" + Format(value.z);

        private static string Csv(string? value)
        {
            value ??= string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Localize(string id)
        {
            try
            {
                var localized = id.GetLocalization()?.ToString();
                return string.IsNullOrWhiteSpace(localized) ? id : localized!;
            }
            catch
            {
                return id;
            }
        }

        private struct TelemetrySnapshot
        {
            internal float SpeedKph;
            internal float LongitudinalG;
            internal float LateralG;
            internal float VerticalG;
            internal float Throttle;
            internal float Brake;
            internal float Steering;
            internal float Handbrake;
            internal int Gear;
            internal string? GearName;
            internal float Rpm;
            internal float RpmPercent;
            internal float EngineLoad;
            internal float GeneratedPower;
            internal bool RevLimiter;
            internal float SavedDamage;
            internal float PhysicsDamage;
            internal int Deformations;
            internal int GroundedWheels;
            internal int WheelCount;
            internal float MaxLongitudinalSlip;
            internal float MaxLateralSlip;
            internal float AverageWheelLoad;
            internal float Fuel;
            internal Vector3 Position;
        }

        private sealed class PendingCollision
        {
            internal int Number;
            internal float SettleAt;
            internal string Other = string.Empty;
            internal int OtherLayer;
            internal float RelativeSpeedKph;
            internal float Impulse;
            internal int ContactCount;
            internal Vector3 ContactPoint;
            internal Vector3 ContactNormal;
            internal bool ValidForDamage;
            internal float BeforeSavedDamage;
            internal float BeforePhysicsDamage;
            internal int BeforeDeformations;
        }
    }
}
