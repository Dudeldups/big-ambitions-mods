from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
RUNTIME = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokRuntime.cs"

if not RUNTIME.is_file():
    raise SystemExit(f"Generated Amarok runtime source is missing: {RUNTIME}")

text = RUNTIME.read_text(encoding="utf-8")

# Match the finished BMW/Cadillac pattern: disabled source renderers are not
# deformation targets unless they are stateful lamp overlays that may become
# visible after the crash. The older Amarok path included every MeshRenderer,
# including hidden source geometry, which multiplied per-impact work.
configure_start_marker = "    private int ConfigureVisualDamage(VehicleController vehicle)"
configure_end_marker = "    private static bool IsDeformableExterior(MeshFilter filter)"
configure_start = text.find(configure_start_marker)
configure_end = text.find(
    configure_end_marker,
    configure_start + len(configure_start_marker) if configure_start >= 0 else 0,
)
if configure_start < 0 or configure_end < 0:
    raise SystemExit("Could not locate Amarok ConfigureVisualDamage() boundaries.")

configure_text = text[configure_start:configure_end]
old_filter_add = '''            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null)
                filters.Add(filter);
'''
new_filter_add = '''            var renderer = filter.GetComponent<MeshRenderer>();
            var statefulLampOverlay =
                filter.name.IndexOf("BHeadlights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("BDRL_Indicator_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("SideIndicator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("1RearDrivingLights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("1BrakeLights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("ThirdBrakeLight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("ReverseLights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("1IndicatorR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filter.name.IndexOf("VolkswagenAmarok_Indicator", StringComparison.OrdinalIgnoreCase) >= 0;
            if (renderer != null && (renderer.enabled || statefulLampOverlay))
                filters.Add(filter);
'''
if "statefulLampOverlay" not in configure_text:
    if old_filter_add not in configure_text:
        raise SystemExit(
            "Could not locate Amarok deformable filter-add block for enabled-renderer gating."
        )
    configure_text = configure_text.replace(old_filter_add, new_filter_add, 1)
    text = text[:configure_start] + configure_text + text[configure_end:]

collision_marker = "    private void OnCollisionEnter(Collision collision)"
visual_class_marker = "public sealed class VolkswagenAmarokVisualDamageController : MonoBehaviour"
helper_marker = "    private static bool IsAttachedExteriorDetail("

