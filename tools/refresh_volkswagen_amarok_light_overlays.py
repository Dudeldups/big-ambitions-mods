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
temporary_output = OUTPUT.with_name(OUTPUT.stem + ".tmp.glb")
if temporary_output.exists():
    temporary_output.unlink()

subprocess.run(
    [
        blender,
        "--background",
        "--python-exit-code",
        "17",
        str(BLEND),
        "--python",
        str(EXPORTER),
        "--",
        str(temporary_output),
    ],
    check=True,
)
if not temporary_output.is_file() or temporary_output.stat().st_size <= 0:
    raise SystemExit(
        "Blender completed without producing a valid AmarokLightOverlays temporary GLB."
    )

# Only replace the known-good overlay after Blender completed successfully. This
# prevents an exception during export from being mistaken for a successful refresh.
temporary_output.replace(OUTPUT)
print(f"Refreshed Amarok authored light overlays: {OUTPUT}")
