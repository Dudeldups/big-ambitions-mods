from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
PRIVATE_DRIVER = MOD / "Scripts/VolkswagenAmarokPrivateDriverSupport.cs"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"

for path in (PRIVATE_DRIVER, SETUP):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


# ---------------------------------------------------------------------------
# NPC / private-driver fixes ported from the current BMW/Lamborghini traffic fix:
# - native ambient traffic color instead of always-white custom geometry
# - copy the Amarok player body colliders instead of retaining the donor AI body
# - fit standing NavMesh obstacles to those body bounds
# - bind custom wheel visuals exactly once to prevent cumulative wheel wobble
# ---------------------------------------------------------------------------
text = PRIVATE_DRIVER.read_text(encoding="utf-8")

if "using UnityEngine.AI;" not in text:
    text = text.replace("using UnityEngine;\n", "using UnityEngine;\nusing UnityEngine.AI;\n", 1)

material_marker = "        VolkswagenAmarokMaterials.FixSolidMaterials(clone);\n"
traffic_setup = '''        VolkswagenAmarokMaterials.FixSolidMaterials(clone);
        foreach (var renderer in templateRenderers)
            renderer.enabled = false;
        clone.AddComponent<VolkswagenAmarokAmbientTrafficAppearance>();
        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
'''
if "clone.AddComponent<VolkswagenAmarokAmbientTrafficAppearance>();" not in text:
    if material_marker not in text:
        raise SystemExit("Could not locate Amarok private-driver material setup.")
    text = text.replace(material_marker, traffic_setup, 1)

load_marker = "    private static GameObject? LoadAiTemplate()\n"
traffic_helpers = r'''    private static bool FitAiBodyColliders(GameObject clone, GameObject playerPrefab)
    {
        var source = FindTransform(playerPrefab.transform, "BodyCollider");
        var sourceBoxes = source?.GetComponents<BoxCollider>();
        if (source == null || sourceBoxes == null || sourceBoxes.Length == 0)
        {
            Debug.LogWarning("VolkswagenAmarok NPC body collider source is missing.");
            return false;
        }

        var disabled = 0;
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || collider is WheelCollider ||
                IsTrafficWheel(collider.transform, clone.transform))
                continue;
            collider.enabled = false;
            disabled++;
        }

        var holder = new GameObject("VolkswagenAmarok NpcBodyCollider");
        holder.layer = clone.layer;
        holder.transform.SetParent(clone.transform, false);
        // The Amarok traffic presentation is raised 6 cm relative to the donor
        // AI root; move its copied body collider by the same amount.
        holder.transform.localPosition = source.localPosition + new Vector3(0f, 0.060f, 0f);
        holder.transform.localRotation = source.localRotation;
        holder.transform.localScale = source.localScale;

        var rear = float.PositiveInfinity;
        var front = float.NegativeInfinity;
        var left = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        foreach (var original in sourceBoxes)
        {
            if (!original.enabled || original.isTrigger)
                continue;
            var box = holder.AddComponent<BoxCollider>();
            box.center = original.center;
            box.size = original.size;
            box.sharedMaterial = original.sharedMaterial;

            rear = Mathf.Min(rear, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(
                    box.center - Vector3.forward * box.size.z * 0.5f)).z);
            front = Mathf.Max(front, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(
                    box.center + Vector3.forward * box.size.z * 0.5f)).z);
            left = Mathf.Min(left, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(
                    box.center - Vector3.right * box.size.x * 0.5f)).x);
            right = Mathf.Max(right, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(
                    box.center + Vector3.right * box.size.x * 0.5f)).x);
        }

        if (front <= rear || right <= left)
        {
            Debug.LogWarning("VolkswagenAmarok NPC body colliders have invalid bounds.");
            UnityEngine.Object.Destroy(holder);
            return false;
        }

        FitAiNavigationObstacles(clone, left, right, rear, front);
        Debug.Log(
            $"VolkswagenAmarok NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
            $"disabledTemplate={disabled} rootRear={rear:0.000} rootFront={front:0.000}.");
        return true;
    }

    private static bool IsTrafficWheel(Transform candidate, Transform root)
    {
        for (var current = candidate; current != null && current != root; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name == "FL" || name == "FR" || name == "BL" || name == "BR")
                return true;
        }
        return false;
    }

    private static void FitAiNavigationObstacles(
        GameObject clone,
        float bodyLeft,
        float bodyRight,
        float bodyRear,
        float bodyFront)
    {
        const float pedestrianClearance = 0.40f;
        var root = clone.transform;
        foreach (var obstacle in clone.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle.shape != NavMeshObstacleShape.Box ||
                Vector3.Dot(root.forward, obstacle.transform.forward) < 0.99f)
            {
                Debug.LogWarning(
                    $"VolkswagenAmarok NPC obstacle could not be fitted name='{obstacle.name}'.");
                continue;
            }

            var targetLeft = bodyLeft + pedestrianClearance;
            var targetRight = bodyRight - pedestrianClearance;
            var targetRear = bodyRear + pedestrianClearance;
            var targetFront = bodyFront - pedestrianClearance;

            var localLeft = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(targetLeft, 0f, 0f))).x;
            var localRight = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(targetRight, 0f, 0f))).x;
            var localRear = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(0f, 0f, targetRear))).z;
            var localFront = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(0f, 0f, targetFront))).z;

            if (localFront <= localRear + 0.05f ||
                localRight <= localLeft + 0.05f)
                continue;

            var center = obstacle.center;
            center.x = (localLeft + localRight) * 0.5f;
            center.z = (localRear + localFront) * 0.5f;
            var size = obstacle.size;
            size.x = localRight - localLeft;
            size.z = localFront - localRear;
            obstacle.center = center;
            obstacle.size = size;
        }
    }

'''
if "private static bool FitAiBodyColliders(" not in text:
    if load_marker not in text:
        raise SystemExit("Could not locate Amarok AI template loader.")
    text = text.replace(load_marker, traffic_helpers + load_marker, 1)

