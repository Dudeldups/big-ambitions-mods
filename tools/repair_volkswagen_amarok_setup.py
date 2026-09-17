from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
RUNTIME = MOD / "Scripts/VolkswagenAmarokRuntime.cs"
MATERIALS = MOD / "Scripts/VolkswagenAmarokMaterials.cs"
DRIVER = MOD / "Scripts/VolkswagenAmarokDriverController.cs"

for path in (SETUP, RUNTIME, MATERIALS, DRIVER):
    if not path.is_file():
        raise SystemExit(f"Generated Amarok source file was not found: {path}")


def save(path, text):
    path.write_text(text, encoding="utf-8", newline="\n")


def patch_method(text, start_pattern, end_marker, replacement, label):
    pattern = re.compile(start_pattern + r".*?\n    \}\n\n    " + re.escape(end_marker), re.S)
    updated, count = pattern.subn(replacement + "\n\n    " + end_marker, text, count=1)
    if count != 1:
        raise SystemExit(f"Could not patch {label}.")
    return updated


def set_float_constant(text, name, literal):
    updated, count = re.subn(
        rf"private const float {re.escape(name)} = [^;]+;",
        f"private const float {name} = {literal};",
        text,
        count=1,
    )
    if count != 1:
        raise SystemExit(f"Could not set {name}.")
    return updated


# ---------------------------------------------------------------------------
# VolkswagenAmarokSetup.cs
# ---------------------------------------------------------------------------
text = SETUP.read_text(encoding="utf-8")

# Enforce the target's actual dimensions and non-sports pickup tuning. This also
# repairs values that the original Porsche-derived string patch missed.
for name, literal in {
    "TargetLength": "5.254f",
    "TargetWidth": "1.954f",
    "TargetHeight": "1.834f",
    "FrontTrack": "1.654f",
    "RearTrack": "1.658f",
    "Wheelbase": "3.097f",
    "FrontTireRadius": "0.382f",
    "RearTireRadius": "0.382f",
    "FrontTireWidth": "0.255f",
    "RearTireWidth": "0.255f",
    "WheelInset": "0f",
    "VehicleLinearDrag": "0.045f",
    "VehicleBrakeForce": "2600f",
    "BrakeMaxTorque": "2600f",
    "FrontForwardGrip": "0.84f",
    "RearForwardGrip": "0.86f",
    "FrontForwardStiffness": "1.08f",
    "RearForwardStiffness": "1.10f",
    "TireFrictionCircleStrength": "0.96f",
    "AntiRollBarForce": "3800f",
    "FrontSuspensionTravel": "0.160f",
    "RearSuspensionTravel": "0.180f",
    "DeformationStrength": "0.12f",
    "DeformationRadius": "0.30f",
}.items():
    text = set_float_constant(text, name, literal)

text = re.sub(
    r"private static readonly Vector3 StableCenterOfMass = new Vector3\([^;]+;",
    "private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.34f, -0.05f);",
    text,
    count=1,
)
text = text.replace("body.mass = 1450f;", "body.mass = 2078f;")
text = text.replace("body.angularDrag = 1.45f;", "body.angularDrag = 1.65f;")
text = text.replace('SetNumber(serialized, "baseMass", 1450f);', 'SetNumber(serialized, "baseMass", 2078f);')
text = text.replace('SetNumber(serialized, "combinedMass", 1450f);', 'SetNumber(serialized, "combinedMass", 2078f);')
text = text.replace('SetRelativeNumber(serialized, "spring.maxForce", 19000f);', 'SetRelativeNumber(serialized, "spring.maxForce", 24000f);')

