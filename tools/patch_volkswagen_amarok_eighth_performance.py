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
# The generated Amarok source has existed under both donor-era
# CreateGT3RSPowerCurve() and sanitized CreateAmarokPowerCurve() names. Match
# either name and preserve whichever one the current worktree contains.
curve_decl_pattern = re.compile(
    r"private static AnimationCurve (?P<name>Create(?:GT3RS|Amarok)PowerCurve)\(\)"
)


def replace_power_curve(text: str, source_name: str) -> str:
    match = curve_decl_pattern.search(text)
    if match is None:
        candidates = sorted(set(re.findall(
            r"private static AnimationCurve\s+([A-Za-z0-9_]+PowerCurve)\s*\(",
            text,
        )))
        raise SystemExit(
            f"Could not locate Amarok power-curve declaration in {source_name}; "
            f"found candidates={candidates or ['<none>']}."
        )

    arrow = text.find("=>", match.end())
    curve_ctor = text.find("new AnimationCurve(", arrow + 2 if arrow >= 0 else match.end())
    if arrow < 0 or curve_ctor < 0:
        raise SystemExit(
            f"Could not locate expression-bodied Amarok power curve in {source_name}."
        )

    open_paren = text.find("(", curve_ctor)
    if open_paren < 0:
        raise SystemExit(f"Amarok power curve opening parenthesis is missing in {source_name}.")

    depth = 0
    close_paren = -1
    for index in range(open_paren, len(text)):
        char = text[index]
        if char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                close_paren = index
                break
    if close_paren < 0:
        raise SystemExit(f"Amarok power curve parentheses are unbalanced in {source_name}.")

    semicolon = text.find(";", close_paren)
    if semicolon < 0:
        raise SystemExit(f"Amarok power curve terminator is missing in {source_name}.")

    method_name = match.group("name")
    replacement = f"""private static AnimationCurve {method_name}() =>
        new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.16f, 0.18f),
            new Keyframe(0.31f, 0.49f), new Keyframe(0.44f, 0.70f),
            new Keyframe(0.61f, 0.96f), new Keyframe(0.67f, 1.00f),
            new Keyframe(0.78f, 1.00f), new Keyframe(0.89f, 1.00f),
            new Keyframe(1.00f, 0.99f));"""
    return text[:match.start()] + replacement + text[semicolon + 1:]


def restore_runtime_instance_fields(text: str) -> str:
    # A previous version of this patch used the next static member as the end of
    # the power-curve method. In VolkswagenAmarokRuntime that deleted every
    # instance field between the curve and public static Initialize(). Repair that
    # exact damage once, while remaining a no-op for healthy generated sources.
    if (
        "private readonly HashSet<int> configuredVehicleIds" in text
        and "private ModContext? context;" in text
        and "private GameObject? playerVehiclePrefab;" in text
    ):
        return text

    initialize_marker = "    public static VolkswagenAmarokRuntime Initialize("
    initialize_at = text.find(initialize_marker)
    if initialize_at < 0:
        raise SystemExit(
            "Could not locate VolkswagenAmarokRuntime.Initialize() while restoring instance fields."
        )

    field_block = """    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private VolkswagenAmarokWarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private GameObject? playerVehiclePrefab;

"""

    # Remove any surviving subset of the damaged block first so the repair cannot
    # create duplicate declarations on partially damaged worktrees.
    field_names = [
        "configuredVehicleIds",
        "initializationCoroutine",
        "enteredVehicleActivationCoroutine",
        "enteredVehicleActivationInstanceId",
        "exitedPlayerRecoveryCoroutine",
        "warehouseExitGuardCoroutine",
        "warehouseExitGuardColliders",
        "warehouseExitGuardEntryController",
        "context",
        "vehicleTypeName",
        "cachedPlayerVehicleCount",
        "dealerRegistrationReady",
        "dealerReadyLogged",
        "privateDriverPoolReady",
        "privateDriverReady",
        "privateDriverRegistrationAllowed",
        "privateDriverPreparationExceptionLogged",
        "playerVehiclePrefab",
    ]
    lines = text[:initialize_at].splitlines(keepends=True)
    kept = []
    for line in lines:
        if any(re.search(rf"\b{re.escape(name)}\b", line) for name in field_names):
            # Only remove declaration lines before Initialize; method bodies begin
            # after Initialize and are therefore unaffected.
            if re.match(r"\s*private\s+", line):
                continue
        kept.append(line)

    prefix = "".join(kept)
    suffix = text[initialize_at:]
    if prefix and not prefix.endswith("\n\n"):
        prefix = prefix.rstrip() + "\n\n"
    return prefix + field_block + suffix


for path in (SETUP, RUNTIME):
    text = path.read_text(encoding="utf-8")
    text = replace_power_curve(text, path.name)
    if path == RUNTIME:
        text = restore_runtime_instance_fields(text)

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
        "private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();",
        "private Coroutine? initializationCoroutine;",
        "private Coroutine? warehouseExitGuardCoroutine;",
        "private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();",
        "private VolkswagenAmarokWarehouseEntryController? warehouseExitGuardEntryController;",
        "private ModContext? context;",
        "private string vehicleTypeName = string.Empty;",
        "private GameObject? playerVehiclePrefab;",
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
print("Restored VolkswagenAmarokRuntime instance state fields if an earlier power-curve patch removed them.")
print("Volkswagen Amarok eighth performance preflight passed.")