# Add the ambient traffic color path used by BMW and Lamborghini.
appearance_marker = "[DefaultExecutionOrder(1000)]\ninternal sealed class VolkswagenAmarokPrivateDriverAppearance"
ambient_class = r'''[DefaultExecutionOrder(1001)]
internal sealed class VolkswagenAmarokAmbientTrafficAppearance : MonoBehaviour
{
    private const int NativeColorAssignmentFrameLimit = 4;
    private Coroutine? initializationCoroutine;

    private void OnEnable()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(ApplyNativeTrafficColor());
    }

    private void OnDisable()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
    }

    private IEnumerator ApplyNativeTrafficColor()
    {
        for (var frame = 0; frame < NativeColorAssignmentFrameLimit; frame++)
        {
            if (GetComponent<PrivateDriverVehicle>() != null)
            {
                initializationCoroutine = null;
                yield break;
            }
            yield return null;
        }

        if (GetComponent<PrivateDriverVehicle>() == null)
        {
            var color = GetComponent<CarFeatures>()?.VehicleColor;
            var applied = false;
            if (color != null)
            {
                var paint = GetComponent<VolkswagenAmarokPaintController>();
                if (paint == null)
                    paint = gameObject.AddComponent<VolkswagenAmarokPaintController>();
                paint.InitializeForPrivateDriver(color.name, color);
                applied = paint.HasAppliedColor;
            }

            Debug.Log(
                $"VolkswagenAmarok ambient NPC id={GetInstanceID()} " +
                $"color='{(color != null ? color.name : "<none>")}' applied={applied}.");
        }
        initializationCoroutine = null;
    }
}

'''
if "class VolkswagenAmarokAmbientTrafficAppearance" not in text:
    if appearance_marker not in text:
        raise SystemExit("Could not locate Amarok private-driver appearance class.")
    text = text.replace(appearance_marker, ambient_class + appearance_marker, 1)

# Prevent Awake + OnEnable from recording duplicate wheel offsets. Duplicate
# bindings accumulate movement from the donor FL/FR/BL/BR transforms and are the
# source of the traffic/private-driver 'wobbly wheels' behavior.
wheel_field = "    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);\n"
if "private bool wheelVisualsBound;" not in text:
    if wheel_field not in text:
        raise SystemExit("Could not locate Amarok private-driver wheel bindings.")
    text = text.replace(
        wheel_field,
        wheel_field + "    private bool wheelVisualsBound;\n",
        1,
    )

bind_signature = '''    internal void BindWheelVisuals()
    {
'''
if "if (wheelVisualsBound)" not in text:
    if bind_signature not in text:
        raise SystemExit("Could not locate Amarok BindWheelVisuals().")
    text = text.replace(
        bind_signature,
        bind_signature + "        if (wheelVisualsBound)\n            return;\n",
        1,
    )

if "wheelVisualsBound = wheelBindings.Count == WheelNames.GetLength(0);" not in text:
    bind_end_pattern = re.compile(
        r'''(    internal void BindWheelVisuals\(\)\s*
    \{.*?)(\n    \}\n\n    private void BindCaliper)''',
        re.S,
    )
    text, bind_end_count = bind_end_pattern.subn(
        r'''\1
        wheelVisualsBound = wheelBindings.Count == WheelNames.GetLength(0);\2''',
        text,
        count=1,
    )
    if bind_end_count != 1:
        bind_end_pattern = re.compile(
            r'''(    internal void BindWheelVisuals\(\)\s*
    \{.*?)(\n    \}\n\n    private void Awake\(\) => BindWheelVisuals\(\);)''',
            re.S,
        )
        text, bind_end_count = bind_end_pattern.subn(
            r'''\1
        wheelVisualsBound = wheelBindings.Count == WheelNames.GetLength(0);\2''',
            text,
            count=1,
        )
    if bind_end_count != 1:
        raise SystemExit("Could not finalize Amarok wheel-binding guard.")

