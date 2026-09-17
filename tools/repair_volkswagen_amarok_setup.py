from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup file was not found: {SETUP}")

text = SETUP.read_text(encoding="utf-8")
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
    SETUP.write_text(text, encoding="utf-8", newline="\n")
    print("Inserted missing AttachLightOverlaySources helper.")
else:
    print("AttachLightOverlaySources helper already present.")

# Fail here instead of letting Unity be the first place that discovers a broken
# generated source file.
check = SETUP.read_text(encoding="utf-8")
if call not in check or signature not in check:
    raise SystemExit("Amarok setup light-overlay preflight failed.")

print("VolkswagenAmarokSetup.cs light-overlay preflight passed.")
