from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
DST = REPO / "Assets/Mods/Volkswagen_Amarok"
if not DST.exists():
    raise SystemExit("Run tools/generate_volkswagen_amarok.py first")

def rw(rel):
    p=DST/rel
    return p, p.read_text(encoding='utf-8')

def save(p,s): p.write_text(s,encoding='utf-8',newline='\n')

# The supplied GLB has four wheel roots, each with its own tyre mesh. Replace
# Porsche-specific wheel discovery with Amarok hierarchy discovery.
p,s=rw('Editor/VolkswagenAmarokSetup.cs')
s=s.replace('using System.Collections.Generic;', 'using System.Collections.Generic;\nusing System.IO;')
s=s.replace('private const string ModelPath = ModRoot + "/Models/2017_volkswagen_amarok_v6.glb";', '''private const string ModelPath = ModRoot + "/Models/2017_volkswagen_amarok_v6.glb";\n    private const string LightOverlayModelPath = ModRoot + "/Models/AmarokLightOverlays.glb";''')
s=re.sub(r'private static void AttachWheelVisuals\(GameObject root, GameObject model\)\s*\{.*?\n    \}\n\n    private static Transform\? FindClosestPart', '''private static void AttachWheelVisuals(GameObject root, GameObject model)\n    {\n        var names = new[] { "vw_amorak_2018:wheel", "wheel", "wheel1", "wheel2" };\n        var wheels = new List<Transform>();\n        foreach (var name in names)\n        {\n            var wheel = FindTransform(model.transform, name);\n            if (wheel != null) wheels.Add(wheel);\n        }\n        if (wheels.Count != 4)\n            throw new InvalidOperationException($"Expected four Amarok wheel roots; found {wheels.Count}.");\n        var centers = new Dictionary<Transform, Vector3>();\n        var averageZ = 0f;\n        foreach (var wheel in wheels)\n        {\n            var tyre = FindTransformWithNameFragment(wheel, "_tyre_0") ??\n                       throw new InvalidOperationException($"Wheel '{wheel.name}' has no tyre mesh.");\n            if (!TryGetRendererBounds(tyre, out var bounds))\n                throw new InvalidOperationException($"Wheel '{wheel.name}' has no tyre bounds.");\n            var center = root.transform.InverseTransformPoint(bounds.center);\n            centers[wheel] = center;\n            averageZ += center.z;\n        }\n        averageZ /= 4f;\n        foreach (var wheel in wheels)\n        {\n            var center = centers[wheel];\n            var front = center.z >= averageZ;\n            var left = center.x < 0f;\n            var corner = (front ? "Front" : "Rear") + (left ? "Left" : "Right");\n            var controllerName = corner + "_WheelController";\n            var controller = FindTransform(root.transform, controllerName) ??\n                             throw new InvalidOperationException($"Missing {controllerName}.");\n            var tyre = FindTransformWithNameFragment(wheel, "_tyre_0")!;\n            TryGetRendererBounds(tyre, out var sourceBounds);\n            var target = WheelControllerPositions[controllerName];\n            controller.localPosition = target;\n            var mount = new GameObject("AmarokWheel" + corner);\n            mount.transform.SetParent(root.transform, false);\n            mount.transform.position = sourceBounds.center;\n            wheel.SetParent(mount.transform, true);\n            mount.transform.localScale = new Vector3(\n                FrontTireWidth / Mathf.Max(.001f, sourceBounds.size.x),\n                FrontTireRadius * 2f / Mathf.Max(.001f, sourceBounds.size.y),\n                FrontTireRadius * 2f / Mathf.Max(.001f, sourceBounds.size.z));\n            mount.transform.localPosition = target;\n            wheel.name = "Geometry_AmarokWheel_" + (front ? "F" : "R") + (left ? "L" : "R");\n            var fixedPivot = new GameObject("AmarokFixedCaliper" + corner);\n            fixedPivot.transform.SetParent(root.transform, false);\n            fixedPivot.transform.localPosition = target;\n            new GameObject("_caliper_placeholder").transform.SetParent(fixedPivot.transform, false);\n            AssignWheelVisual(controller, mount);\n        }\n    }\n\n    private static Transform? FindClosestPart''', s, count=1, flags=re.S)
# Body bounds must ignore rolling assemblies.
s=re.sub(r'private static bool TryGetModelBodyBounds\(Transform root, out Bounds bounds\)\s*\{.*?\n    \}', '''private static bool TryGetModelBodyBounds(Transform root, out Bounds bounds)\n    {\n        var found = false; bounds = default;\n        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))\n        {\n            var current = renderer.transform; var wheel = false;\n            while (current != null && current != root)\n            {\n                var n=current.name;\n                if (n == "vw_amorak_2018:wheel" || n == "wheel" || n == "wheel1" || n == "wheel2" || n.StartsWith("AmarokWheel", StringComparison.Ordinal)) { wheel=true; break; }\n                current=current.parent;\n            }\n            if (wheel) continue;\n            if (!found) { bounds=renderer.bounds; found=true; } else bounds.Encapsulate(renderer.bounds);\n        }\n        return found;\n    }''', s, count=1, flags=re.S)
# Source body paint and body damage mesh use the Amarok's actual material/hierarchy.
s=s.replace('HasMaterialMarker(renderer, "phong5") &&\n                HasAncestorNameFragment(renderer.transform, "body_gt3rs")', '(HasMaterialMarker(renderer, "phong5") || HasMaterialMarker(renderer, "dorr_R")) &&\n                HasAncestorNameFragment(renderer.transform, "vw_amorak_2018:body")')
s=s.replace('material.name.IndexOf("carPaint", StringComparison.OrdinalIgnoreCase) >= 0', '(material.name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) >= 0 || material.name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) >= 0)')
# Attach pre-exported Blender overlays if present. The companion exporter script
# creates one mesh object for each named vertex group.
s=s.replace('NormalizeModel(modelInstance);\n            ConfigureExitMarkers(root, modelInstance);', '''NormalizeModel(modelInstance);\n            AttachLightOverlaySources(root, modelInstance);\n            ConfigureExitMarkers(root, modelInstance);''')
marker='    private static void ConfigureExitMarkers(GameObject root, GameObject modelInstance)'
helper='''    private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)\n    {\n        var source = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);\n        if (source == null)\n            throw new InvalidOperationException("Models/AmarokLightOverlays.glb is missing. Export it from the supplied Blender vertex groups first.");\n        var overlay = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject ??\n                      throw new InvalidOperationException("Could not instantiate Amarok light overlays.");\n        PrefabUtility.UnpackPrefabInstance(overlay, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);\n        overlay.name = "AmarokLightSources";\n        overlay.transform.localPosition = modelInstance.transform.localPosition;\n        overlay.transform.localRotation = modelInstance.transform.localRotation;\n        overlay.transform.localScale = modelInstance.transform.localScale;\n        foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))\n            renderer.enabled = false;\n    }\n\n'''
if marker in s and 'AttachLightOverlaySources(GameObject root' not in s: s=s.replace(marker,helper+marker)
s=s.replace('"gt3rs_bumper_F"','"bump_front_ok"').replace('"gt3rs_bumper_R"','"bump_rear_ok"')
s=s.replace('material.name.IndexOf("wheels_chrome_1", StringComparison.OrdinalIgnoreCase) >= 0','false').replace('material.name.IndexOf("amdb11_caliper", StringComparison.OrdinalIgnoreCase) >= 0','false')
s=re.sub(r'private static int ConfigureRimFinish\(GameObject root\)\s*\{.*?\n    \}\n\n    private static void ApplyRimFinish', 'private static int ConfigureRimFinish(GameObject root) => 0;\n\n    private static void ApplyRimFinish', s, count=1, flags=re.S)
s=s.replace('rimMaterials.Count != 1 ||\n                !rimFinishValid ||','').replace('fixedCalipers != 4 ||\n                !calipersDetachedFromWheels ||','').replace('!caliperPivotsVerified ||','')
save(p,s)

