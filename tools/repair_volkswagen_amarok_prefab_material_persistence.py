from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"
CREATE = REPO / "tools/create_volkswagen_amarok.py"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup file was not found: {SETUP}")

text = SETUP.read_text(encoding="utf-8")

old_call = "            var fix = VolkswagenAmarokMaterials.FixSolidMaterials(root);\n"
new_call = "            var fix = PreparePrefabMaterialsWithoutRuntimeClones(root);\n"
if old_call in text:
    text = text.replace(old_call, new_call, 1)
elif new_call not in text:
    raise SystemExit("Could not locate VolkswagenAmarokMaterials.FixSolidMaterials(root) in the generated setup.")

marker = "    private static int ConfigureRimFinish(GameObject root)"
helper = '''    private static VolkswagenAmarokMaterialFixResult PreparePrefabMaterialsWithoutRuntimeClones(GameObject root)\n    {\n        // Do NOT call VolkswagenAmarokMaterials.FixSolidMaterials() while authoring\n        // the prefab. That method is a runtime per-instance material-cloning path.\n        // Its Instantiate(Material) results are not persistent assets, so assigning\n        // them to renderers before SaveAsPrefabAsset() serializes the model's material\n        // references as null. Keep the manifest-generated material assets on the\n        // prefab and only ensure the runtime controller component is present.\n        if (root.GetComponent<VolkswagenAmarokMaterialController>() == null)\n            root.AddComponent<VolkswagenAmarokMaterialController>();\n\n        var rendererCount = 0;\n        var opaqueMaterials = 0;\n        var transparentMaterials = 0;\n        var uniqueMaterials = new HashSet<Material>();\n        var nullSlots = 0;\n        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))\n        {\n            rendererCount++;\n            foreach (var material in renderer.sharedMaterials)\n            {\n                if (material == null)\n                {\n                    nullSlots++;\n                    continue;\n                }\n                if (!uniqueMaterials.Add(material))\n                    continue;\n                if (VolkswagenAmarokMaterials.IsTransparentMaterial(material))\n                    transparentMaterials++;\n                else\n                    opaqueMaterials++;\n            }\n        }\n\n        if (nullSlots != 0)\n            throw new InvalidOperationException(\n                $"Amarok prefab contains {nullSlots} null material slot(s) before save; " +\n                "runtime material cloning must not be used during prefab authoring.");\n\n        Debug.Log(\n            $"VolkswagenAmarok: preserving persistent prefab materials " +\n            $"unique={uniqueMaterials.Count}, opaque={opaqueMaterials}, " +\n            $"transparent={transparentMaterials}, nullSlots={nullSlots}; " +\n            "runtime cloning deferred until vehicle initialization.");\n\n        return new VolkswagenAmarokMaterialFixResult(\n            rendererCount,\n            0,\n            opaqueMaterials,\n            transparentMaterials,\n            0,\n            0,\n            0,\n            0);\n    }\n\n'''

if "private static VolkswagenAmarokMaterialFixResult PreparePrefabMaterialsWithoutRuntimeClones" not in text:
    if marker not in text:
        raise SystemExit("Could not locate ConfigureRimFinish insertion point in VolkswagenAmarokSetup.cs.")
    text = text.replace(marker, helper + marker, 1)

# Add a second guard immediately before SaveAsPrefabAsset so any future editor\n# step that clears the persistent assignments fails before writing a broken prefab.
save_marker = "            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);\n"
guard = '''            var preSaveNullMaterialSlots = 0;\n            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))\n                foreach (var material in renderer.sharedMaterials)\n                    if (material == null) preSaveNullMaterialSlots++;\n            if (preSaveNullMaterialSlots != 0)\n                throw new InvalidOperationException(\n                    $"Amarok prefab has {preSaveNullMaterialSlots} null material slot(s) immediately before SaveAsPrefabAsset().");\n\n'''
if "preSaveNullMaterialSlots" not in text:
    if save_marker not in text:
        raise SystemExit("Could not locate SaveAsPrefabAsset call for the material-persistence guard.")
    text = text.replace(save_marker, guard + save_marker, 1)

SETUP.write_text(text, encoding="utf-8", newline="\n")

check = SETUP.read_text(encoding="utf-8")
required = [
    "PreparePrefabMaterialsWithoutRuntimeClones(root)",
    "root.AddComponent<VolkswagenAmarokMaterialController>()",
    "runtime cloning deferred until vehicle initialization",
    "preSaveNullMaterialSlots",
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit("Amarok prefab-material persistence patch failed; missing: " + ", ".join(missing))

if "VolkswagenAmarokMaterials.FixSolidMaterials(root)" in check:
    raise SystemExit("Amarok prefab-material persistence patch failed: editor-time runtime cloning call still exists.")

if CREATE.is_file():
    create = CREATE.read_text(encoding="utf-8")
    call = '    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_prefab_material_persistence.py"), run_name="__main__")\n'
    anchor = '    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_material_assignments.py"), run_name="__main__")\n'
    if call not in create:
        if anchor not in create:
            raise SystemExit("Could not locate material-assignment step in create_volkswagen_amarok.py.")
        create = create.replace(anchor, anchor + call, 1)
        CREATE.write_text(create, encoding="utf-8", newline="\n")

print("Removed editor-time runtime material cloning from Amarok prefab generation.")
print("Persistent manifest-generated material assets now remain assigned through SaveAsPrefabAsset().")
print("VolkswagenAmarokMaterialController remains on the prefab and will clone materials only at runtime.")
print("Added a hard pre-save null-material guard.")
