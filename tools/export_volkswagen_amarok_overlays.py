from pathlib import Path
import os
import shutil
import subprocess

REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "tools"
MODELS = REPO / "Assets/Mods/Volkswagen_Amarok/Models"
BLEND = MODELS / "VolkswagenAmarokLightOverlays.blend"
OUTPUT = MODELS / "AmarokLightOverlays.glb"
EXPORTER = TOOLS / "export_volkswagen_amarok_lights.py"


def find_blender():
    configured = os.environ.get("BLENDER_PATH")
    if configured and Path(configured).is_file():
        return Path(configured)
    found = shutil.which("blender")
    if found:
        return Path(found)
    root = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Blender Foundation"
    candidates = sorted(root.glob("Blender */blender.exe")) if root.exists() else []
    return candidates[-1] if candidates else None


if not BLEND.is_file():
    raise SystemExit(f"Missing Blender source: {BLEND}")
if not EXPORTER.is_file():
    raise SystemExit(f"Missing exporter script: {EXPORTER}")

blender = find_blender()
if blender is None:
    raise SystemExit(
        "Blender was not found. Set BLENDER_PATH to blender.exe and run this script again."
    )

print(f"Blender: {blender}")
print(f"Source:  {BLEND}")
print(f"Output:  {OUTPUT}")

# Remove stale exports so a successful return cannot accidentally be confused
# with an old file from an earlier run.
if OUTPUT.exists():
    OUTPUT.unlink()
extra_extension = Path(str(OUTPUT) + ".glb")
if extra_extension.exists():
    extra_extension.unlink()

subprocess.run(
    [
        str(blender),
        "--background",
        str(BLEND),
        "--python",
        str(EXPORTER),
        "--",
        str(OUTPUT),
    ],
    check=True,
)

# Some Blender exporter versions can append the extension even when the path
# already contains it. Normalize that case back to the path Unity expects.
if not OUTPUT.exists() and extra_extension.exists():
    extra_extension.replace(OUTPUT)

if not OUTPUT.is_file() or OUTPUT.stat().st_size == 0:
    raise SystemExit(
        "Blender returned successfully but AmarokLightOverlays.glb was not created at the expected path."
    )

print(f"OK: created {OUTPUT}")
print(f"Size: {OUTPUT.stat().st_size:,} bytes")
