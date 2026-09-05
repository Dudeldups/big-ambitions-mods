#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

internal sealed class AudiRS6RVehicleDiagnostics : MonoBehaviour
{
    private const float CollisionCorrelationWindow = 3f;
    private const float DamageTolerance = 0.0001f;
    private const float MinimumDrivingSpeedKph = 2f;
    private const float RoadDamageSuppressionWindow = 0.5f;
    private const float RoadSurfaceNormalThreshold = 0.65f;
    private const float TelemetryInterval = 5f;

    private VehicleController? vehicleController;
    private Rigidbody? body;
    private VehicleDeformationController? deformationController;
    private FieldInfo? deformationQueueField;
    private readonly List<VehicleDeformationController.VehicleDeformation> approvedDeformations = new();
    private bool initialized;
    private bool wasControlledByPlayer;
    private float lastDamage;
    private int lastDeformationCount;
    private float nextTelemetryAt;
    private CollisionSnapshot lastCollision;
    private bool hasCollisionSnapshot;
    private float approvedDamage;
    private float roadDamageSuppressionUntil;
    private int roadSurfaceContactFrame = -1;
    private int obstacleContactFrame = -1;

    public void Initialize(VehicleController controller)
    {
        if (initialized && vehicleController == controller)
            return;

        vehicleController = controller;
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        deformationController = controller.GetComponentInChildren<VehicleDeformationController>(true);
        deformationQueueField = typeof(VehicleDeformationController).GetField(
            "_deformationQueue",
            BindingFlags.Instance | BindingFlags.NonPublic);
        initialized = true;
        wasControlledByPlayer = controller.controlledByPlayer;
        lastDamage = controller.vehicleInstance?.damage ?? 0f;
        lastDeformationCount = controller.vehicleInstance?.deformations?.Count ?? 0;
        approvedDamage = lastDamage;
        SnapshotApprovedDeformations();
        nextTelemetryAt = Time.unscaledTime;

        AudiRS6RDiagnostics.Vehicle("ATTACH", BuildAttachmentMessage());
    }

    private void Update()
    {
        if (!initialized || vehicleController == null || vehicleController.vehicleInstance == null)
            return;

        try
        {
            var controlledByPlayer = vehicleController.controlledByPlayer;
            if (controlledByPlayer != wasControlledByPlayer)
            {
                AudiRS6RDiagnostics.Vehicle(
                    controlledByPlayer ? "DRIVE_START" : "DRIVE_STOP",
                    BuildTelemetryMessage(includeCollisionContext: true));
                wasControlledByPlayer = controlledByPlayer;
                nextTelemetryAt = Time.unscaledTime;
            }

            SuppressRoadSurfaceDamage();
            DetectConditionChange();

            if (controlledByPlayer && GetSpeedKph() >= MinimumDrivingSpeedKph && Time.unscaledTime >= nextTelemetryAt)
            {
                AudiRS6RDiagnostics.Vehicle("TELEMETRY", BuildTelemetryMessage(includeCollisionContext: true));
                nextTelemetryAt = Time.unscaledTime + TelemetryInterval;
            }
        }
        catch (Exception ex)
        {
            AudiRS6RDiagnostics.Error(nameof(Update), ex);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null)
            return;

        try
        {
            var otherCollider = collision.collider;
            var otherName = otherCollider != null ? otherCollider.name : "<none>";
            var otherPath = otherCollider != null ? GetHierarchyPath(otherCollider.transform) : "<none>";
            var selfPath = "<not reported>";
            var tag = otherCollider != null ? otherCollider.tag : "<none>";
            var layer = otherCollider != null ? otherCollider.gameObject.layer : -1;
            var layerName = layer >= 0 ? LayerMask.LayerToName(layer) : string.Empty;
            var relativeSpeedKph = collision.relativeVelocity.magnitude * 3.6f;
            var point = Vector3.zero;
            var normal = Vector3.zero;

            if (collision.contactCount > 0)
            {
                var contact = collision.GetContact(0);
                point = contact.point;
                normal = contact.normal;
                selfPath = contact.thisCollider != null
                    ? GetHierarchyPath(contact.thisCollider.transform)
                    : "<none>";
            }

            var isRoadLike = IsRoadLike(otherName, otherPath, tag, layerName);
            var isRoadSurfaceContact = isRoadLike &&
                                       normal.y >= RoadSurfaceNormalThreshold &&
                                       Vector3.Dot(transform.up, Vector3.up) >= 0.7f;

            if (isRoadSurfaceContact)
            {
                roadSurfaceContactFrame = Time.frameCount;
                roadDamageSuppressionUntil = Time.unscaledTime + RoadDamageSuppressionWindow;
            }
            else
            {
                obstacleContactFrame = Time.frameCount;
                roadDamageSuppressionUntil = 0f;
            }

            lastCollision = new CollisionSnapshot(
                Time.unscaledTime,
                isRoadLike,
                isRoadSurfaceContact,
                otherName,
                otherPath,
                selfPath,
                tag,
                layer,
                layerName,
                relativeSpeedKph,
                collision.impulse.magnitude,
                point,
                normal);
            hasCollisionSnapshot = true;

            AudiRS6RDiagnostics.Vehicle(
                isRoadLike ? "ROAD_CONTACT" : "COLLISION",
                $"{VehicleIdentity()} {lastCollision.Describe()} state=[{BuildMotionState()}]");
        }
        catch (Exception ex)
        {
            AudiRS6RDiagnostics.Error(nameof(OnCollisionEnter), ex);
        }
    }

