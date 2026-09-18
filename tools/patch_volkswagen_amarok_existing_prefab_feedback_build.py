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

# Existing worktrees may already contain the feedback-build method from an older
# pass. Retrofit the overlay refresh into that method as well.
if "AttachLightOverlaySources(root, visual.gameObject);" not in text:
    old_alignment = '''            // Blender lamp meshes were authored against the model transform. Keep
            // them exactly aligned with the corrected visual root.
            var lightSources = FindTransform(root.transform, "AmarokLightSources");
'''
    refreshed_alignment = '''            // Always replace the instantiated overlay child from the current GLB.
            // Existing prefabs otherwise retain a stale unpacked AmarokLightSources hierarchy.
            var staleLightSources = FindTransform(root.transform, "AmarokLightSources");
            if (staleLightSources != null)
                UnityEngine.Object.DestroyImmediate(staleLightSources.gameObject);
            AttachLightOverlaySources(root, visual.gameObject);

            // Blender lamp/trim meshes were authored against the model transform.
            var lightSources = FindTransform(root.transform, "AmarokLightSources");
'''
    if old_alignment not in text:
        raise SystemExit("Could not retrofit refreshed Amarok overlay sources into existing-prefab build.")
    text = text.replace(old_alignment, refreshed_alignment, 1)

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