# VehicleType values.
for pattern, replacement in {
    r'SetNumber\(serialized, "price", [^;]+;': 'SetNumber(serialized, "price", 49900f);',
    r'SetNumber\(serialized, "maxFuel", [^;]+;': 'SetNumber(serialized, "maxFuel", 80f);',
    r'SetNumber\(serialized, "maxCargoCapacity", [^;]+;': 'SetNumber(serialized, "maxCargoCapacity", 0f); // No invented cargo capacity.',
    r'SetNumber\(serialized, "maxSpeed", [^;]+;': 'SetNumber(serialized, "maxSpeed", 193f);',
    r'SetNumber\(serialized, "enginePower", [^;]+;': 'SetNumber(serialized, "enginePower", 165f);',
    r'SetNumber\(serialized, "turnRadius", [^;]+;': 'SetNumber(serialized, "turnRadius", 35f);',
    r'SetNumber\(serialized, "damageIntensity", [^;]+;': 'SetNumber(serialized, "damageIntensity", 0.28f);',
    r'SetBool\(serialized, "isATruck", (?:true|false)\);': 'SetBool(serialized, "isATruck", true);',
    r'SetBool\(serialized, "isLuxuryCar", (?:true|false)\);': 'SetBool(serialized, "isLuxuryCar", false);',
}.items():
    text = re.sub(pattern, replacement, text)

