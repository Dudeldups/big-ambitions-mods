from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit("VolkswagenAmarokSetup.cs is missing; generate the Amarok source first.")

s = SETUP.read_text(encoding="utf-8")

# ---------------------------------------------------------------------------
# Remove the actual Audi donor hierarchy and any missing MonoBehaviour slots.
# ---------------------------------------------------------------------------
old_sequence = '''            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);'''
new_sequence = '''            StripAudiGeometry(root);
            RemoveAudiDonorHierarchy(root);
            RemoveAudiSpecificBehaviours(root);
            RemoveMissingScripts(root);
            ConfigureRootPhysics(root);'''
if "RemoveAudiDonorHierarchy(root);" not in s:
    if old_sequence not in s:
        raise SystemExit("Could not locate the Amarok donor-cleanup sequence in CreateVehiclePrefab().")
    s = s.replace(old_sequence, new_sequence, 1)

# Convert imported opaque GLB materials after they have been persisted but before
# the damage-body copy is created. This avoids magenta/error-shader materials.
material_marker = "            AssignPersistentMaterials(modelInstance);\n"
material_insert = material_marker + "            ConvertAmarokOpaqueMaterialsToHdrp(modelInstance);\n"
if "ConvertAmarokOpaqueMaterialsToHdrp(modelInstance);" not in s:
    if material_marker not in s:
        raise SystemExit("Could not locate AssignPersistentMaterials(modelInstance).")
    s = s.replace(material_marker, material_insert, 1)

# Validate the generated hierarchy before it is saved, and validate the saved
# prefab again so Unity cannot silently persist donor/missing-script/error-shader state.
save_marker = "            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);\n"
save_insert = '''            EnsureNoAudiDonorHierarchy(root);
            EnsureNoMissingScripts(root);
            EnsureNoErrorShaders(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
'''
if "EnsureNoAudiDonorHierarchy(root);" not in s:
    if save_marker not in s:
        raise SystemExit("Could not locate the prefab save call.")
    s = s.replace(save_marker, save_insert, 1)

null_check = '''            if (result == null)
                throw new InvalidOperationException("Could not save the Amarok vehicle prefab.");'''
null_check_new = null_check + '''
            EnsureNoAudiDonorHierarchy(result);
            EnsureNoMissingScripts(result);
            EnsureNoErrorShaders(result);'''
if "EnsureNoAudiDonorHierarchy(result);" not in s:
    if null_check not in s:
        raise SystemExit("Could not locate the Amarok prefab-save null check.")
    s = s.replace(null_check, null_check_new, 1)

