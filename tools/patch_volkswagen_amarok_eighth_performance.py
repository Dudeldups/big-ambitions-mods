from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"

for path in (SETUP, RUNTIME):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


# The current low/mid diesel curve already models ~550 Nm correctly:
# 1395 rpm -> 0.49 * 165 kW ~= 553 Nm
# 1980 rpm -> 0.70 * 165 kW ~= 557 Nm
# 2745 rpm -> 0.96 * 165 kW ~= 551 Nm
#
# The problem is the old donor-derived tail: after the 165 kW peak around
# 3000 rpm it fell to 70% by 4500 rpm, so higher gears felt almost powerless.
# Keep the real 165 kW ceiling and flatten the power plateau through the usable
# upper rev range instead of adding fake engine power.
curve = '''private static AnimationCurve CreateGT3RSPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.16f, 0.18f),
            new Keyframe(0.31f, 0.49f), new Keyframe(0.44f, 0.70f),
            new Keyframe(0.61f, 0.96f), new Keyframe(0.67f, 1.00f),
            new Keyframe(0.78f, 1.00f), new Keyframe(0.89f, 1.00f),
            new Keyframe(1.00f, 0.99f));'''

pattern = re.compile(
    r'''private static AnimationCurve CreateGT3RSPowerCurve\(\) =>\s*
        new AnimationCurve\(.*?\);''',
    re.S | re.X,
)

for path in (SETUP, RUNTIME):
    text = path.read_text(encoding="utf-8")
    text, count = pattern.subn(curve, text, count=1)
    if count != 1:
        raise SystemExit(f"Could not flatten Amarok upper-rpm power curve in {path.name}.")

    # Rigidbody.drag is linear velocity damping, not a physical Cd coefficient.
    # 0.045 on a 2078 kg truck consumes implausibly large power as speed rises and
    # is the main reason the current build runs out of acceleration at the top end.
    # 0.020 keeps the pickup visibly less slippery than the sports-car mods while
    # allowing the real 165 kW drivetrain to pull toward the 193 km/h limiter.
    text, drag_count = re.subn(
        r"private const float VehicleLinearDrag = [^;]+;",
        "private const float VehicleLinearDrag = 0.020f;",
        text,
        count=1,
    )
    if drag_count != 1:
        raise SystemExit(f"Could not set Amarok linear drag in {path.name}.")

    path.write_text(text, encoding="utf-8", newline="\n")

checks = {
    SETUP: [
        "new Keyframe(0.67f, 1.00f)",
        "new Keyframe(0.78f, 1.00f)",
        "new Keyframe(0.89f, 1.00f)",
        "new Keyframe(1.00f, 0.99f)",
        "private const float VehicleLinearDrag = 0.020f;",
    ],
    RUNTIME: [
        "new Keyframe(0.67f, 1.00f)",
        "new Keyframe(0.78f, 1.00f)",
        "new Keyframe(0.89f, 1.00f)",
        "new Keyframe(1.00f, 0.99f)",
        "private const float EnginePowerKw = 165f;",
        "private const float EngineLimitRpm = 4500f;",
        "private const float VehicleLinearDrag = 0.020f;",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok eighth performance patch failed:\n- " + "\n- ".join(missing))

print("Kept the Amarok at 165 kW / ~550 Nm instead of increasing nominal engine output.")
print("Removed the unrealistic upper-rpm power collapse: 3000-4500 rpm now stays at ~99-100% peak power.")
print("Reduced Rigidbody linear drag from 0.045 to 0.020 so the 2078 kg truck can still accelerate realistically at motorway speeds.")
print("Volkswagen Amarok eighth performance preflight passed.")