# Actual 8-speed ratios. Keep the private donor symbol name so unrelated helper
# references continue compiling, but the data is now Amarok data.
text, count = re.subn(
    r"private static readonly float\[] GT3RSGears =\s*\{.*?\};",
    '''private static readonly float[] GT3RSGears =\n    {\n        -3.317f, 0f, 4.714f, 3.143f, 2.106f, 1.667f, 1.285f, 1.000f, 0.839f, 0.667f,\n    };''',
    text,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("Could not patch Amarok setup gear ratios.")

text, count = re.subn(
    r"private static AnimationCurve CreateGT3RSPowerCurve\(\) =>\s*new AnimationCurve\(.*?\);",
    '''private static AnimationCurve CreateGT3RSPowerCurve() =>\n        new AnimationCurve(\n            new Keyframe(0f, 0f), new Keyframe(0.16f, 0.18f),\n            new Keyframe(0.31f, 0.49f), new Keyframe(0.44f, 0.70f),\n            new Keyframe(0.61f, 0.96f), new Keyframe(0.67f, 1.00f),\n            new Keyframe(0.78f, 0.96f), new Keyframe(0.89f, 0.88f),\n            new Keyframe(1.00f, 0.70f));''',
    text,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("Could not patch Amarok diesel power curve.")

# Light-overlay helper. The source GLB contains no authored emissive overlay meshes.
call = "AttachLightOverlaySources(root, modelInstance);"
signature = "private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)"
marker = "    private static void ConfigureExitMarkers(GameObject root, GameObject model)"
helper = '''    private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)\n    {\n        var source = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);\n        if (source == null)\n            throw new InvalidOperationException(\n                "Models/AmarokLightOverlays.glb is missing. Export it from the supplied Blender vertex groups first.");\n        var overlay = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject ??\n                      throw new InvalidOperationException("Could not instantiate Amarok light overlays.");\n        PrefabUtility.UnpackPrefabInstance(\n            overlay, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);\n        overlay.name = "AmarokLightSources";\n        overlay.transform.localPosition = modelInstance.transform.localPosition;\n        overlay.transform.localRotation = modelInstance.transform.localRotation;\n        overlay.transform.localScale = modelInstance.transform.localScale;\n        foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))\n            renderer.enabled = false;\n    }\n\n'''
if call not in text:
    text = text.replace(
        "            NormalizeModel(modelInstance);\n            ConfigureExitMarkers(root, modelInstance);",
        "            NormalizeModel(modelInstance);\n            AttachLightOverlaySources(root, modelInstance);\n            ConfigureExitMarkers(root, modelInstance);",
        1,
    )
if signature not in text:
    if marker not in text:
        raise SystemExit("Could not find ConfigureExitMarkers insertion point.")
    text = text.replace(marker, helper + marker, 1)

# Actual Amarok steering-wheel hierarchy.
exit_method = '''    private static void ConfigureExitMarkers(GameObject root, GameObject model)\n    {\n        var steeringWheel = FindTransformWithNameFragment(model.transform, "steering_ok") ??\n                            throw new InvalidOperationException("Amarok steering wheel node 'steering_ok' is missing.");\n        if (!TryGetRendererBounds(steeringWheel, out var steeringBounds))\n            throw new InvalidOperationException("Amarok steering wheel renderer bounds are missing.");\n        var steeringPosition = root.transform.InverseTransformPoint(steeringBounds.center);\n        var driverSide = steeringPosition.x < 0f ? -1.55f : 1.55f;\n        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.12f, -0.10f));\n        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.12f, -0.10f));\n        Debug.Log(\n            $"VolkswagenAmarok: steering wheel x={steeringPosition.x:F3}; " +\n            $"driver exit x={driverSide:F2}.");\n    }\n'''
text, count = re.subn(
    r"    private static void ConfigureExitMarkers\(GameObject root, GameObject model\)\s*\{.*?\n    \}\n",
    exit_method,
    text,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("Could not patch Amarok exit markers.")

# The actual outer shell is vw_amorak_2018:body_phong5_0. The previous patch was
# still looking for Porsche's body_gt3rs/carPaint pair and caused the current error.
body_source = '''        var bodyNode = FindTransform(modelInstance.transform, "vw_amorak_2018:body_phong5_0") ??\n                       FindTransformWithNameFragment(modelInstance.transform, "body_phong5_0") ??\n                       throw new InvalidOperationException("Amarok outer body node 'body_phong5_0' is missing.");\n        var sourceRenderer = bodyNode.GetComponent<MeshRenderer>();\n        var sourceFilter = bodyNode.GetComponent<MeshFilter>();'''
text, count = re.subn(
    r"        MeshRenderer\? sourceRenderer = null;\s*foreach \(var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>\(true\)\)\s*\{.*?\n        \}\s*\n        var sourceFilter = sourceRenderer\?\.GetComponent<MeshFilter>\(\);",
    body_source,
    text,
    count=1,
    flags=re.S,
)
if count != 1 and '"vw_amorak_2018:body_phong5_0"' not in text:
    raise SystemExit("Could not patch Amarok deformable body source.")

# Body paint: most panels use phong5; rear doors use dorr_R.
text = re.sub(
    r'private static bool IsBodyPaintMaterial\(Material material\) =>\s*.*?;',
    '''private static bool IsBodyPaintMaterial(Material material) =>\n        material.name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) >= 0 ||\n        material.name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) >= 0;''',
    text,
    count=1,
    flags=re.S,
)

# Author the target powertrain directly instead of allowing donor values to survive
# string replacements.
powertrain = '''    private static void ConfigurePowertrain(GameObject root)\n    {\n        var found = false;\n        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))\n        {\n            if (component == null)\n                continue;\n            var serialized = new SerializedObject(component);\n            if (string.Equals(component.GetType().FullName,\n                    "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))\n            {\n                found = true;\n                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1000f);\n                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 250f);\n                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 300f);\n                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);\n                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);\n                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.16f);\n                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 165f);\n                var curve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");\n                if (curve?.propertyType != SerializedPropertyType.AnimationCurve)\n                    throw new InvalidOperationException("Reference engine power curve is missing.");\n                curve.animationCurveValue = CreateGT3RSPowerCurve();\n                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 725f);\n                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 4500f);\n                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);\n                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);\n                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);\n                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);\n                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.32f);\n                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.70f);\n                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);\n                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);\n                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.28f);\n                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 1500f);\n                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 4200f);\n                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);\n                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);\n\n                var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");\n                if (wheelGroups == null || !wheelGroups.isArray || wheelGroups.arraySize != 2)\n                    throw new InvalidOperationException("Reference wheel groups are missing.");\n                for (var index = 0; index < wheelGroups.arraySize; index++)\n                {\n                    var antiRoll = wheelGroups.GetArrayElementAtIndex(index)\n                        .FindPropertyRelative("antiRollBarForce");\n                    if (antiRoll?.propertyType != SerializedPropertyType.Float)\n                        throw new InvalidOperationException("Wheel group anti-roll setting is missing.");\n                    antiRoll.floatValue = AntiRollBarForce;\n                }\n\n                var differentials = FindRelativeProperty(serialized, "powertrain.differentials");\n                if (differentials == null || !differentials.isArray)\n                    throw new InvalidOperationException("Reference differentials are missing.");\n                var awd = false;\n                for (var index = 0; index < differentials.arraySize; index++)\n                {\n                    var differential = differentials.GetArrayElementAtIndex(index);\n                    var name = differential.FindPropertyRelative("name");\n                    if (name?.propertyType != SerializedPropertyType.String ||\n                        !string.Equals(name.stringValue, "Center Differential", StringComparison.Ordinal))\n                        continue;\n                    var bias = differential.FindPropertyRelative("biasAB");\n                    if (bias?.propertyType != SerializedPropertyType.Float)\n                        throw new InvalidOperationException("Center differential torque bias is missing.");\n                    bias.floatValue = 0.60f;\n                    awd = true;\n                }\n                if (!awd)\n                    throw new InvalidOperationException("Center differential could not be configured for 4MOTION AWD.");\n\n                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");\n                if (gears == null || !gears.isArray)\n                    throw new InvalidOperationException("Reference transmission gear array is missing.");\n                gears.arraySize = GT3RSGears.Length;\n                for (var index = 0; index < GT3RSGears.Length; index++)\n                    gears.GetArrayElementAtIndex(index).floatValue = GT3RSGears[index];\n            }\n            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))\n            {\n                SetRelativeNumber(serialized, "module.speedLimit", 193f);\n            }\n            serialized.ApplyModifiedPropertiesWithoutUndo();\n        }\n        if (!found)\n            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");\n    }'''
text = patch_method(
    text,
    r"    private static void ConfigurePowertrain\(GameObject root\)\s*\{",
    "private static void NormalizeModel(GameObject model)",
    powertrain,
    "Amarok setup powertrain",
)

# The old verifier also encoded Porsche-only windshield/exhaust/light names and
# 7-speed assumptions. Make its checks match the Amarok hierarchy and drivetrain.
text = re.sub(
    r'            var windshield = FindTransformWithNameFragment\(visual, "windshield"\);.*?            var bodyUpright =\s*!float\.IsNaN\(windshieldHeight\) && !float\.IsNaN\(exhaustHeight\) &&\s*windshieldHeight > exhaustHeight;',
    '''            var windshieldHeight = bounds.max.y;\n            var exhaustHeight = bounds.min.y;\n            var bodyUpright = bounds.size.y > 1f && bounds.size.y < bounds.size.x;''',
    text,
    count=1,
    flags=re.S,
)
text = text.replace('transform.name.IndexOf("fascia_mid", StringComparison.OrdinalIgnoreCase) >= 0', 'transform.name.IndexOf("1RearDrivingLights", StringComparison.OrdinalIgnoreCase) >= 0')
text = text.replace('transform.name.IndexOf("tailgate", StringComparison.OrdinalIgnoreCase) >= 0', 'transform.name.IndexOf("ThirdBrakeLight", StringComparison.OrdinalIgnoreCase) >= 0')
text = text.replace('transform.name.IndexOf("headlight_L_led", StringComparison.OrdinalIgnoreCase) >= 0 ||\n                    transform.name.IndexOf("headlight_R_led", StringComparison.OrdinalIgnoreCase) >= 0', 'transform.name.IndexOf("BDRL_Indicator_FL", StringComparison.OrdinalIgnoreCase) >= 0 ||\n                    transform.name.IndexOf("BDRL_Indicator_FR", StringComparison.OrdinalIgnoreCase) >= 0')
text = text.replace('transmissionVerified = gearCount == 7 && gears != null && gears.arraySize == 9;', 'transmissionVerified = gearCount == 8 && gears != null && gears.arraySize == 10;')
text = text.replace('Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1250f)', 'Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1000f)')
text = text.replace('Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 600f)', 'Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 250f)')
text = text.replace('Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 550f)', 'Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 300f)')
text = text.replace('Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.075f)', 'Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.16f)')
text = text.replace('(TargetHeight - WheelCenterRideHeightOffset)', 'TargetHeight')
for donor_requirement in [
    '                interiorAccentPaintSlots == 0 ||\n',
    '                caliperSlots != 4 ||\n',
    '                rimSlots != 4 ||\n',
]:
    text = text.replace(donor_requirement, '')
text = text.replace('sevenSpeed={transmissionVerified}', 'eightSpeed={transmissionVerified}')

# Update setup summary so the Console no longer claims GT3/PDK/RWD behavior.
text = text.replace(
    '"VolkswagenAmarok setup complete: generated a fitted GT3 RS with four " +\n            "independent wheel visuals, seven-speed PDK, RWD, flat-six audio, functional lamp " +\n            "geometry, colorable body/calipers, and corrected glass materials."',
    '"VolkswagenAmarok setup complete: generated the 2017 Amarok V6 with four " +\n            "independent wheel visuals, 8-speed automatic, 4MOTION AWD, V6 TDI tuning, " +\n            "functional lamp geometry, body repaint support and damage geometry."',
)

save(SETUP, text)


# ---------------------------------------------------------------------------
# VolkswagenAmarokRuntime.cs
# The previous generator changed several constants but left the active Porsche
# ConfigurePowertrain method intact. That would have reset the Amarok to 7-speed,
# RWD and 8850 rpm as soon as the vehicle initialized in-game.
# ---------------------------------------------------------------------------
runtime = RUNTIME.read_text(encoding="utf-8")
for name, literal in {
    "VehicleMass": "2078f",
    "EnginePowerKw": "165f",
    "EngineIdleRpm": "725f",
    "EngineLimitRpm": "4500f",
    "SpeedLimitKph": "193f",
    "FinalDriveRatio": "3.70f",
    "EngineInertia": "0.16f",
    "ClutchEngagementRpm": "1000f",
    "ClutchThrottleOffsetRpm": "250f",
    "ClutchEngagementRange": "300f",
    "ClutchCreepTorque": "0f",
    "TireFrictionCircleStrength": "0.96f",
    "AntiRollBarForce": "3800f",
    "FrontSuspensionTravel": "0.160f",
    "RearSuspensionTravel": "0.180f",
    "FrontTireRadius": "0.382f",
    "RearTireRadius": "0.382f",
    "FrontTireWidth": "0.255f",
    "RearTireWidth": "0.255f",
    "VehicleLinearDrag": "0.045f",
    "FrontForwardGrip": "0.84f",
    "RearForwardGrip": "0.86f",
    "FrontForwardStiffness": "1.08f",
    "RearForwardStiffness": "1.10f",
    "DeformationStrength": "0.12f",
    "DeformationRadius": "0.30f",
    "DamageIntensity": "0.72f",
    "DamageDecelerationThreshold": "650f",
}.items():
    runtime = set_float_constant(runtime, name, literal)

runtime = re.sub(
    r"private static readonly Vector3 StableCenterOfMass = new Vector3\([^;]+;",
    "private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.34f, -0.05f);",
    runtime,
    count=1,
)
runtime = runtime.replace('SetFloat(spring, "maxForce", 19000f);', 'SetFloat(spring, "maxForce", 24000f);')
runtime, count = re.subn(
    r"private static readonly float\[] GT3RSGears =\s*\{.*?\};",
    '''private static readonly float[] GT3RSGears =\n    {\n        -3.317f, 0f, 4.714f, 3.143f, 2.106f, 1.667f, 1.285f, 1.000f, 0.839f, 0.667f,\n    };''',
    runtime,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("Could not patch Amarok runtime gear ratios.")
runtime, count = re.subn(
    r"private static AnimationCurve CreateGT3RSPowerCurve\(\) =>\s*new AnimationCurve\(.*?\);",
    '''private static AnimationCurve CreateGT3RSPowerCurve() =>\n        new AnimationCurve(\n            new Keyframe(0f, 0f), new Keyframe(0.16f, 0.18f),\n            new Keyframe(0.31f, 0.49f), new Keyframe(0.44f, 0.70f),\n            new Keyframe(0.61f, 0.96f), new Keyframe(0.67f, 1.00f),\n            new Keyframe(0.78f, 0.96f), new Keyframe(0.89f, 0.88f),\n            new Keyframe(1.00f, 0.70f));''',
    runtime,
    count=1,
    flags=re.S,
)
if count != 1:
    raise SystemExit("Could not patch Amarok runtime diesel power curve.")

runtime_powertrain = '''    private static bool ConfigurePowertrain(GameObject root)\n    {\n        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))\n        {\n            if (component == null || !string.Equals(\n                    component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController",\n                    StringComparison.Ordinal))\n                continue;\n\n            var powertrain = GetMember(component, "powertrain");\n            var clutch = GetMember(powertrain, "clutch");\n            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);\n            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);\n            SetFloat(clutch, "engagementRange", ClutchEngagementRange);\n            SetFloat(clutch, "creepTorque", ClutchCreepTorque);\n            SetFloat(clutch, "creepSpeedLimit", 1f);\n\n            var engine = GetMember(powertrain, "engine");\n            SetFloat(engine, "inertia", EngineInertia);\n            SetFloat(engine, "maxPower", EnginePowerKw);\n            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateGT3RSPowerCurve());\n            SetFloat(engine, "idleRPM", EngineIdleRpm);\n            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);\n            SetFloat(engine, "startDuration", EngineStartDuration);\n            SetBool(engine, "stallingEnabled", false);\n            var forcedInduction = GetMember(engine, "forcedInduction");\n            SetBool(forcedInduction, "useForcedInduction", true);\n            SetFloat(forcedInduction, "powerGainMultiplier", 1f);\n            SetFloat(forcedInduction, "spoolUpTime", 0.32f);\n\n            var transmission = GetMember(powertrain, "transmission");\n            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);\n            SetFloat(transmission, "shiftDuration", 0.28f);\n            SetFloat(transmission, "_downshiftRPM", 1500f);\n            SetFloat(transmission, "_upshiftRPM", 4200f);\n            SetInt(transmission, "forwardGearCount", 8);\n            SetInt(transmission, "reverseGearCount", 1);\n            SetInt(transmission, "transmissionType", 1);\n            SetFloatArray(transmission, "gears", GT3RSGears);\n\n            if (GetMember(powertrain, "wheelGroups") is System.Collections.IList wheelGroups)\n                foreach (var wheelGroup in wheelGroups)\n                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);\n\n            var awd = false;\n            if (GetMember(powertrain, "differentials") is System.Collections.IList differentials)\n            {\n                foreach (var differential in differentials)\n                {\n                    if (!string.Equals(GetMember(differential, "name") as string,\n                            "Center Differential", StringComparison.Ordinal))\n                        continue;\n                    SetFloat(differential, "biasAB", 0.60f);\n                    awd = true;\n                }\n            }\n\n            foreach (var other in root.GetComponentsInChildren<MonoBehaviour>(true))\n                if (other != null && string.Equals(other.GetType().Name,\n                        "SpeedLimiterModuleWrapper", StringComparison.Ordinal))\n                    SetFloat(GetMember(other, "module"), "speedLimit", SpeedLimitKph);\n\n            return GetInt(transmission, "forwardGearCount") == 8 && awd;\n        }\n        return false;\n    }'''
runtime = patch_method(
    runtime,
    r"    private static bool ConfigurePowertrain\(GameObject root\)\s*\{",
    "private static object? GetMember(object? target, string name)",
    runtime_powertrain,
    "Amarok runtime powertrain",
)
runtime = runtime.replace("transmission=7-speed-PDK, rwd=true", "transmission=8-speed-automatic, awd=true")
runtime = runtime.replace("official-flat-six-profile", "V6-TDI-low-rpm-profile")
save(RUNTIME, runtime)


# ---------------------------------------------------------------------------
# Materials and repaint
# CHROME is shared by wheels and exterior trim in this GLB. Treating every CHROME
# material as a Porsche rim would darken the Amarok's chrome trim at runtime.
# ---------------------------------------------------------------------------
materials = MATERIALS.read_text(encoding="utf-8")
materials = re.sub(
    r'private const string RimMaterialMarker = "[^"]*";',
    'private const string RimMaterialMarker = "__AMAROK_SEPARATE_RIM_FINISH_DISABLED__";',
    materials,
)
materials = re.sub(
    r'private const string BodyMaterialMarker = "[^"]*";',
    'private const string BodyMaterialMarker = "phong5";',
    materials,
    count=1,
)
if "private static bool IsAmarokBodyPaintMaterial" not in materials:
    paint_marker = "    private void FindPaintSlots()"
    paint_helper = '''    private static bool IsAmarokBodyPaintMaterial(Material material) =>\n        material.name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) >= 0 ||\n        material.name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) >= 0;\n\n'''
    if paint_marker not in materials:
        raise SystemExit("Could not find Amarok paint-slot insertion point.")
    materials = materials.replace(paint_marker, paint_helper + paint_marker, 1)
materials = re.sub(
    r'if \(material != null && material\.name\.IndexOf\(\s*BodyMaterialMarker,\s*StringComparison\.OrdinalIgnoreCase\) >= 0\)',
    'if (material != null && IsAmarokBodyPaintMaterial(material))',
    materials,
    count=1,
    flags=re.S,
)
materials = materials.replace("if (bodySlots == 0 || interiorAccentSlots == 0)", "if (bodySlots == 0)")
save(MATERIALS, materials)


# ---------------------------------------------------------------------------
# Driver controller
# ---------------------------------------------------------------------------
driver = DRIVER.read_text(encoding="utf-8")
driver = re.sub(
    r'private const string SteeringWheelMarker = "[^"]+";',
    'private const string SteeringWheelMarker = "steering_ok";',
    driver,
    count=1,
)
driver = driver.replace("GT3 RS steering-wheel seat reference is missing.", "Amarok steering-wheel seat reference is missing.")
save(DRIVER, driver)


# ---------------------------------------------------------------------------
# Preflight — catch the donor assumptions before Unity does.
# ---------------------------------------------------------------------------
setup_check = SETUP.read_text(encoding="utf-8")
runtime_check = RUNTIME.read_text(encoding="utf-8")
materials_check = MATERIALS.read_text(encoding="utf-8")
driver_check = DRIVER.read_text(encoding="utf-8")

required = [
    (setup_check, '"vw_amorak_2018:body_phong5_0"'),
    (setup_check, 'FindTransformWithNameFragment(model.transform, "steering_ok")'),
    (setup_check, 'SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);'),
    (setup_check, 'bias.floatValue = 0.60f;'),
    (runtime_check, 'SetInt(transmission, "forwardGearCount", 8);'),
    (runtime_check, 'SetFloat(differential, "biasAB", 0.60f);'),
    (runtime_check, 'SetBool(forcedInduction, "useForcedInduction", true);'),
    (runtime_check, 'private const float VehicleMass = 2078f;'),
    (runtime_check, 'private const float EngineLimitRpm = 4500f;'),
    (materials_check, 'IsAmarokBodyPaintMaterial(material)'),
    (driver_check, 'private const string SteeringWheelMarker = "steering_ok";'),
]
missing = [needle for haystack, needle in required if needle not in haystack]
forbidden = [
    (setup_check, '"steer_3"'),
    (setup_check, '"body_gt3rs"'),
    (runtime_check, 'SetInt(transmission, "forwardGearCount", 7);'),
    (runtime_check, 'SetFloat(differential, "biasAB", 1f);'),
    (runtime_check, 'SetBool(forcedInduction, "useForcedInduction", false);'),
]
remaining = [needle for haystack, needle in forbidden if needle in haystack]
if missing or remaining:
    raise SystemExit(
        "Volkswagen Amarok generated-source preflight failed.\n"
        + ("Missing: " + ", ".join(missing) + "\n" if missing else "")
        + ("Donor remnants: " + ", ".join(remaining) if remaining else "")
    )

print("Patched setup: real Amarok body/steering hierarchy, 2078 kg physics and 8-speed 4MOTION drivetrain.")
print("Patched runtime: it can no longer overwrite the Amarok with Porsche 7-speed/RWD/9000-rpm values.")
print("Patched repaint/material mapping: phong5 + dorr_R body panels, shared chrome trim preserved.")
print("Patched seated-driver steering reference: steering_ok.")
print("Volkswagen Amarok generated-source preflight passed.")