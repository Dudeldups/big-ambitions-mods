from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"

for path in (SETUP, RUNTIME):
    if not path.is_file():
        raise SystemExit(f"Required generated Amarok source is missing: {path}")


def set_constant(text: str, name: str, value: str) -> str:
    updated, count = re.subn(
        rf"private const float {re.escape(name)} = [^;]+;",
        f"private const float {name} = {value};",
        text,
        count=1,
    )
    if count != 1:
        raise SystemExit(f"Could not set Amarok {name}.")
    return updated


# Real 2017 3.0 V6 TDI 224 PS performance targets used by this calibration:
#   0-100 km/h: ~8.0 s
#   100-0 km/h: 36.7-37.0 m
#
# The current engine side is already physically modeled around 165 kW / 550 Nm,
# 2078 kg, real eight-speed ratios and 3.70 final drive. Do not fake a faster
# truck by increasing engine power. The old 0.84/0.86 longitudinal tire grip and
# 0.96 friction circle, however, cap braking below the ~1.06 g average needed for
# a 37 m 100-0 stop. Raise tire/brake authority while keeping engine output real.
setup = SETUP.read_text(encoding="utf-8")
setup = set_constant(setup, "VehicleBrakeForce", "2800f")
setup = set_constant(setup, "BrakeMaxTorque", "2800f")
setup = set_constant(setup, "FrontForwardGrip", "1.08f")
setup = set_constant(setup, "RearForwardGrip", "1.05f")
setup = set_constant(setup, "FrontForwardStiffness", "1.12f")
setup = set_constant(setup, "RearForwardStiffness", "1.10f")
setup = set_constant(setup, "TireFrictionCircleStrength", "1.08f")

# Existing VehicleType asset also needs the new brake force when we are patching
# the already-generated prefab instead of running the full donor regeneration.
feedback_marker = '        SetNumber(feedbackVehicleSerialized, "damageIntensity", 0.31f);\n'
feedback_brake = feedback_marker + '        SetNumber(feedbackVehicleSerialized, "brakeForce", VehicleBrakeForce);\n'
if 'SetNumber(feedbackVehicleSerialized, "brakeForce", VehicleBrakeForce);' not in setup:
    if feedback_marker not in setup:
        raise SystemExit("Could not locate Amarok feedback VehicleType performance block.")
    setup = setup.replace(feedback_marker, feedback_brake, 1)

SETUP.write_text(setup, encoding="utf-8", newline="\n")


runtime = RUNTIME.read_text(encoding="utf-8")
runtime = set_constant(runtime, "FrontForwardGrip", "1.08f")
runtime = set_constant(runtime, "RearForwardGrip", "1.05f")
runtime = set_constant(runtime, "FrontForwardStiffness", "1.12f")
runtime = set_constant(runtime, "RearForwardStiffness", "1.10f")
runtime = set_constant(runtime, "TireFrictionCircleStrength", "1.08f")
RUNTIME.write_text(runtime, encoding="utf-8", newline="\n")


checks = {
    SETUP: [
        "private const float VehicleBrakeForce = 2800f;",
        "private const float BrakeMaxTorque = 2800f;",
        "private const float FrontForwardGrip = 1.08f;",
        "private const float RearForwardGrip = 1.05f;",
        "private const float TireFrictionCircleStrength = 1.08f;",
        'SetNumber(feedbackVehicleSerialized, "brakeForce", VehicleBrakeForce);',
        'SetRelativeNumber(serialized, "powertrain.engine.maxPower", 165f);',
        "body.mass = 2078f;",
    ],
    RUNTIME: [
        "private const float EnginePowerKw = 165f;",
        "private const float VehicleMass = 2078f;",
        "private const float FrontForwardGrip = 1.08f;",
        "private const float RearForwardGrip = 1.05f;",
        "private const float TireFrictionCircleStrength = 1.08f;",
    ],
}
missing = []
for path, needles in checks.items():
    current = path.read_text(encoding="utf-8")
    for needle in needles:
        if needle not in current:
            missing.append(f"{path.name}: {needle}")
if missing:
    raise SystemExit("Amarok seventh performance patch failed:\n- " + "\n- ".join(missing))

print("Kept Amarok acceleration physics at the real 165 kW / 550 Nm / 2078 kg drivetrain model.")
print("Raised longitudinal tire/friction-circle authority to target the measured ~8.0 s 0-100 and 36.7-37.0 m 100-0 envelope.")
print("Raised brake torque/VehicleType brake force modestly from 2600 to 2800 while retaining road-tire-limited braking.")
print("Volkswagen Amarok seventh performance preflight passed.")