# Commit e59d285 temporarily searched OnCollisionEnter globally. On an already-run
# worktree it could therefore replace the CollisionSeparationController method and
# everything through the VisualDamageController helper. Repair that exact region
# from the finished Porsche donor architecture (renamed to Amarok) before applying
# the optimized, class-scoped collision method below.
known_good_repair_region = r'''    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || body == null || collision?.collider == null)
            return;
        var otherVehicle = collision.collider.GetComponentInParent<VehicleController>();
        if (otherVehicle == null || otherVehicle == vehicle)
            return;

        if (separationCoroutine != null)
            StopCoroutine(separationCoroutine);
        separationCoroutine = StartCoroutine(ResolveVehiclePenetration(otherVehicle));
    }

    private IEnumerator ResolveVehiclePenetration(VehicleController otherVehicle)
    {
        yield return new WaitForFixedUpdate();
        var otherColliders = otherVehicle != null
            ? otherVehicle.GetComponentsInChildren<Collider>(true)
            : Array.Empty<Collider>();
        for (var pass = 0; pass < MaximumPasses; pass++)
        {
            if (body == null || otherVehicle == null)
                break;

            var bestDirection = Vector3.zero;
            var bestDistance = 0f;
            foreach (var ownCollider in bodyColliders)
            {
                if (ownCollider == null || !ownCollider.enabled)
                    continue;
                foreach (var otherCollider in otherColliders)
                {
                    if (otherCollider == null || !otherCollider.enabled ||
                        otherCollider.isTrigger ||
                        otherCollider.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                            ownCollider,
                            ownCollider.transform.position,
                            ownCollider.transform.rotation,
                            otherCollider,
                            otherCollider.transform.position,
                            otherCollider.transform.rotation,
                            out var direction,
                            out var distance) ||
                        distance <= bestDistance)
                    {
                        continue;
                    }

                    bestDirection = direction;
                    bestDistance = distance;
                }
            }

            if (bestDistance <= MinimumPenetration || bestDirection.sqrMagnitude < 0.5f)
                break;

            var correction = Mathf.Min(
                bestDistance + SeparationPadding,
                MaximumCorrectionPerPass);
            body.position += bestDirection.normalized * correction;
            var inwardSpeed = Vector3.Dot(body.velocity, -bestDirection.normalized);
            if (inwardSpeed > 0f)
                body.velocity += bestDirection.normalized * inwardSpeed;
            body.WakeUp();
            yield return new WaitForFixedUpdate();
        }
        separationCoroutine = null;
    }

    private void OnDisable()
    {
        if (separationCoroutine != null)
            StopCoroutine(separationCoroutine);
        separationCoroutine = null;
    }
}
#endif

[AddComponentMenu("")]
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class VolkswagenAmarokHighwaySeamGuard : MonoBehaviour
{
    private const float MinimumSpeedMps = 40f;
    private const float MaximumSampleAgeSeconds = 0.1f;
    private const float MinimumUpwardContactNormal = 0.9f;
    private static readonly string[] KnownHighwaySurfaceNames =
    {
        "HamptonsAvenue_Highway",
        "HighwayAvenue_Highway",
        "X_IntersectionAASAAS_Highway",
    };

    private Rigidbody? body;
    private Vector3 velocityBeforeStep;
    private Vector3 angularVelocityBeforeStep;
    private float velocitySampleTime;

    internal void Initialize(Rigidbody vehicleBody)
    {
        body = vehicleBody;
    }

    private void FixedUpdate()
    {
        if (body == null || body.isKinematic)
            return;

        var planarVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        if (planarVelocity.sqrMagnitude < MinimumSpeedMps * MinimumSpeedMps)
            return;

        velocityBeforeStep = body.velocity;
        angularVelocityBeforeStep = body.angularVelocity;
        velocitySampleTime = Time.unscaledTime;
    }

    private void OnCollisionEnter(Collision collision)
    {
        CorrectKnownHighwaySeam(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CorrectKnownHighwaySeam(collision);
    }

    private void CorrectKnownHighwaySeam(Collision collision)
    {
        if (collision == null || body == null)
            return;

        var other = collision.collider;
        if (other == null ||
            Time.unscaledTime - velocitySampleTime > MaximumSampleAgeSeconds ||
            !IsKnownHighwaySurface(other.name) || !HasUpwardContact(collision))
        {
            return;
        }

        var correctedVelocity = body.velocity;
        if (correctedVelocity.y <= velocityBeforeStep.y)
            return;

        correctedVelocity.y = velocityBeforeStep.y;
        body.velocity = correctedVelocity;
        body.angularVelocity = angularVelocityBeforeStep;
    }

    private static bool HasUpwardContact(Collision collision)
    {
        for (var index = 0; index < collision.contactCount; index++)
        {
            if (collision.GetContact(index).normal.y >= MinimumUpwardContactNormal)
                return true;
        }
        return false;
    }

    private static bool IsKnownHighwaySurface(string objectName)
    {
        foreach (var surfaceName in KnownHighwaySurfaceNames)
        {
            if (objectName.IndexOf(surfaceName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}

[AddComponentMenu("")]
internal sealed class VolkswagenAmarokContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("Volkswagen Amarok body contact")
        {
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        return contactMaterial;
    }

    private void OnDestroy()
    {
        if (contactMaterial != null)
            Destroy(contactMaterial);
        contactMaterial = null;
    }
}

[AddComponentMenu("")]
public sealed class VolkswagenAmarokGlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
        new Dictionary<Material, Material>();
    private ModContext? context;
    private Coroutine? restoreCoroutine;
    private bool initialized;

    internal void Initialize(ModContext? modContext)
    {
        context = modContext;
        if (initialized)
        {
            EnsureVisible("reinitialize");
            return;
        }

        cabinGlass.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null ||
                    VolkswagenAmarokMaterials.GetTransparentRole(renderer, source) !=
                    VolkswagenAmarokTransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    VolkswagenAmarokMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
                    runtimeMaterials.Add(source, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }
            if (!containsCabinGlass)
                continue;
            renderer.sharedMaterials = materials;
            cabinGlass.Add(renderer);
        }
        initialized = true;
        EnsureVisible("initialize");
    }

    internal void RestoreAfterVehicleEntered()
    {
        if (!initialized)
            return;
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(RestoreAfterEntryLifecycle());
    }

    private IEnumerator RestoreAfterEntryLifecycle()
    {
        // Vehicle entry can alter renderer state after the entry callback. Two
        // deferred event passes restore glass once setup has settled, without a
        // permanent per-frame poll.
        yield return null;
        yield return new WaitForEndOfFrame();
        EnsureVisible("vehicle-entered");
        restoreCoroutine = null;
    }

    private void EnsureVisible(string source)
    {
        var restored = 0;
        var propertyBlocksCleared = 0;
        foreach (var renderer in cabinGlass)
        {
            if (renderer == null)
                continue;
            if (!renderer.enabled || renderer.forceRenderingOff)
                restored++;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            if (renderer.HasPropertyBlock())
            {
                renderer.SetPropertyBlock(null);
                propertyBlocksCleared++;
            }
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null &&
                    VolkswagenAmarokMaterials.GetTransparentRole(renderer, material) ==
                    VolkswagenAmarokTransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    VolkswagenAmarokMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            VolkswagenAmarokDiagnostics.Info(
                context,
                $"VolkswagenAmarok glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            VolkswagenAmarokDiagnostics.Info(
                context,
                $"VolkswagenAmarok glass vehicle={GetInstanceID()}: repaired after " +
                $"'{source}' renderers={restored}, propertyBlocks={propertyBlocksCleared}.");
        }
    }

    private void OnDestroy()
    {
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
        foreach (var material in runtimeMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
    }
}

[AddComponentMenu("")]
public sealed class VolkswagenAmarokVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.36f;
    private const float FrontDepthPerExcessMps = 0.012f;
    private const float RearDentLateralRadius = 0.88f;
    private const float RearDentVerticalRadius = 0.72f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.42f;
    private const float RearDepthPerExcessMps = 0.013f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Mesh> damageMeshes =
        new Dictionary<MeshFilter, Mesh>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private float previousSavedDamage;
    private int diagnosticLogs;
    private bool initialized;
    private bool failureReported;
    private Coroutine? repairRecoveryCoroutine;

    internal void Initialize(
        VehicleController controller,
        NWH.VehiclePhysics2.Damage.DamageHandler handler,
        ModContext? modContext,
        IReadOnlyList<MeshFilter> filters,
        float thresholdMps)
    {
        if (initialized && vehicle == controller)
            return;

        vehicle = controller;
        damageHandler = handler;
        context = modContext;
        body = controller.GetComponent<Rigidbody>();
        impactThresholdMps = thresholdMps;
        previousDamage = handler.Damage;
        previousSavedDamage = controller.vehicleInstance?.damage ?? 0f;
        deformableFilters.Clear();
        originalVertices.Clear();
        damageMeshes.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            originalVertices[filter] = runtimeMesh.vertices;
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
        }
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || damageHandler == null)
            return;

        var currentDamage = damageHandler.Damage;
        var currentSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        if ((previousDamage > 0.001f && currentDamage <= 0.001f) ||
            (previousSavedDamage > 0.001f && currentSavedDamage <= 0.001f))
        {
            foreach (var pair in originalVertices)
            {
                if (pair.Key == null || !damageMeshes.TryGetValue(pair.Key, out var mesh) ||
                    mesh == null)
                    continue;
                // CarController.Repair() invokes the disabled legacy deformation
                // component, which swaps its serialized source mesh back onto the
                // filter. Restore this vehicle-owned mesh before resetting it so
                // later impacts never mutate the shared prefab asset.
                pair.Key.sharedMesh = mesh;
                mesh.vertices = pair.Value;
                mesh.RecalculateBounds();
            }
            if (repairRecoveryCoroutine != null)
                StopCoroutine(repairRecoveryCoroutine);
            repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
            VolkswagenAmarokDiagnostics.DamageInfo(
                context,
                $"VolkswagenAmarok damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
        previousDamage = currentDamage;
        previousSavedDamage = currentSavedDamage;
    }

    private IEnumerator RestoreDrivingStateAfterRepair()
    {
        yield return null;
        for (var pass = 0; pass < 3; pass++)
        {
            yield return new WaitForFixedUpdate();
            if (vehicle == null || !vehicle.controlledByPlayer)
                continue;

            vehicle.SetFreeze(false);
            var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            if (physics != null)
            {
                physics.enabled = true;
                if (!physics.powertrain.engine.IsRunning)
                    physics.powertrain.engine.StartEngine();
                if (physics.powertrain.transmission.Gear == 0)
                    physics.powertrain.transmission.ShiftInto(1, true);
            }
            foreach (var wheelController in
                     vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
                wheelController.enabled = true;
            var rigidbody = vehicle.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = false;
                rigidbody.WakeUp();
            }
        }
        repairRecoveryCoroutine = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
            return;

        try
        {
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var contacts = collision.contacts;
            if (contacts.Length == 0)
                return;

            var excessSpeed = collision.relativeVelocity.magnitude - impactThresholdMps;
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.025f, MaximumDentDepth);
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
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var meshChanged = false;
                var attachedDetail = IsAttachedExteriorDetail(filter, vertices.Length);
                var appliedWorldDisplacements = attachedDetail
                    ? new Vector3[vertices.Length]
                    : null;
                var totalWorldDisplacement = Vector3.zero;
                var changedVerticesInMesh = 0;
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
                            var localDelta = transform.InverseTransformVector(worldVertex - contact.point);
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
                            influence = 1f - Vector3.Distance(worldVertex, contact.point) / DentRadius;
                            var towardCenter = (center - contact.point).normalized;
                            var contactNormal = contact.normal.normalized;
                            candidateDirection = Vector3.Dot(contactNormal, towardCenter) >= 0f
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

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    var worldDisplacement = inwardDirection * (selectedDepth * falloff);
                    worldVertex += worldDisplacement;
                    vertices[vertexIndex] = filter.transform.InverseTransformPoint(worldVertex);
                    if (appliedWorldDisplacements != null)
                        appliedWorldDisplacements[vertexIndex] = worldDisplacement;
                    totalWorldDisplacement += worldDisplacement;
                    changedVerticesInMesh++;
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;
                if (attachedDetail && appliedWorldDisplacements != null &&
                    changedVerticesInMesh > 0)
                {
                    // Lamps, badges, vents, fasteners, and similar separate
                    // pieces must remain attached to the panel. Translate the
                    // whole small mesh by the sampled regional deformation
                    // instead of leaving unaffected vertices hovering behind.
                    var averageDisplacement =
                        totalWorldDisplacement / changedVerticesInMesh;
                    for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        var undeformedWorld = filter.transform.TransformPoint(vertices[vertexIndex]) -
                                              appliedWorldDisplacements[vertexIndex];
                        vertices[vertexIndex] = filter.transform.InverseTransformPoint(
                            undeformedWorld + averageDisplacement);
                    }
                    changedVertices += vertices.Length - changedVerticesInMesh;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                changedMeshes++;
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                VolkswagenAmarokDiagnostics.DamageInfo(
                    context,
                    $"VolkswagenAmarok damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
                    $"contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={collision.relativeVelocity.magnitude * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} vertices={changedVertices} " +
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
                $"VolkswagenAmarok damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

'''

