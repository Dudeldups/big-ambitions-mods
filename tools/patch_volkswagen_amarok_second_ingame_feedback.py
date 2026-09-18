from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
DRIVER = MOD / "Scripts/VolkswagenAmarokDriverController.cs"

for path in (SETUP, RUNTIME, DRIVER):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def save(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------------------
# Second in-game feedback pass.
# - raise the body 3 cm from the previous -5.5 cm visual drop
# - restore the real wheel track (remove the accidental 10 cm inset per side)
# - lower/rear-shift/recline the seated player
# - auto-orient the separately exported Blender light-source GLB
# - make deformation slightly stronger
# ---------------------------------------------------------------------------
setup = SETUP.read_text(encoding="utf-8")

setup, body_height_count = re.subn(
    r"private const float BodyVisualBottomY = [^;]+;",
    "private const float BodyVisualBottomY = -0.025f;",
    setup,
    count=1,
)
if body_height_count != 1:
    raise SystemExit("Could not update Amarok BodyVisualBottomY.")

setup, setup_deformation_count = re.subn(
    r"private const float DeformationStrength = [^;]+;",
    "private const float DeformationStrength = 0.14f;",
    setup,
    count=1,
)
if setup_deformation_count != 1:
    raise SystemExit("Could not update Amarok setup deformation strength.")

# The 0.100 m inset was a temporary fitment experiment and visibly pulled the
# wheels into the body. The official front/rear track values are wheel-center
# track widths already, so the controller centers belong at +/- track/2.
setup, wheel_inset_count = re.subn(
    r"private const float WheelInset = [^;]+;",
    "private const float WheelInset = 0f;",
    setup,
    count=1,
)
if wheel_inset_count != 1:
    raise SystemExit("Could not restore Amarok WheelInset to zero.")

feedback_marker = "// Amarok second-feedback wheel + authored-light alignment."
if feedback_marker not in setup:
    old_light_block = '''            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.localPosition = visual.localPosition;
                lightSources.localRotation = visual.localRotation;
                lightSources.localScale = visual.localScale;
            }
'''
    if old_light_block not in setup:
        raise SystemExit("Could not locate existing-prefab Amarok light-source alignment block.")

    new_light_and_wheel_block = '''            // Amarok second-feedback wheel + authored-light alignment.
            // Restore the real track width on both the physics controllers and the
            // root-level parked/NPC wheel visuals. This also keeps parked traffic
            // from using the stale inward-shifted wheel transforms.
            var frontLeftWheel = new Vector3(-FrontTrack * 0.5f, 0.306f, 1.733f);
            var frontRightWheel = new Vector3(FrontTrack * 0.5f, 0.306f, 1.733f);
            var rearLeftWheel = new Vector3(-RearTrack * 0.5f, 0.306f, -1.361f);
            var rearRightWheel = new Vector3(RearTrack * 0.5f, 0.306f, -1.361f);
            SetLocalPosition(root, "FrontLeft_WheelController", frontLeftWheel);
            SetLocalPosition(root, "FrontRight_WheelController", frontRightWheel);
            SetLocalPosition(root, "RearLeft_WheelController", rearLeftWheel);
            SetLocalPosition(root, "RearRight_WheelController", rearRightWheel);

            var frontLeftVisual = FindTransform(root.transform, "AmarokWheelFrontLeft");
            var frontRightVisual = FindTransform(root.transform, "AmarokWheelFrontRight");
            var rearLeftVisual = FindTransform(root.transform, "AmarokWheelRearLeft");
            var rearRightVisual = FindTransform(root.transform, "AmarokWheelRearRight");
            if (frontLeftVisual != null) frontLeftVisual.localPosition = frontLeftWheel;
            if (frontRightVisual != null) frontRightVisual.localPosition = frontRightWheel;
            if (rearLeftVisual != null) rearLeftVisual.localPosition = rearLeftWheel;
            if (rearRightVisual != null) rearRightVisual.localPosition = rearRightWheel;

            var frontLeftCaliper = FindTransform(root.transform, "AmarokFixedCaliperFrontLeft");
            var frontRightCaliper = FindTransform(root.transform, "AmarokFixedCaliperFrontRight");
            var rearLeftCaliper = FindTransform(root.transform, "AmarokFixedCaliperRearLeft");
            var rearRightCaliper = FindTransform(root.transform, "AmarokFixedCaliperRearRight");
            if (frontLeftCaliper != null) frontLeftCaliper.localPosition = frontLeftWheel;
            if (frontRightCaliper != null) frontRightCaliper.localPosition = frontRightWheel;
            if (rearLeftCaliper != null) rearLeftCaliper.localPosition = rearLeftWheel;
            if (rearRightCaliper != null) rearRightCaliper.localPosition = rearRightWheel;

            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.localPosition = visual.localPosition;
                lightSources.localScale = visual.localScale;

                // AmarokLightOverlays.glb was exported separately with Blender's
                // glTF axis conversion already applied. Copying the visual root's
                // +/-90 degree correction a second time stood the entire light set
                // on end. Try the valid axis/yaw candidates and choose the one whose
                // lamp span follows vehicle Z instead of world Y, with headlights
                // in front of the rear running lights.
                var rotations = new[]
                {
                    Quaternion.identity,
                    Quaternion.Euler(90f, 0f, 0f),
                    Quaternion.Euler(-90f, 0f, 0f),
                    Quaternion.Euler(0f, 180f, 0f),
                    Quaternion.Euler(90f, 180f, 0f),
                    Quaternion.Euler(-90f, 180f, 0f),
                };
                var bestRotation = Quaternion.identity;
                var bestScore = float.NegativeInfinity;
                var headSource = FindTransformWithNameFragment(lightSources, "BHeadlights");
                var rearSource = FindTransformWithNameFragment(lightSources, "1RearDrivingLights");

                foreach (var rotation in rotations)
                {
                    lightSources.localRotation = rotation;
                    if (!TryGetRendererBounds(lightSources, out var lightBounds))
                        continue;

                    var score = lightBounds.size.z * 4f - lightBounds.size.y * 6f - lightBounds.size.x;
                    if (headSource != null && rearSource != null &&
                        TryGetRendererBounds(headSource, out var headBounds) &&
                        TryGetRendererBounds(rearSource, out var rearBounds))
                    {
                        score += headBounds.center.z > rearBounds.center.z ? 50f : -50f;
                    }

                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    bestRotation = rotation;
                }

                lightSources.localRotation = bestRotation;
                Debug.Log(
                    $"VolkswagenAmarok feedback light alignment: rotation={lightSources.localEulerAngles}, " +
                    $"score={bestScore:F2}.");
            }
'''
    setup = setup.replace(old_light_block, new_light_and_wheel_block, 1)

save(SETUP, setup)


runtime = RUNTIME.read_text(encoding="utf-8")
runtime, runtime_deformation_count = re.subn(
    r"private const float DeformationStrength = [^;]+;",
    "private const float DeformationStrength = 0.14f;",
    runtime,
    count=1,
)
if runtime_deformation_count != 1:
    raise SystemExit("Could not update Amarok runtime deformation strength.")

if re.search(r"private const float WheelInset = [^;]+;", runtime):
    runtime = re.sub(
        r"private const float WheelInset = [^;]+;",
        "private const float WheelInset = 0f;",
        runtime,
        count=1,
    )
save(RUNTIME, runtime)


driver = DRIVER.read_text(encoding="utf-8")
driver, seat_offset_count = re.subn(
    r"private static readonly Vector3 SeatOffset = new\([^;]+;",
    "private static readonly Vector3 SeatOffset = new(0f, -0.13f, -0.45f);",
    driver,
    count=1,
)
if seat_offset_count != 1:
    raise SystemExit("Could not update Amarok seated-player offset.")

driver, lean_count = re.subn(
    r"private const float SeatBackLeanDegrees = [^;]+;",
    "private const float SeatBackLeanDegrees = 15f;",
    driver,
    count=1,
)
if lean_count != 1:
    raise SystemExit("Could not update Amarok seated-player back lean.")
save(DRIVER, driver)


checks = {
    SETUP: [
        "private const float BodyVisualBottomY = -0.025f;",
        "private const float WheelInset = 0f;",
        "private const float DeformationStrength = 0.14f;",
        feedback_marker,
        'SetLocalPosition(root, "FrontLeft_WheelController", frontLeftWheel);',
        'FindTransform(root.transform, "AmarokWheelFrontLeft")',
    ],
    RUNTIME: [
        "private const float DeformationStrength = 0.14f;",
    ],
    DRIVER: [
        "private static readonly Vector3 SeatOffset = new(0f, -0.13f, -0.45f);",
        "private const float SeatBackLeanDegrees = 15f;",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")

# Later feedback passes deliberately replace the second pass's original
# rotation-search block. Accept either the legacy auto-orientation implementation
# or the newer parent/local-space light alignment. This keeps repeated builds
# idempotent instead of treating a newer fix as a regression.
setup_check = SETUP.read_text(encoding="utf-8")
legacy_light_alignment = (
    "var rotations = new[]" in setup_check
    and "headBounds.center.z > rearBounds.center.z" in setup_check
)
newer_light_alignment = (
    "lightSources.SetParent(visual, false);" in setup_check
    or "AmarokLightSources" in setup_check
       and "lightSources.localRotation" in setup_check
)
if not (legacy_light_alignment or newer_light_alignment):
    missing.append(
        "VolkswagenAmarokSetup.cs: authored-light alignment strategy "
        "(legacy rotation search or newer local-space alignment)"
    )

if missing:
    raise SystemExit("Amarok second in-game feedback patch failed:\n- " + "\n- ".join(missing))

print("Raised Amarok body by 3.0 cm from the previous feedback position.")
print("Restored wheel centers to the full 1.654/1.658 m track with zero artificial inset.")
print("Updated parked/NPC wheel visual mounts together with the wheel controllers.")
print("Lowered the seated player by 3 cm, moved them 3 cm rearward and added 3 degrees of recline.")
print("Added automatic authored-light axis/yaw alignment to correct the 90-degree rotation.")
print("Raised deformation strength slightly from 0.12 to 0.14.")
print("Volkswagen Amarok second in-game feedback patch preflight passed.")