    private void OnDestroy()
    {
        if (!initialized)
            return;

        try
        {
            AudiRS6RDiagnostics.Vehicle("DETACH", $"{VehicleIdentity()} state=[{BuildMotionState()}]");
        }
        catch (Exception ex)
        {
            AudiRS6RDiagnostics.Error(nameof(OnDestroy), ex);
        }
    }

    private void DetectConditionChange()
    {
        if (vehicleController?.vehicleInstance == null)
            return;

        var currentDamage = vehicleController.vehicleInstance.damage;
        var currentDeformationCount = vehicleController.vehicleInstance.deformations?.Count ?? 0;
        var damageDelta = currentDamage - lastDamage;
        var deformationDelta = currentDeformationCount - lastDeformationCount;

        if (Mathf.Abs(damageDelta) <= DamageTolerance && deformationDelta == 0)
            return;

        var collisionAge = hasCollisionSnapshot ? Time.unscaledTime - lastCollision.Time : float.PositiveInfinity;
        var hasRecentCollision = hasCollisionSnapshot && collisionAge >= 0f && collisionAge <= CollisionCorrelationWindow;
        var damageIncreased = damageDelta > DamageTolerance;

        if (damageIncreased)
        {
            var classification = ClassifyDamage(hasRecentCollision);
            var collisionContext = hasRecentCollision
                ? $"age={collisionAge:0.000}s {lastCollision.Describe()}"
                : "none within correlation window";

            AudiRS6RDiagnostics.Damage(
                $"classification={classification} {VehicleIdentity()} " +
                $"damage={lastDamage:0.000000}->{currentDamage:0.000000} delta={damageDelta:+0.000000;-0.000000;0.000000} " +
                $"deformations={lastDeformationCount}->{currentDeformationCount} delta={deformationDelta:+0;-0;0} " +
                $"state=[{BuildMotionState()}] collision=[{collisionContext}]");
        }
        else if (deformationDelta > 0)
        {
            var collisionContext = hasRecentCollision
                ? $"age={collisionAge:0.000}s {lastCollision.Describe()}"
                : "none within correlation window";

            AudiRS6RDiagnostics.Vehicle(
                "DEFORMATION_EVENT",
                $"{VehicleIdentity()} damageUnchanged={currentDamage:0.000000} " +
                $"deformations={lastDeformationCount}->{currentDeformationCount} delta={deformationDelta:+0;-0;0} " +
                $"state=[{BuildMotionState()}] collision=[{collisionContext}]");
        }
        else
        {
            AudiRS6RDiagnostics.Vehicle(
                "CONDITION_IMPROVED",
                $"{VehicleIdentity()} damage={lastDamage:0.000000}->{currentDamage:0.000000} " +
                $"deformations={lastDeformationCount}->{currentDeformationCount} state=[{BuildMotionState()}]");
        }

        lastDamage = currentDamage;
        lastDeformationCount = currentDeformationCount;
    }