PRIVATE_DRIVER.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Vanilla service compatibility: keep the donor's refuel host active, rebind the
# game-facing vehicleCollider to the fitted Amarok BodyCollider, and make the
# legacy deformation arrays safe for CarController.Repair().
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

service_call_pattern = re.compile(
    r'(            ConfigureBodyColliders\(root\);\n)'
    r'(?!            ConfigureServiceCompatibility\(root\);)'
)
setup, service_call_count = service_call_pattern.subn(
    r'\1            ConfigureServiceCompatibility(root);\n',
    setup,
)
if service_call_count == 0 and "ConfigureServiceCompatibility(root);" not in setup:
    raise SystemExit("Could not locate Amarok BodyCollider service-compatibility call sites.")

helper_marker = "    private static void ConfigurePowertrain(GameObject root)\n"
service_helper = r'''    private static void ConfigureServiceCompatibility(GameObject root)
    {
        var bodyHolder = FindTransform(root.transform, "BodyCollider") ??
                         throw new InvalidOperationException(
                             "Amarok service compatibility: BodyCollider is missing.");
        BoxCollider? serviceCollider = null;
        foreach (var box in bodyHolder.GetComponents<BoxCollider>())
        {
            if (box != null && box.enabled && !box.isTrigger)
            {
                serviceCollider = box;
                break;
            }
        }
        if (serviceCollider == null)
            throw new InvalidOperationException(
                "Amarok service compatibility: no enabled body BoxCollider is available.");

        var refuelingPosition = FindTransform(root.transform, "RefuelingPosition");
        if (refuelingPosition == null)
        {
            var host = new GameObject("RefuelingPosition");
            host.transform.SetParent(root.transform, false);
            // Fuel flap area on the Amarok rear quarter. This transform is a
            // service anchor only; station eligibility is still vanilla.
            host.transform.localPosition = new Vector3(-0.92f, 0.82f, -1.55f);
            refuelingPosition = host.transform;
        }
        refuelingPosition.gameObject.SetActive(true);
        refuelingPosition.gameObject.layer = 12;

        var colliderBindings = 0;
        var refuelBindings = 0;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            var serialized = new SerializedObject(component);
            var vehicleCollider = serialized.FindProperty("vehicleCollider");
            if (vehicleCollider?.propertyType == SerializedPropertyType.ObjectReference)
            {
                vehicleCollider.objectReferenceValue = serviceCollider;
                colliderBindings++;
            }

            var refuel = serialized.FindProperty("refuelingPosition") ??
                         serialized.FindProperty("refuelingTransform") ??
                         serialized.FindProperty("fuelingPosition");
            if (refuel?.propertyType == SerializedPropertyType.ObjectReference)
            {
                refuel.objectReferenceValue = refuelingPosition;
                refuelBindings++;
            }

            if (string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
            {
                // Runtime owns Amarok visual deformation. Keep the legacy arrays
                // empty, matching BMW, so CarController.Repair().Reset() cannot
                // index an intentionally absent original-mesh entry.
                var meshFilters = serialized.FindProperty("meshFilters");
                if (meshFilters != null && meshFilters.isArray)
                    meshFilters.ClearArray();
                var originals = serialized.FindProperty("originalMeshes");
                if (originals != null && originals.isArray)
                    originals.ClearArray();
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log(
            $"VolkswagenAmarok service compatibility: colliderBindings={colliderBindings}, " +
            $"refuelBindings={refuelBindings}, refuelLocal={refuelingPosition.localPosition}.");
    }

'''
if "private static void ConfigureServiceCompatibility(GameObject root)" not in setup:
    if helper_marker not in setup:
        raise SystemExit("Could not locate Amarok service helper insertion point.")
    setup = setup.replace(helper_marker, service_helper + helper_marker, 1)

SETUP.write_text(setup, encoding="utf-8", newline="\n")


checks = {
    PRIVATE_DRIVER: [
        "using UnityEngine.AI;",
        "VolkswagenAmarokAmbientTrafficAppearance",
        "FitAiBodyColliders(clone, playerPrefab)",
        '"VolkswagenAmarok NpcBodyCollider"',
        "FitAiNavigationObstacles",
        "private bool wheelVisualsBound;",
        "if (wheelVisualsBound)",
    ],
    SETUP: [
        "ConfigureServiceCompatibility(root);",
        "private static void ConfigureServiceCompatibility(GameObject root)",
        '"RefuelingPosition"',
        '"vehicleCollider"',
        '"VehicleDeformationController"',
        "meshFilters.ClearArray();",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok seventh traffic/services patch failed:\n- " + "\n- ".join(missing))

print("Ported BMW/Lamborghini ambient traffic color assignment to Amarok NPCs.")
print("Replaced donor NPC body colliders/NavMesh bounds with fitted Amarok BodyCollider copies.")
print("Added one-time private-driver wheel binding to prevent wobbly/offset NPC wheels.")
print("Rebound vanilla vehicleCollider/refueling anchors and made repair reset arrays safe.")
print("Volkswagen Amarok seventh traffic/services preflight passed.")
