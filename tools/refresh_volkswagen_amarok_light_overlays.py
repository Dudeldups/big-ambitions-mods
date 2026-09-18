from pathlib import Path
import os
import shutil
import subprocess

REPO = Path(__file__).resolve().parents[1]
MODELS = REPO / "Assets/Mods/Volkswagen_Amarok/Models"
BLEND = MODELS / "VolkswagenAmarokLightOverlays.blend"
OUTPUT = MODELS / "AmarokLightOverlays.glb"
EXPORTER = REPO / "tools/export_volkswagen_amarok_lights.py"

if not BLEND.is_file():
    raise SystemExit(f"Amarok Blender light source is missing: {BLEND}")
if not EXPORTER.is_file():
    raise SystemExit(f"Amarok Blender light exporter is missing: {EXPORTER}")


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


needs_export = (
    not OUTPUT.is_file()
    or OUTPUT.stat().st_mtime < BLEND.stat().st_mtime
    or OUTPUT.stat().st_mtime < EXPORTER.stat().st_mtime
)

if not needs_export:
    print("AmarokLightOverlays.glb is already newer than the Blender source/exporter.")
    raise SystemExit(0)

blender = find_blender()
if not blender:
    raise SystemExit(
        "VolkswagenAmarokLightOverlays.blend changed, but Blender was not found. "
        "Set BLENDER_PATH to blender.exe or add Blender to PATH."
    )

print(f"Refreshing Amarok light GLB from: {BLEND}")
subprocess.run(
    [
        blender,
        "--background",
        str(BLEND),
        "--python",
        str(EXPORTER),
        "--",
        str(OUTPUT),
    ],
    check=True,
)
if not OUTPUT.is_file():
    raise SystemExit("Blender completed without producing AmarokLightOverlays.glb.")

print(f"Refreshed Amarok authored light overlays: {OUTPUT}")