# Runtime damage: deform pickup panels, lamps, glass and trim, never wheels/interior/engine.
p,s=rw('Scripts/VolkswagenAmarokRuntime.cs')
start=s.find('    private static bool IsDeformableExterior(MeshFilter filter)')
end=s.find('\n    private static bool HasAncestor(', start)
if start >= 0 and end > start:
    block='''    private static bool IsDeformableExterior(MeshFilter filter)\n    {\n        if (HasAncestor(filter.transform, "AmarokWheel") || HasAncestor(filter.transform, "interior") ||\n            HasAncestor(filter.transform, "steering_ok") || HasAncestor(filter.transform, "v6tdi") ||\n            HasAncestor(filter.transform, "tyre")) return false;\n        if (filter.name.StartsWith("AmarokDamageBody", StringComparison.Ordinal)) return true;\n        var names = new[] { "vw_amorak_2018:body", "door_rr_ok", "door_rf_ok", "door_lr_ok", "door_lf_ok",\n            "bump_rear_ok", "bump_front_ok", "boot_ok", "bonnet_ok", "far", "led", "cam",\n            "motionstock", "aventuramodular", "extra1" };\n        foreach (var name in names) if (HasAncestor(filter.transform, name)) return true;\n        return false;\n    }\n'''
    s=s[:start]+block+s[end:]
