from pathlib import Path
import os
import runpy
import shutil
import subprocess
import tempfile

REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "tools"
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
MODELS = MOD / "Models"

SOURCE_ASSET_NAMES = (
    "2017_volkswagen_amarok_v6.glb",
    "VolkswagenAmarokLightOverlays.blend",
    "AmarokLightOverlays.glb",
)

# generate_volkswagen_amarok.py deliberately recreates the mod folder from the
# current Porsche architecture. Preserve user-supplied binary source assets first
# so repeated/idempotent setup runs do not destroy the GLB or Blender file.
with tempfile.TemporaryDirectory(prefix="volkswagen_amarok_setup_") as temp_dir:
    temp = Path(temp_dir)
    preserved = []
    for name in SOURCE_ASSET_NAMES:
        src = MODELS / name
        if src.is_file():
            shutil.copy2(src, temp / name)
            preserved.append(name)

    runpy.run_path(str(TOOLS / "generate_volkswagen_amarok.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "finalize_volkswagen_amarok.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_setup.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "patch_volkswagen_amarok_side_indicators.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "patch_volkswagen_amarok_prefab_cleanup.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "split_volkswagen_amarok_material_controller.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "patch_volkswagen_amarok_visual_and_prefab_values.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "patch_volkswagen_amarok_nav_and_materials.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_duplicate_normalizer.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_material_assignments.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_prefab_material_persistence.py"), run_name="__main__")
    runpy.run_path(str(TOOLS / "patch_volkswagen_amarok_fitment.py"), run_name="__main__")

    MODELS.mkdir(parents=True, exist_ok=True)
    for name in preserved:
        shutil.copy2(temp / name, MODELS / name)

model = MODELS / "2017_volkswagen_amarok_v6.glb"
blend = MODELS / "VolkswagenAmarokLightOverlays.blend"
overlays = MODELS / "AmarokLightOverlays.glb"
missing = [str(path) for path in (model, blend) if not path.exists()]
if missing:
    raise SystemExit(
        "Volkswagen Amarok source was generated, but the supplied binary source assets still need "
        "to be copied into Assets/Mods/Volkswagen_Amarok/Models:\n- " + "\n- ".join(missing)
    )

def find_blender():
    configured = os.environ.get("BLENDER_PATH")
    if configured and Path(configured).is_file():
        return configured
    found = shutil.which("blender")
    if found:
        return found
    root = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Blender Foundation"
    candidates = sorted(root.glob("Blender */blender.exe")) if root.exists() else []
    return str(candidates[-1]) if candidates else None

blender = find_blender()
if not blender:
    raise SystemExit(
        "Amarok source and model are ready, but Blender was not found. Set BLENDER_PATH to blender.exe "
        "and run this script again so AmarokLightOverlays.glb can be generated."
    )

needs_export = (
    not overlays.exists()
    or overlays.stat().st_mtime < blend.stat().st_mtime
    or overlays.stat().st_mtime < (TOOLS / "export_volkswagen_amarok_lights.py").stat().st_mtime
)
if needs_export:
    subprocess.run([
        blender,
        "--background", str(blend),
        "--python", str(TOOLS / "export_volkswagen_amarok_lights.py"),
        "--", str(overlays),
    ], check=True)

print("Volkswagen Amarok source is ready for Unity.")
print("In Unity run: Big Ambitions Mods > Setup Volkswagen Amarok")