first_collision = text.find(collision_marker)
visual_class_start = text.find(visual_class_marker)
if first_collision >= 0 and (
    visual_class_start < 0 or first_collision < visual_class_start
):
    repair_end = text.find(helper_marker, first_collision + len(collision_marker))
    if repair_end < 0:
        raise SystemExit(
            "Amarok runtime appears damage-patch-corrupted but the repair end "
            "marker is missing."
        )
    text = (
        text[:first_collision]
        + known_good_repair_region
        + text[repair_end:]
    )
    print(
        "Repaired the previously over-broad Amarok damage patch region from "
        "CollisionSeparationController through VisualDamageController."
    )

visual_class_start = text.find(visual_class_marker)
if visual_class_start < 0:
    raise SystemExit("VolkswagenAmarokVisualDamageController class is missing after repair.")

start = text.find(
    collision_marker,
    visual_class_start + len(visual_class_marker),
)
end = text.find(
    helper_marker,
    start + len(collision_marker) if start >= 0 else visual_class_start,
)
if start < 0 or end < 0 or end <= start:
    raise SystemExit(
        "Could not locate class-scoped VolkswagenAmarokVisualDamageController "
        f"collision method boundaries start={start} end={end}."
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
    "statefulLampOverlay",
    "renderer.enabled || statefulLampOverlay",
    visual_class_marker,
    "public sealed class VolkswagenAmarokCollisionSeparationController",
    "internal sealed class VolkswagenAmarokHighwaySeamGuard",
    "public sealed class VolkswagenAmarokGlassController",
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

# Verify the optimized method is specifically inside VisualDamageController and
# that the older per-impact temporary-array path is gone from that method only.
visual_class_start = check.find(visual_class_marker)
method_start = check.find(collision_marker, visual_class_start)
method_end = check.find(helper_marker, method_start)
method_check = check[method_start:method_end]
if "appliedWorldDisplacements" in method_check or "IsAttachedExteriorDetail(" in method_check:
    raise SystemExit(
        "Amarok damage-performance patch failed: per-impact attached-detail "
        "allocation path is still active in VisualDamageController."
    )
if check.find(collision_marker) == method_start:
    raise SystemExit(
        "Amarok damage-performance preflight expected earlier non-damage "
        "OnCollisionEnter methods but none were restored."
    )

print("Excluded hidden Amarok source renderers from crash deformation while retaining stateful lamp overlays.")
print("Scoped Amarok crash deformation patch to VolkswagenAmarokVisualDamageController only.")
print("Replaced per-impact attached-detail allocations with the finished-vehicle panel deformation path.")
print("Added Audi-style whole-panel bounds culling before mesh vertex buffers are read.")
print("Added crash deformation timing diagnostics for the first few impacts.")
print("Volkswagen Amarok damage-performance preflight passed.")