    private void SuppressRoadSurfaceDamage()
    {
        if (vehicleController?.vehicleInstance == null)
            return;

        var instance = vehicleController.vehicleInstance;
        var roadOnlyThisFrame = roadSurfaceContactFrame == Time.frameCount &&
                                obstacleContactFrame != Time.frameCount;
        var roadDamageMayBePending = Time.unscaledTime <= roadDamageSuppressionUntil &&
                                     (!hasCollisionSnapshot || lastCollision.IsRoadSurfaceContact);
        var queueEntriesDiscarded = roadOnlyThisFrame ? ClearPendingDeformationQueue() : 0;
        var deformationEntriesDiscarded = 0;
        var damageDiscarded = 0f;

        if (roadOnlyThisFrame && !MatchesApprovedDeformations(instance.deformations))
        {
            deformationEntriesDiscarded = Math.Max(0, instance.deformations.Count - approvedDeformations.Count);
            instance.deformations.Clear();
            instance.deformations.AddRange(approvedDeformations);
        }

        if (roadDamageMayBePending && instance.damage > approvedDamage + DamageTolerance)
        {
            damageDiscarded = instance.damage - approvedDamage;
            if (vehicleController is CarController carController)
                carController.SetDamage(approvedDamage);
            else
                instance.damage = approvedDamage;
        }

        if (queueEntriesDiscarded > 0 || deformationEntriesDiscarded > 0 || damageDiscarded > DamageTolerance)
        {
            AudiRS6RDiagnostics.Damage(
                $"classification=ROAD_SURFACE_DAMAGE_SUPPRESSED {VehicleIdentity()} " +
                $"queueEntriesDiscarded={queueEntriesDiscarded} " +
                $"deformationEntriesDiscarded={deformationEntriesDiscarded} " +
                $"damageDiscarded={damageDiscarded:0.000000} state=[{BuildMotionState()}] " +
                $"collision=[{(hasCollisionSnapshot ? lastCollision.Describe() : "none")}]");
        }

        if (!roadOnlyThisFrame && !roadDamageMayBePending)
        {
            approvedDamage = instance.damage;
            SnapshotApprovedDeformations();
        }
    }

