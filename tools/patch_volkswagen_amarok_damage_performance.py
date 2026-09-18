from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
RUNTIME = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokRuntime.cs"

if not RUNTIME.is_file():
    raise SystemExit(f"Generated Amarok runtime source is missing: {RUNTIME}")

text = RUNTIME.read_text(encoding="utf-8")

start_marker = "    private void OnCollisionEnter(Collision collision)"
end_marker = "    private static bool IsAttachedExteriorDetail("
start = text.find(start_marker)
end = text.find(end_marker, start + len(start_marker)) if start >= 0 else -1
if start < 0 or end < 0 or end <= start:
    raise SystemExit(
        "Could not locate VolkswagenAmarokVisualDamageController collision method "
        f"boundaries start={start} end={end}."
    )

method = r'''    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
            return;

        try
        {
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var contacts = collision.contacts;
            if (contacts.Length == 0)
                return;

            var excessSpeed = collision.relativeVelocity.magnitude - impactThresholdMps;
            var dentDepth = Mathf.Clamp(
                excessSpeed * DepthPerExcessMps,
                0.025f,
                MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.04f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.04f,
                MaximumRearDentDepth);
            var center = body != null ? body.worldCenterOfMass : transform.position;
            var primaryLocalContact = transform.InverseTransformPoint(contacts[0].point);
            var changedMeshes = 0;
            var changedVertices = 0;
            var skippedMeshes = 0;
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;

                // Follow the finished Audi/BMW/Lamborghini damage path: reject
                // whole panels before touching mesh.vertices. This is especially
                // important for the Amarok's body/door paint panels.
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var canReachFilter = false;
                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var isEndContact =
                            Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                            Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        var isFrontContact = isEndContact && localContact.z >= 0f;
                        var influenceRadius = DentRadius;
                        if (isEndContact)
                        {
                            influenceRadius = isFrontContact
                                ? Mathf.Max(
                                    FrontDentLateralRadius,
                                    Mathf.Max(
                                        FrontDentVerticalRadius,
                                        FrontDentLongitudinalRadius))
                                : Mathf.Max(
                                    RearDentLateralRadius,
                                    Mathf.Max(
                                        RearDentVerticalRadius,
                                        RearDentLongitudinalRadius));
                        }

                        if (renderer.bounds.SqrDistance(contact.point) <=
                            influenceRadius * influenceRadius)
                        {
                            canReachFilter = true;
                            break;
                        }
                    }

                    if (!canReachFilter)
                    {
                        skippedMeshes++;
                        continue;
                    }
                }

                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var meshChanged = false;
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var worldVertex = filter.transform.TransformPoint(vertices[vertexIndex]);
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = dentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontImpact = false;

                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var isEndContact =
                            Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                            Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        var isFrontContact = isEndContact && localContact.z >= 0f;
                        float influence;
                        Vector3 candidateDirection;

                        if (isEndContact)
                        {
                            var localDelta =
                                transform.InverseTransformVector(worldVertex - contact.point);
                            var lateralRadius = isFrontContact
                                ? FrontDentLateralRadius
                                : RearDentLateralRadius;
                            var verticalRadius = isFrontContact
                                ? FrontDentVerticalRadius
                                : RearDentVerticalRadius;
                            var longitudinalRadius = isFrontContact
                                ? FrontDentLongitudinalRadius
                                : RearDentLongitudinalRadius;
                            var normalizedDistance = Mathf.Sqrt(
                                localDelta.x * localDelta.x /
                                (lateralRadius * lateralRadius) +
                                localDelta.y * localDelta.y /
                                (verticalRadius * verticalRadius) +
                                localDelta.z * localDelta.z /
                                (longitudinalRadius * longitudinalRadius));
                            influence = 1f - normalizedDistance;
                            candidateDirection = localContact.z >= 0f
                                ? -transform.forward
                                : transform.forward;
                        }
                        else
                        {
                            influence =
                                1f - Vector3.Distance(worldVertex, contact.point) / DentRadius;
                            var towardCenter = (center - contact.point).normalized;
                            var contactNormal = contact.normal.normalized;
                            candidateDirection =
                                Vector3.Dot(contactNormal, towardCenter) >= 0f
                                    ? contactNormal
                                    : -contactNormal;
                        }

                        if (influence <= strongestInfluence)
                            continue;

                        strongestInfluence = influence;
                        inwardDirection = candidateDirection;
                        selectedDepth = isEndContact
                            ? isFrontContact ? frontDentDepth : rearDentDepth
                            : dentDepth;
                        selectedEndImpact = isEndContact;
                        selectedFrontImpact = isFrontContact;
                    }

                    if (strongestInfluence <= 0f ||
                        inwardDirection.sqrMagnitude < 0.5f)
                    {
                        continue;
                    }

                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    worldVertex += inwardDirection * (selectedDepth * falloff);
                    var localVertex =
                        filter.transform.InverseTransformPoint(worldVertex);

                    // BMW/Lamborghini-style cumulative cap: keep repeated impacts
                    // bounded relative to the runtime mesh captured at spawn.
                    if (originalVertices.TryGetValue(filter, out var baseline) &&
                        vertexIndex < baseline.Length)
                    {
                        var cumulativeLimit = selectedEndImpact
                            ? selectedFrontImpact
                                ? MaximumFrontDentDepth
                                : MaximumRearDentDepth
                            : MaximumDentDepth;
                        localVertex = baseline[vertexIndex] +
                                      Vector3.ClampMagnitude(
                                          localVertex - baseline[vertexIndex],
                                          cumulativeLimit);
                    }

                    vertices[vertexIndex] = localVertex;
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;

                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                changedMeshes++;
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                var elapsedMilliseconds =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) *
                    1000d / System.Diagnostics.Stopwatch.Frequency;
                context?.Logger.Info(
                    $"VolkswagenAmarok damage vehicle={vehicle?.GetInstanceID()}: " +
                    $"inward dent contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={collision.relativeVelocity.magnitude * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} skipped={skippedMeshes} " +
                    $"vertices={changedVertices} processing={elapsedMilliseconds:0.0}ms " +
                    $"nwhDamage={(damageHandler?.Damage ?? 0f) * 100f:0.0}% " +
                    $"vehicleDamage={(vehicle?.vehicleInstance?.damage ?? 0f) * 100f:0.0}%.");
            }
        }
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn(
                $"VolkswagenAmarok damage vehicle={vehicle?.GetInstanceID()}: " +
                $"inward deformation failed with {exception.GetType().Name}: " +
                $"{exception.Message}");
        }
    }

'''

text = text[:start] + method + text[end:]
RUNTIME.write_text(text, encoding="utf-8", newline="\n")

check = RUNTIME.read_text(encoding="utf-8")
required = [
    "renderer.bounds.SqrDistance(contact.point)",
    "skippedMeshes",
    "processing={elapsedMilliseconds:0.0}ms",
    "Vector3.ClampMagnitude(",
    "mesh.RecalculateBounds();",
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit(
        "Amarok damage-performance patch failed:\n- " + "\n- ".join(missing)
    )

# This Porsche-only allocation path must no longer be called from OnCollisionEnter.
method_check = check[
    check.find(start_marker):
    check.find(end_marker, check.find(start_marker) + len(start_marker))
]
if "appliedWorldDisplacements" in method_check or "IsAttachedExteriorDetail(" in method_check:
    raise SystemExit(
        "Amarok damage-performance patch failed: per-impact attached-detail "
        "allocation path is still active."
    )

print("Replaced Amarok per-impact attached-detail allocations with the finished-vehicle panel deformation path.")
print("Added Audi-style whole-panel bounds culling before mesh vertex buffers are read.")
print("Added crash deformation timing diagnostics for the first few impacts.")
print("Volkswagen Amarok damage-performance preflight passed.")
