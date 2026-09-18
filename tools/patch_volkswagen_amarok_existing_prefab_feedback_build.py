from pathlib import Path
import re
import runpy

REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "tools"
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup was not found: {SETUP}")

text = SETUP.read_text(encoding="utf-8")
method_name = "ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle"

if method_name not in text:
    marker = "    public static void RegenerateAndBuildStandaloneWindowsAssetBundle()\n"
    if marker not in text:
        marker = '    [MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]\n'
    if marker not in text:
        raise SystemExit("Could not locate Amarok feedback-build insertion point.")

    method = '''    public static void ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            var visual = FindTransform(root.transform, "AmarokVisual") ??
                         FindTransformWithNameFragment(root.transform, "AmarokVisual") ??
                         throw new InvalidOperationException("Existing Amarok visual root is missing.");

            if (!TryGetModelBodyBounds(visual, out var bounds))
                throw new InvalidOperationException("Existing Amarok visual has no measurable body bounds.");

            // The current prefab was normalized to 1.954 m using bounds that include
            // the mirrors. Correct X only; keep the already-correct Y/Z proportions.
            var xScaleCorrection = VisualTargetWidth / Mathf.Max(0.001f, bounds.size.x);
            var visualScale = visual.localScale;
            visualScale.x *= xScaleCorrection;
            visual.localScale = visualScale;

            if (!TryGetModelBodyBounds(visual, out bounds))
                throw new InvalidOperationException("Corrected Amarok visual bounds could not be measured.");

            // Recenter the widened visual around the vehicle root before setting the
            // requested body height. This is idempotent when run repeatedly.
            var centerLocal = root.transform.InverseTransformPoint(bounds.center);
            visual.position += root.transform.TransformVector(
                new Vector3(-centerLocal.x, 0f, -centerLocal.z));

            if (!TryGetModelBodyBounds(visual, out bounds))
                throw new InvalidOperationException("Recentered Amarok visual bounds could not be measured.");
            var bottomLocalY = root.transform.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
            visual.position += root.transform.TransformVector(
                new Vector3(0f, BodyVisualBottomY - bottomLocalY, 0f));

            // Always replace the instantiated overlay child from the current GLB.
            // Merely refreshing/importing AmarokLightOverlays.glb is not enough:
            // an existing prefab keeps its previously unpacked AmarokLightSources
            // hierarchy, so newly authored light/trim vertex groups would never
            // reach the built prefab.
            var staleLightSources = FindTransform(root.transform, "AmarokLightSources");
            if (staleLightSources != null)
                UnityEngine.Object.DestroyImmediate(staleLightSources.gameObject);
            AttachLightOverlaySources(root, visual.gameObject);

            // Blender lamp/trim meshes were authored against the model transform.
            // Keep them exactly aligned with the corrected visual root.
            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.localPosition = visual.localPosition;
                lightSources.localRotation = visual.localRotation;
                lightSources.localScale = visual.localScale;
            }

            // The visible outer shell is the root-level deformable body baked from
            // the source mesh. Re-bake it after changing visual scale/position so it
            // cannot remain at the old squeezed width/height. Preserve its persisted
            // body-paint material assignment while recreating the mesh.
            var oldDamageBody = FindTransform(root.transform, "AmarokDamageBody");
            var oldDamageMaterials = oldDamageBody?.GetComponent<MeshRenderer>()?.sharedMaterials ??
                                     Array.Empty<Material>();
            if (oldDamageBody != null)
                UnityEngine.Object.DestroyImmediate(oldDamageBody.gameObject);

            var damageBody = CreateDeformableBody(root, visual.gameObject);
            var damageRenderer = damageBody.GetComponent<MeshRenderer>();
            if (damageRenderer != null && oldDamageMaterials.Length > 0)
                damageRenderer.sharedMaterials = oldDamageMaterials;

            ConfigureVehicleDeformation(root, damageBody);
            ConfigurePowertrain(root);
            ConfigureRendererReferences(root);
            MarkMaterialsDirty(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save in-game feedback changes to the Amarok prefab.");

            if (!TryGetModelBodyBounds(visual, out bounds))
                throw new InvalidOperationException("Final Amarok visual bounds could not be measured.");
            Debug.Log(
                $"VolkswagenAmarok feedback prefab patch complete: visualBounds={bounds.size}, " +
                $"visualBottom={root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y:F3}, " +
                $"downshiftRPM=1900.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildStandaloneWindowsAssetBundle();
    }

'''
    text = text.replace(marker, method + marker, 1)
    print("Added existing-prefab Amarok feedback + bundle build method (no donor regeneration required).")
