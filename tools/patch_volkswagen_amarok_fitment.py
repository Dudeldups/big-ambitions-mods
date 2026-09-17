from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup file was not found: {SETUP}")

WHEEL_Y = 0.306
FRONT_AXLE_Z = 1.733
REAR_AXLE_Z = -1.361
WHEEL_INSET = 0.000


def save(path: Path, text: str):
    path.write_text(text, encoding="utf-8", newline="\n")


setup = SETUP.read_text(encoding="utf-8")

# The previous fitment attempt lifted the whole visual by 13.6 cm. That exposed
# independent/generated visual roots at different heights. Keep the complete
# body at the normalized GLB position and instead use the wheel centers measured
# directly against the Amarok arches in Unity.
setup = re.sub(
    r"\n\s*modelInstance\.transform\.localPosition \+= new Vector3\(0f, [^,]+f, 0f\); // Amarok body-to-wheel fitment",
    "",
    setup,
)

setup, count = re.subn(
    r"private const float WheelInset = [^;]+;",
    f"private const float WheelInset = {WHEEL_INSET:.3f}f;",
    setup,
    count=1,
)
if count != 1:
    raise SystemExit("Could not set Amarok WheelInset in VolkswagenAmarokSetup.cs.")

# These are the measured wheel-arch centers from the generated Amarok prefab.
# Front/rear Z preserve a 3.094 m visual wheelbase, effectively matching the
# 3.097 m real specification while shifting the axle pair forward as observed.
# The official track values already describe wheel-center to wheel-center width,
# so no additional inward inset belongs in the final wheel positions.
wheel_positions_pattern = re.compile(
    r"private static readonly Dictionary<string, Vector3> WheelControllerPositions =\s*"
    r"new Dictionary<string, Vector3>\s*\{.*?\n\s*\};",
    re.S,
)
wheel_positions = f'''private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {{
            {{ "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, {WHEEL_Y:.3f}f, {FRONT_AXLE_Z:.3f}f) }},
            {{ "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, {WHEEL_Y:.3f}f, {FRONT_AXLE_Z:.3f}f) }},
            {{ "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, {WHEEL_Y:.3f}f, {REAR_AXLE_Z:.3f}f) }},
            {{ "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, {WHEEL_Y:.3f}f, {REAR_AXLE_Z:.3f}f) }},
        }};'''
setup, count = wheel_positions_pattern.subn(wheel_positions, setup, count=1)
if count != 1:
    raise SystemExit("Could not replace Amarok WheelControllerPositions.")

save(SETUP, setup)

# Runtime does not normally rewrite mount transforms, but keep its inset constant
# synchronized if a generated variant contains one.
if RUNTIME.is_file():
    runtime = RUNTIME.read_text(encoding="utf-8")
    if re.search(r"private const float WheelInset = [^;]+;", runtime):
        runtime = re.sub(
            r"private const float WheelInset = [^;]+;",
            f"private const float WheelInset = {WHEEL_INSET:.3f}f;",
            runtime,
            count=1,
        )
    save(RUNTIME, runtime)

check = SETUP.read_text(encoding="utf-8")
required = [
    f"private const float WheelInset = {WHEEL_INSET:.3f}f;",
    f"{WHEEL_Y:.3f}f, {FRONT_AXLE_Z:.3f}f",
    f"{WHEEL_Y:.3f}f, {REAR_AXLE_Z:.3f}f",
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit("Amarok fitment patch failed; missing: " + ", ".join(missing))
if "// Amarok body-to-wheel fitment" in check:
    raise SystemExit("Amarok fitment patch failed: old body-lift line is still present.")

front_x = 1.654 * 0.5 - WHEEL_INSET
rear_x = 1.658 * 0.5 - WHEEL_INSET
print("Removed the previous +0.136 m Amarok body lift; the complete body stays at its normalized GLB position.")
print(f"Patched wheel Y to {WHEEL_Y:.3f} m for all four wheels.")
print(f"Patched axle Z: front={FRONT_AXLE_Z:.3f}, rear={REAR_AXLE_Z:.3f} (visual wheelbase={FRONT_AXLE_Z - REAR_AXLE_Z:.3f} m).")
print(f"Patched wheel inset: 0.000 m per side (front X=±{front_x:.3f}, rear X=±{rear_x:.3f}).")
print("Volkswagen Amarok fitment preflight passed.")
