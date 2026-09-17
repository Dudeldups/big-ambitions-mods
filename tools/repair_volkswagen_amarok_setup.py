from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup file was not found: {SETUP}")

text = SETUP.read_text(encoding="utf-8")

# ---------------------------------------------------------------------------
# Light-overlay helper
# ---------------------------------------------------------------------------
call = "AttachLightOverlaySources(root, modelInstance);"
signature = "private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)"
marker = "    private static void ConfigureExitMarkers(GameObject root, GameObject model)"

helper = '''    private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)\n    {\n        var source = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);\n        if (source == null)\n            throw new InvalidOperationException(\n                "Models/AmarokLightOverlays.glb is missing. Export it from the supplied Blender vertex groups first.");\n\n        var overlay = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject ??\n                      throw new InvalidOperationException("Could not instantiate Amarok light overlays.");\n        PrefabUtility.UnpackPrefabInstance(\n            overlay,\n            PrefabUnpackMode.Completely,\n            InteractionMode.AutomatedAction);\n\n        overlay.name = "AmarokLightSources";\n        overlay.transform.localPosition = modelInstance.transform.localPosition;\n        overlay.transform.localRotation = modelInstance.transform.localRotation;\n        overlay.transform.localScale = modelInstance.transform.localScale;\n\n        // These meshes are source geometry only. VolkswagenAmarokLightingController\n        // creates the visible emissive overlays from them at runtime.\n        foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))\n            renderer.enabled = false;\n    }\n\n'''

if call not in text:
    raise SystemExit("The Amarok setup does not contain the light-overlay attach call; regenerate it first.")

if signature not in text:
    if marker not in text:
        raise SystemExit("Could not find ConfigureExitMarkers insertion point in VolkswagenAmarokSetup.cs.")
    text = text.replace(marker, helper + marker, 1)
    print("Inserted missing AttachLightOverlaySources helper.")
else:
    print("AttachLightOverlaySources helper already present.")

# ---------------------------------------------------------------------------
# Amarok driver/passenger exit markers
# ---------------------------------------------------------------------------
# The Porsche setup searched for a donor-specific 'steer_3' transform. The
# supplied Amarok GLB uses 'vw_amorak_2018:steering_ok'. Its parent pivot is at
# the model origin, so use the bounds of the actual steering-wheel renderers to
# determine which side of the cabin contains the steering wheel.
exit_method = '''    private static void ConfigureExitMarkers(GameObject root, GameObject model)\n    {\n        var steeringWheel = FindTransformWithNameFragment(model.transform, "steering_ok") ??\n                            throw new InvalidOperationException("Amarok steering wheel node 'steering_ok' is missing.");\n        if (!TryGetRendererBounds(steeringWheel, out var steeringBounds))\n            throw new InvalidOperationException("Amarok steering wheel renderer bounds are missing.");\n\n        var steeringPosition = root.transform.InverseTransformPoint(steeringBounds.center);\n        var driverSide = steeringPosition.x < 0f ? -1.55f : 1.55f;\n        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.12f, -0.10f));\n        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.12f, -0.10f));\n        Debug.Log(\n            $"VolkswagenAmarok: steering wheel x={steeringPosition.x:F3}; " +\n            $"driver exit x={driverSide:F2}.");\n    }\n'''

pattern = re.compile(
    r'    private static void ConfigureExitMarkers\(GameObject root, GameObject model\)\s*\{.*?\n    \}\n',
    re.S,
)
text, count = pattern.subn(exit_method, text, count=1)
if count != 1:
    raise SystemExit("Could not patch ConfigureExitMarkers in VolkswagenAmarokSetup.cs.")
print("Patched ConfigureExitMarkers for Amarok steering_ok geometry.")

SETUP.write_text(text, encoding="utf-8", newline="\n")

# Fail here instead of letting Unity be the first place that discovers a broken
# generated source file.
check = SETUP.read_text(encoding="utf-8")
required = [
    call,
    signature,
    'FindTransformWithNameFragment(model.transform, "steering_ok")',
    'TryGetRendererBounds(steeringWheel, out var steeringBounds)',
]
missing = [value for value in required if value not in check]
if missing:
    raise SystemExit("Amarok setup preflight failed; missing: " + ", ".join(missing))

print("VolkswagenAmarokSetup.cs preflight passed.")