    private int ClearPendingDeformationQueue()
    {
        if (deformationController == null || deformationQueueField == null)
            return 0;

        var queue = deformationQueueField.GetValue(deformationController);
        if (queue == null)
            return 0;

        var queueType = queue.GetType();
        var countProperty = queueType.GetProperty("Count");
        var count = countProperty?.GetValue(queue) is int currentCount ? currentCount : 0;
        queueType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)?.Invoke(queue, null);
        return count;
    }

    private void SnapshotApprovedDeformations()
    {
        approvedDeformations.Clear();
        var deformations = vehicleController?.vehicleInstance?.deformations;
        if (deformations != null)
            approvedDeformations.AddRange(deformations);
    }

    private bool MatchesApprovedDeformations(List<VehicleDeformationController.VehicleDeformation> deformations)
    {
        if (deformations.Count != approvedDeformations.Count)
            return false;

        for (var index = 0; index < deformations.Count; index++)
        {
            if (!ReferenceEquals(deformations[index], approvedDeformations[index]))
                return false;
        }

        return true;
    }

    private string ClassifyDamage(bool hasRecentCollision)
    {
        if (hasRecentCollision)
            return lastCollision.IsRoadLike ? "POSSIBLE_ROAD_DAMAGE" : "COLLISION_DAMAGE";

        if (vehicleController != null &&
            vehicleController.controlledByPlayer &&
            GetSpeedKph() >= MinimumDrivingSpeedKph &&
            Vector3.Dot(transform.up, Vector3.up) >= 0.7f)
        {
            return "UNATTRIBUTED_REGULAR_DRIVING_DAMAGE";
        }

        return "UNATTRIBUTED_DAMAGE";
    }

    private string BuildAttachmentMessage()
    {
        var rigidbodyDescription = body == null
            ? "rigidbody=<none>"
            : $"rigidbody=[mass={body.mass:0.###} drag={body.drag:0.###} angularDrag={body.angularDrag:0.###} " +
              $"centerOfMass={FormatVector(body.centerOfMass)} worldCenterOfMass={FormatVector(body.worldCenterOfMass)} " +
              $"collisionMode={body.collisionDetectionMode} interpolation={body.interpolation}]";

        return $"{VehicleIdentity()} root={GetHierarchyPath(transform)} {rigidbodyDescription} " +
               $"roadDeformationGuard=[controllerFound={deformationController != null} " +
               $"queueAccessReady={deformationQueueField != null} approvedDamage={approvedDamage:0.000000} " +
               $"approvedDeformations={approvedDeformations.Count}] " +
               $"colliders=[{DescribeColliders()}] state=[{BuildMotionState()}]";
    }

    private string BuildTelemetryMessage(bool includeCollisionContext)
    {
        var message = $"{VehicleIdentity()} state=[{BuildMotionState()}]";
        if (!includeCollisionContext)
            return message;

        if (!hasCollisionSnapshot)
            return message + " lastCollision=[none]";

        var age = Time.unscaledTime - lastCollision.Time;
        return message + $" lastCollision=[age={age:0.000}s {lastCollision.Describe()}]";
    }

    private string BuildMotionState()
    {
        var instance = vehicleController?.vehicleInstance;
        var velocity = body != null ? body.velocity : Vector3.zero;
        var angularVelocity = body != null ? body.angularVelocity : Vector3.zero;
        var streetName = instance?.streetName ?? string.Empty;

        return $"controlled={vehicleController?.controlledByPlayer ?? false} speedKph={GetSpeedKph():0.000} " +
               $"position={FormatVector(transform.position)} rotation={FormatVector(transform.eulerAngles)} " +
               $"velocity={FormatVector(velocity)} angularVelocity={FormatVector(angularVelocity)} " +
               $"upright={Vector3.Dot(transform.up, Vector3.up):0.000} damage={instance?.damage ?? 0f:0.000000} " +
               $"deformations={instance?.deformations?.Count ?? 0} street=\"{Sanitize(streetName)}\" " +
               $"parkingState={instance?.parkingState.ToString() ?? "<none>"} scene=\"{Sanitize(SceneManager.GetActiveScene().name)}\"";
    }

    private string VehicleIdentity()
    {
        var instance = vehicleController?.vehicleInstance;
        return $"vehicleId=\"{Sanitize(instance?.id ?? "<none>")}\" " +
               $"vehicleType=\"{Sanitize(instance?.vehicleTypeName ?? "<none>")}\"";
    }

    private float GetSpeedKph()
    {
        return body != null ? body.velocity.magnitude * 3.6f : 0f;
    }

    private string DescribeColliders()
    {
        var colliders = GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
            return "none";

        var builder = new StringBuilder();
        var written = 0;
        foreach (var collider in colliders)
        {
            if (collider == null || collider.isTrigger)
                continue;

            if (written > 0)
                builder.Append("; ");

            builder.Append("path=").Append(GetHierarchyPath(collider.transform));
            builder.Append(" type=").Append(collider.GetType().Name);
            builder.Append(" enabled=").Append(collider.enabled);
            builder.Append(" boundsCenter=").Append(FormatVector(collider.bounds.center));
            builder.Append(" boundsSize=").Append(FormatVector(collider.bounds.size));

            if (collider is BoxCollider boxCollider)
            {
                builder.Append(" localCenter=").Append(FormatVector(boxCollider.center));
                builder.Append(" localSize=").Append(FormatVector(boxCollider.size));
            }

            written++;
            if (written >= 12)
            {
                builder.Append("; remainingCollidersOmitted=").Append(Math.Max(0, colliders.Length - written));
                break;
            }
        }

        return written > 0 ? builder.ToString() : "none (all colliders are triggers)";
    }

    private static bool IsRoadLike(string name, string path, string tag, string layerName)
    {
        var objectName = name.ToLowerInvariant();
        var objectIdentity = $"{name} {path} {tag}".ToLowerInvariant();
        var normalizedLayerName = layerName.ToLowerInvariant();

        if (objectName.Contains("tree") ||
            objectName.Contains("pine") ||
            objectName.Contains("trunk") ||
            objectName.Contains("fence") ||
            objectName.Contains("wall") ||
            objectName.Contains("building") ||
            objectName.Contains("pole") ||
            objectName.Contains("lamp") ||
            objectName.Contains("hydrant") ||
            objectName.Contains("barricade") ||
            objectName.Contains("delimiter") ||
            objectName.Contains("barrier") ||
            objectName.Contains("bollard") ||
            objectName.Contains("curb") ||
            objectName.Contains("kerb") ||
            objectName.Contains("sidewalk") ||
            objectName.Contains("pavement"))
        {
            return false;
        }

        if (objectName.Contains("road"))
            return true;

        if (normalizedLayerName.Contains("prop"))
            return false;

        if (normalizedLayerName == "ground" ||
            normalizedLayerName == "terrain" ||
            normalizedLayerName == "road" ||
            normalizedLayerName == "roads")
        {
            return true;
        }

        return objectIdentity.Contains("groundplane") ||
               objectIdentity.Contains("ground_plane") ||
               objectIdentity.Contains("roadmesh") ||
               objectIdentity.Contains("road_mesh") ||
               objectIdentity.Contains("roadsurface") ||
               objectIdentity.Contains("road_surface") ||
               objectIdentity.Contains("asphalt") ||
               objectIdentity.Contains("terrain");
    }

    private static string GetHierarchyPath(Transform? target)
    {
        if (target == null)
            return "<none>";

        var path = target.name;
        var current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static string FormatVector(Vector3 value)
    {
        return FormattableString.Invariant($"({value.x:0.000},{value.y:0.000},{value.z:0.000})");
    }

    private static string Sanitize(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
    }

    private readonly struct CollisionSnapshot
    {
        public CollisionSnapshot(
            float time,
            bool isRoadLike,
            bool isRoadSurfaceContact,
            string otherName,
            string otherPath,
            string selfPath,
            string tag,
            int layer,
            string layerName,
            float relativeSpeedKph,
            float impulse,
            Vector3 point,
            Vector3 normal)
        {
            Time = time;
            IsRoadLike = isRoadLike;
            IsRoadSurfaceContact = isRoadSurfaceContact;
            OtherName = otherName;
            OtherPath = otherPath;
            SelfPath = selfPath;
            Tag = tag;
            Layer = layer;
            LayerName = layerName;
            RelativeSpeedKph = relativeSpeedKph;
            Impulse = impulse;
            Point = point;
            Normal = normal;
        }

        public float Time { get; }
        public bool IsRoadLike { get; }
        public bool IsRoadSurfaceContact { get; }
        private string OtherName { get; }
        private string OtherPath { get; }
        private string SelfPath { get; }
        private string Tag { get; }
        private int Layer { get; }
        private string LayerName { get; }
        private float RelativeSpeedKph { get; }
        private float Impulse { get; }
        private Vector3 Point { get; }
        private Vector3 Normal { get; }

        public string Describe()
        {
            return $"roadLike={IsRoadLike} roadSurface={IsRoadSurfaceContact} " +
                   $"other=\"{Sanitize(OtherName)}\" otherPath=\"{Sanitize(OtherPath)}\" " +
                   $"selfCollider=\"{Sanitize(SelfPath)}\" tag=\"{Sanitize(Tag)}\" layer={Layer} " +
                   $"layerName=\"{Sanitize(LayerName)}\" relativeSpeedKph={RelativeSpeedKph:0.000} " +
                   $"impulse={Impulse:0.000} point={FormatVector(Point)} normal={FormatVector(Normal)}";
        }
    }
}
