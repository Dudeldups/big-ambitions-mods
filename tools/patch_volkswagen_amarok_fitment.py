from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup file was not found: {SETUP}")

BODY_Y_OFFSET = 0.136
FRONT_AXLE_Z = 1.733
REAR_AXLE_Z = -1.361
WHEEL_INSET = 0.100


def save(path: Path, text: str):
    path.write_text(text, encoding="utf-8", newline="\n")


setup = SETUP.read_text(encoding="utf-8")

# Keep the real 255/60 R18 wheel center at its physically plausible height.
# The visual correction requested in Unity is achieved by lifting the Amarok
# body instead of burying the tires below the vehicle/root ground plane.
setup, count = re.subn(
    r"private const float WheelInset = [^;]+;",
    f"private const float WheelInset = {WHEEL_INSET:.3f}f;",
    setup,
    count=1,
)
if count != 1:
    raise SystemExit("Could not set Amarok WheelInset in VolkswagenAmarokSetup.cs.")

# The measured arch centers preserve essentially the real wheelbase (3.094 m vs
# the 3.097 m spec) but shift the complete axle pair about 18.6 cm forward.
wheel_positions_pattern = re.compile(
    r"private static readonly Dictionary<string, Vector3> WheelControllerPositions =\s*"
    r"new Dictionary<string, Vector3>\s*\{.*?\n\s*\};",
    re.S,
)
wheel_positions = f'''private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {{
            {{ "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, {FRONT_AXLE_Z:.3f}f) }},
            {{ "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, {FRONT_AXLE_Z:.3f}f) }},
            {{ "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, {REAR_AXLE_Z:.3f}f) }},
            {{ "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, {REAR_AXLE_Z:.3f}f) }},
        }};'''
setup, count = wheel_positions_pattern.subn(wheel_positions, setup, count=1)
if count != 1:
    raise SystemExit("Could not replace Amarok WheelControllerPositions.")

# NormalizeModel() puts the supplied GLB on the root ground plane. The wheel-arch
# inspection showed the body 13.6 cm too low relative to correctly sized tires.
# Lift the full visual before wheels are detached. AttachLightOverlaySources copies
# this transform and CreateDeformableBody bakes it, so lights and damage geometry
# stay aligned automatically.
body_offset_line = f"            modelInstance.transform.localPosition += new Vector3(0f, {BODY_Y_OFFSET:.3f}f, 0f); // Amarok body-to-wheel fitment"
if "// Amarok body-to-wheel fitment" not in setup:
    normalize_call = "            NormalizeModel(modelInstance);"
    if normalize_call not in setup:
        raise SystemExit("Could not locate NormalizeModel(modelInstance) in VolkswagenAmarokSetup.cs.")
    setup = setup.replace(normalize_call, normalize_call + "\n" + body_offset_line, 1)
else:
    setup = re.sub(
        r"\s*modelInstance\.transform\.localPosition \+= new Vector3\(0f, [^,]+f, 0f\); // Amarok body-to-wheel fitment",
        "\n" + body_offset_line,
        setup,
        count=1,
    )

save(SETUP, setup)

# Runtime currently does not own the visual mounts, but if a generated runtime
# variant contains the same fitment constants, keep them from restoring an old
# wider wheel position after vehicle initialization.
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
    f"{FRONT_AXLE_Z:.3f}f",
    f"{REAR_AXLE_Z:.3f}f",
    f"new Vector3(0f, {BODY_Y_OFFSET:.3f}f, 0f); // Amarok body-to-wheel fitment",
]
missing = [needle for needle in required if needle not in check]
if missing:
    raise SystemExit("Amarok fitment patch failed; missing: " + ", ".join(missing))

front_x = 1.654 * 0.5 - WHEEL_INSET
rear_x = 1.658 * 0.5 - WHEEL_INSET
print(f"Patched Amarok body height: visual/light/damage geometry +{BODY_Y_OFFSET:.3f} m; wheel Y remains 0.442 m.")
print(f"Patched axle Z: front={FRONT_AXLE_Z:.3f}, rear={REAR_AXLE_Z:.3f} (visual wheelbase={FRONT_AXLE_Z - REAR_AXLE_Z:.3f} m).")
print(f"Patched wheel inset: 0.100 m per side (front X=±{front_x:.3f}, rear X=±{rear_x:.3f}).")
print("Volkswagen Amarok fitment preflight passed.")