else:
    print("Existing-prefab Amarok feedback + bundle build method is already present.")

# Keep the old public executeMethod name used by the PowerShell wrapper, but make
# it operate on the already-generated Amarok prefab instead of calling Generate().
# Generate() requires the Audi donor VehicleType and is unnecessary for this
# feedback iteration because the working Amarok asset/prefab already exists.
wrapper_pattern = re.compile(
    r"    public static void RegenerateAndBuildStandaloneWindowsAssetBundle\(\)\s*"
    r"\{.*?\n    \}",
    re.S,
)
wrapper_replacement = '''    public static void RegenerateAndBuildStandaloneWindowsAssetBundle()
    {
        ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle();
    }'''
text, wrapper_count = wrapper_pattern.subn(wrapper_replacement, text, count=1)
if wrapper_count != 1:
    raise SystemExit("Could not redirect RegenerateAndBuildStandaloneWindowsAssetBundle away from donor regeneration.")

# Existing worktrees may already contain the feedback-build method after several
# later light-alignment passes. Do not depend on comments/whitespace or the exact
# contents of that light block. Find the lightSources declaration inside the
# existing-prefab method and inject the refresh immediately before it.
method_start_marker = (
    "    public static void "
    "ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle()"
)
method_start = text.find(method_start_marker)
method_end = text.find(
    "    public static void RegenerateAndBuildStandaloneWindowsAssetBundle()",
    method_start + len(method_start_marker),
) if method_start >= 0 else -1
if method_start < 0 or method_end < 0:
    raise SystemExit(
        "Could not locate existing-prefab Amarok build method boundaries "
        f"start={method_start} end={method_end}."
    )

method_text = text[method_start:method_end]
refresh_call = "AttachLightOverlaySources(root, visual.gameObject);"
if refresh_call not in method_text:
    light_marker = (
        '            var lightSources = '
        'FindTransform(root.transform, "AmarokLightSources");\n'
    )
    light_at = method_text.find(light_marker)
    if light_at < 0:
        raise SystemExit(
            "Could not locate AmarokLightSources declaration inside "
            "existing-prefab build method."
        )

    refresh_block = '''            // Refresh the unpacked overlay hierarchy from the current GLB on
            // every existing-prefab build. This is required for newly authored
            // Blender light/trim vertex groups to reach the saved vehicle prefab.
            var staleLightSources = FindTransform(root.transform, "AmarokLightSources");
            if (staleLightSources != null)
                UnityEngine.Object.DestroyImmediate(staleLightSources.gameObject);
            AttachLightOverlaySources(root, visual.gameObject);

'''
    absolute_light_at = method_start + light_at
    text = (
        text[:absolute_light_at]
        + refresh_block
        + text[absolute_light_at:]
    )
    print("Retrofitted current Amarok overlay GLB refresh into existing-prefab build.")
else:
    print("Existing-prefab Amarok overlay GLB refresh is already present.")

SETUP.write_text(text, encoding="utf-8", newline="\n")

check = SETUP.read_text(encoding="utf-8")
required = [
    method_name,
    'FindTransform(root.transform, "AmarokVisual")',
    "VisualTargetWidth / Mathf.Max(0.001f, bounds.size.x)",
    "BodyVisualBottomY - bottomLocalY",
    'FindTransform(root.transform, "AmarokLightSources")',
    'AttachLightOverlaySources(root, visual.gameObject);',
    'DestroyImmediate(staleLightSources.gameObject);',
    'FindTransform(root.transform, "AmarokDamageBody")',
    "CreateDeformableBody(root, visual.gameObject)",
    "ConfigurePowertrain(root);",
    "BuildStandaloneWindowsAssetBundle();",
    "ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle();",
]
missing = [item for item in required if item not in check]
if missing:
    raise SystemExit("Existing-prefab Amarok feedback build patch failed; missing: " + ", ".join(missing))

if "RegenerateAndBuildStandaloneWindowsAssetBundle()\n    {\n        Generate();" in check:
    raise SystemExit("Existing-prefab Amarok feedback build patch failed: donor Generate() call is still active.")

print("Redirected the existing batch executeMethod to patch the current Amarok prefab without the Audi donor.")
print("Volkswagen Amarok existing-prefab feedback build preflight passed.")

# Apply the next in-game feedback pass only after the existing-prefab method is
# guaranteed to exist, so clean worktrees and already-generated worktrees follow
# the same deterministic path.
runpy.run_path(
    str(TOOLS / "patch_volkswagen_amarok_second_ingame_feedback.py"),
    run_name="__main__",
)