helper_marker = "    private static void RemoveAudiSpecificBehaviours(GameObject root)\n"
helpers = r'''    private static void RemoveAudiDonorHierarchy(GameObject root)
    {
        var targets = new List<GameObject>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null || transform == root.transform)
                continue;
            var name = transform.name;
            if (string.Equals(name, "2020_abt_sportline_audi_rs6-r", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("abt_sportline_audi_rs6", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                targets.Add(transform.gameObject);
            }
        }

        // The known donor geometry is one hierarchy root. Destroy only top-most
        // matches so nested matches, if any, are not destroyed twice.
        foreach (var target in targets)
        {
            if (target == null)
                continue;
            var parent = target.transform.parent;
            var nested = false;
            while (parent != null && parent != root.transform)
            {
                if (targets.Contains(parent.gameObject))
                {
                    nested = true;
                    break;
                }
                parent = parent.parent;
            }
            if (!nested)
                UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void RemoveMissingScripts(GameObject root)
    {
        var removed = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var gameObject = transform.gameObject;
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (count <= 0)
                continue;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            removed += count;
        }
        Debug.Log($"VolkswagenAmarok: removed {removed} missing donor script component(s).");
    }

    private static void EnsureNoMissingScripts(GameObject root)
    {
        var missing = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform != null)
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        }
        if (missing != 0)
            throw new InvalidOperationException(
                $"Generated Amarok prefab still contains {missing} missing script component(s).");
    }

    private static void EnsureNoAudiDonorHierarchy(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var name = transform.name;
            if (string.Equals(name, "2020_abt_sportline_audi_rs6-r", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("abt_sportline_audi_rs6", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    $"Generated Amarok prefab still contains Audi donor hierarchy '{name}'.");
            }
        }
    }

    private static void ConvertAmarokOpaqueMaterialsToHdrp(GameObject model)
    {
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null)
            throw new InvalidOperationException("HDRP/Lit shader is unavailable in the SDK project.");

        var converted = 0;
        var materials = new HashSet<Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !materials.Add(material))
                    continue;
                if (VolkswagenAmarokMaterials.IsTransparentMaterial(material))
                    continue;

                var shaderName = material.shader?.name ?? string.Empty;
                if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal) ||
                    shaderName.StartsWith("High Definition Render Pipeline/", StringComparison.Ordinal) ||
                    material.HasProperty("_SupportDecals"))
                {
                    continue;
                }

                var baseColor = ReadMaterialColor(
                    material, "baseColorFactor", "_BaseColor", "_Color", Color.white);
                baseColor.a = 1f;
                var baseTextureProperty = FirstMaterialTextureProperty(
                    material, "baseColorTexture", "_BaseColorMap", "_MainTex");
                var baseTexture = baseTextureProperty == null
                    ? null
                    : material.GetTexture(baseTextureProperty);
                var baseScale = baseTextureProperty == null
                    ? Vector2.one
                    : material.GetTextureScale(baseTextureProperty);
                var baseOffset = baseTextureProperty == null
                    ? Vector2.zero
                    : material.GetTextureOffset(baseTextureProperty);
                var normalTextureProperty = FirstMaterialTextureProperty(
                    material, "normalTexture", "_NormalMap", "_BumpMap");
                var normalTexture = normalTextureProperty == null
                    ? null
                    : material.GetTexture(normalTextureProperty);
                var metallic = ReadMaterialFloat(material, "metallicFactor", "_Metallic", 0f);
                var roughness = ReadMaterialFloat(material, "roughnessFactor", null, 0.55f);

                material.shader = hdrpLit;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
                if (material.HasProperty("_BaseColorMap") && baseTexture != null)
                {
                    material.SetTexture("_BaseColorMap", baseTexture);
                    material.SetTextureScale("_BaseColorMap", baseScale);
                    material.SetTextureOffset("_BaseColorMap", baseOffset);
                }
                if (material.HasProperty("_NormalMap") && normalTexture != null)
                    material.SetTexture("_NormalMap", normalTexture);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(roughness));
                if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 0f);
                if (material.HasProperty("_AlphaCutoffEnable")) material.SetFloat("_AlphaCutoffEnable", 0f);
                if (material.HasProperty("_SupportDecals")) material.SetFloat("_SupportDecals", 0f);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
                EditorUtility.SetDirty(material);
                converted++;
            }
        }

        Debug.Log($"VolkswagenAmarok: converted {converted} opaque imported material(s) to HDRP/Lit.");
    }

    private static string? FirstMaterialTextureProperty(Material material, params string[] names)
    {
        foreach (var name in names)
            if (material.HasProperty(name) && material.GetTexture(name) != null)
                return name;
        return null;
    }

    private static Color ReadMaterialColor(
        Material material,
        string first,
        string second,
        string third,
        Color fallback)
    {
        if (material.HasProperty(first)) return material.GetColor(first);
        if (material.HasProperty(second)) return material.GetColor(second);
        if (material.HasProperty(third)) return material.GetColor(third);
        return fallback;
    }

    private static float ReadMaterialFloat(
        Material material,
        string first,
        string? second,
        float fallback)
    {
        if (material.HasProperty(first)) return material.GetFloat(first);
        if (second != null && material.HasProperty(second)) return material.GetFloat(second);
        return fallback;
    }

    private static void EnsureNoErrorShaders(GameObject root)
    {
        var broken = new List<string>();
        var seen = new HashSet<Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !seen.Add(material))
                    continue;
                var shader = material.shader;
                if (shader == null ||
                    string.Equals(shader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal))
                {
                    broken.Add($"{renderer.name}/{material.name}");
                }
            }
        }

        if (broken.Count > 0)
            throw new InvalidOperationException(
                "Generated Amarok prefab still has error-shader materials: " +
                string.Join(", ", broken.GetRange(0, Math.Min(8, broken.Count))));
    }

'''

if "private static void RemoveAudiDonorHierarchy(GameObject root)" not in s:
    if helper_marker not in s:
        raise SystemExit("Could not locate RemoveAudiSpecificBehaviours insertion point.")
    s = s.replace(helper_marker, helpers + helper_marker, 1)

required = [
    "RemoveAudiDonorHierarchy(root);",
    "RemoveMissingScripts(root);",
    "ConvertAmarokOpaqueMaterialsToHdrp(modelInstance);",
    "EnsureNoMissingScripts(root);",
    "EnsureNoErrorShaders(root);",
    '"2020_abt_sportline_audi_rs6-r"',
    "GameObjectUtility.RemoveMonoBehavioursWithMissingScript",
]
missing = [item for item in required if item not in s]
if missing:
    raise SystemExit("Amarok prefab-cleanup patch failed; missing: " + ", ".join(missing))

SETUP.write_text(s, encoding="utf-8", newline="\n")
print("Patched Amarok prefab cleanup: Audi donor hierarchy removal + missing-script cleanup.")
print("Patched Amarok material import: opaque GLB materials are converted to HDRP/Lit.")
print("Added hard prefab validation for donor hierarchy, missing scripts and error shaders.")
