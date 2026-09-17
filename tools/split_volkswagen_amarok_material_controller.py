from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SCRIPTS = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts"
MATERIALS = SCRIPTS / "VolkswagenAmarokMaterials.cs"
CONTROLLER = SCRIPTS / "VolkswagenAmarokMaterialController.cs"

if not MATERIALS.is_file():
    raise SystemExit(f"Generated Amarok materials file was not found: {MATERIALS}")

text = MATERIALS.read_text(encoding="utf-8")
class_marker = '[AddComponentMenu("")]\npublic sealed class VolkswagenAmarokMaterialController : MonoBehaviour\n'
next_marker = '[AddComponentMenu("")]\npublic sealed class VolkswagenAmarokPaintController : MonoBehaviour\n'

start = text.find(class_marker)
if start < 0:
    if CONTROLLER.is_file() and "class VolkswagenAmarokMaterialController" in CONTROLLER.read_text(encoding="utf-8"):
        print("VolkswagenAmarokMaterialController is already in its own Unity script file.")
        raise SystemExit(0)
    raise SystemExit("Could not find VolkswagenAmarokMaterialController in VolkswagenAmarokMaterials.cs.")

end = text.find(next_marker, start)
if end < 0:
    raise SystemExit("Could not find VolkswagenAmarokPaintController after the material controller.")

controller_block = text[start:end].rstrip() + "\n"
materials_text = (text[:start].rstrip() + "\n\n" + text[end:]).replace("\r\n", "\n")

controller_text = '''#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

''' + controller_block

MATERIALS.write_text(materials_text, encoding="utf-8", newline="\n")
CONTROLLER.write_text(controller_text, encoding="utf-8", newline="\n")

# A MonoBehaviour serialized into a prefab must have a resolvable MonoScript.
# Keeping this controller in a multi-class utility file allowed AddComponent<T>()
# to work in memory but produced a Missing Script after SaveAsPrefabAsset().
check_materials = MATERIALS.read_text(encoding="utf-8")
check_controller = CONTROLLER.read_text(encoding="utf-8")
if "class VolkswagenAmarokMaterialController" in check_materials:
    raise SystemExit("Material-controller split failed: class still exists in VolkswagenAmarokMaterials.cs.")
if "public sealed class VolkswagenAmarokMaterialController : MonoBehaviour" not in check_controller:
    raise SystemExit("Material-controller split failed: dedicated script does not contain the controller class.")
if CONTROLLER.stem != "VolkswagenAmarokMaterialController":
    raise SystemExit("Material-controller split failed: Unity script filename does not match the MonoBehaviour class.")

print("Moved VolkswagenAmarokMaterialController into Scripts/VolkswagenAmarokMaterialController.cs.")
print("Removed the serializable MonoBehaviour from the multi-class VolkswagenAmarokMaterials.cs file.")
print("Unity can now assign a stable MonoScript reference when the prefab is saved.")