s=s.replace('transmission=7-speed-PDK, rwd=true','transmission=8-speed-automatic, awd=true').replace('official-flat-six-profile','V6-TDI-low-rpm-profile')
save(p,s)

# Material/paint markers for the actual GLB.
for rel in ['Scripts/VolkswagenAmarokMaterials.cs']:
    p,s=rw(rel)
    s=s.replace('wheels_chrome_1','CHROME').replace('carPaint','phong5').replace('HasAncestor(renderer.transform, "gt3rs_carbon_roof")','false')
    save(p,s)

# Lighting binds directly to exported Blender group meshes. Shared DRL/indicator
# meshes deliberately receive both white and amber runtime overlays.
p,s=rw('Scripts/VolkswagenAmarokLightingController.cs')
start=s.find('    public void Initialize(VehicleController controller, ModContext? modContext)')
end=s.find('\n    private void Update()', start)
init='''    public void Initialize(VehicleController controller, ModContext? modContext)\n    {\n        if (initialized && vehicle == controller) return;\n        vehicle=controller; context=modContext; LocateStateSources();\n        var r=controller.GetComponentsInChildren<MeshRenderer>(true);\n        var head=FindRendererByHierarchy(r,"BHeadlights");\n        var fl=FindRendererByHierarchy(r,"BDRL_Indicator_FL");\n        var fr=FindRendererByHierarchy(r,"BDRL_Indicator_FR");\n        var tail=FindRendererByHierarchy(r,"1RearDrivingLights");\n        var brake=FindRendererByHierarchy(r,"1BrakeLights");\n        var third=FindRendererByHierarchy(r,"ThirdBrakeLight");\n        var reverse=FindRendererByHierarchy(r,"ReverseLights");\n        var rl=FindRendererByHierarchy(r,"1IndicatorRL");\n        var rr=FindRendererByHierarchy(r,"1IndicatorRR");\n        var white=new Color(.86f,.93f,1f,1f); var red=new Color(1f,.008f,.002f,1f); var amber=new Color(1f,.48f,.015f,1f);\n        headlampOverlay=CreateOverlay(head,"Headlights",white,5.8f,1.006f);\n        daylightOverlay=CreateOverlay(fl,"DRLLeft",white,3.8f,1.006f); daylightOverlayRight=CreateOverlay(fr,"DRLRight",white,3.8f,1.006f);\n        leftBlinkerOverlay=CreateOverlay(fl,"IndicatorLeft",amber,5.2f,1.009f); rightBlinkerOverlay=CreateOverlay(fr,"IndicatorRight",amber,5.2f,1.009f);\n        rearTailOverlay=CreateOverlay(tail,"RearRunning",red,2.6f,1.006f); rearBrakeOverlay=CreateOverlay(brake,"RearBrake",red,4.6f,1.009f);\n        thirdBrakeOverlay=CreateOverlay(third,"ThirdBrake",red,4.6f,1.009f); reverseOverlay=CreateOverlay(reverse,"Reverse",white,4.4f,1.008f);\n        rearLeftBlinkerOverlay=CreateOverlay(rl,"RearIndicatorLeft",amber,5.2f,1.009f); rearRightBlinkerOverlay=CreateOverlay(rr,"RearIndicatorRight",amber,5.2f,1.009f);\n        ConfigureHeadlightBeams(); initialized=true; ApplyState();\n    }\n'''
if start >= 0 and end > start: s=s[:start]+init+s[end:]
save(p,s)

print('Applied Amarok model, wheel, damage and Blender-light integration patches.')
