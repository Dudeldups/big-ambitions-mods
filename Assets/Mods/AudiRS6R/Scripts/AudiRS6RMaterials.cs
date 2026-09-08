#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

internal readonly struct AudiRS6RMaterialFixResult
{
    internal AudiRS6RMaterialFixResult(
        int rendererCount,
        int solidRendererCount,
        int decalMasksCleared,
        int hdrpMaterialsFixed,
        int hdrpMaterialsValidated,
        string shaderNames)
    {
        RendererCount = rendererCount;
        SolidRendererCount = solidRendererCount;
        DecalMasksCleared = decalMasksCleared;
        HdrpMaterialsFixed = hdrpMaterialsFixed;
        HdrpMaterialsValidated = hdrpMaterialsValidated;
        ShaderNames = shaderNames;
    }

    internal int RendererCount { get; }
    internal int SolidRendererCount { get; }
    internal int DecalMasksCleared { get; }
    internal int HdrpMaterialsFixed { get; }
    internal int HdrpMaterialsValidated { get; }
    internal string ShaderNames { get; }
}

internal static class AudiRS6RMaterials
{
    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string FrontLampRendererName = "B:Light_Geo_lodA_B:Light_Geo_lodASG1_0";
    private const string HdMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName = "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    internal static AudiRS6RMaterialFixResult FixSolidVehicleMaterials(GameObject vehicleRoot)
    {
        var visualRoot = FindImportedVisualRoot(vehicleRoot);
        var renderers = visualRoot != null
            ? visualRoot.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        var materials = new HashSet<Material>();
        var shaderNames = new HashSet<string>(StringComparer.Ordinal);
        var solidRendererCount = 0;
        var decalMasksCleared = 0;
        var hdrpMaterialsFixed = 0;
        var hdrpMaterialsValidated = 0;

        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer ||
                renderer is LineRenderer)
            {
                continue;
            }

            var hasSolidMaterial = false;
            var hasTransparentOrCutoutMaterial = false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                if (IsTransparentOrCutout(material))
                {
                    hasTransparentOrCutoutMaterial = true;
                    continue;
                }

                hasSolidMaterial = true;
                if (!materials.Add(material))
                    continue;

                var originalShaderName = material.shader != null ? material.shader.name : "<null>";
                if (!IsHdrpMaterial(material, originalShaderName))
                {
                    shaderNames.Add(originalShaderName);
                    continue;
                }

                RebindToHdrpLit(material);
                if (FixSolidHdrpMaterial(material))
                    hdrpMaterialsValidated++;

                hdrpMaterialsFixed++;
                var finalShaderName = material.shader != null ? material.shader.name : "<null>";
                shaderNames.Add(string.Equals(originalShaderName, finalShaderName, StringComparison.Ordinal)
                    ? finalShaderName
                    : originalShaderName + "->" + finalShaderName);
            }

            // A mixed transparent/opaque renderer must retain its original layer mask. The Audi
            // import currently uses one material per renderer, but this keeps future imports safe.
            if (!hasSolidMaterial || hasTransparentOrCutoutMaterial)
                continue;

            solidRendererCount++;
            var previousMask = renderer.renderingLayerMask;
            renderer.renderingLayerMask &= ~HdrpDecalLayerMask;
            if (renderer.renderingLayerMask != previousMask)
                decalMasksCleared++;
        }

        var orderedShaderNames = new List<string>(shaderNames);
        orderedShaderNames.Sort(StringComparer.Ordinal);
        return new AudiRS6RMaterialFixResult(
            renderers.Length,
            solidRendererCount,
            decalMasksCleared,
            hdrpMaterialsFixed,
            hdrpMaterialsValidated,
            string.Join("|", orderedShaderNames));
    }

    private static GameObject? FindImportedVisualRoot(GameObject vehicleRoot)
    {
        foreach (var renderer in vehicleRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == null ||
                !string.Equals(renderer.name, FrontLampRendererName, StringComparison.Ordinal))
            {
                continue;
            }

            // The imported renderer is nested as Object_3/B:Light_Geo_lodA/<renderer>.
            // Scope material mutation to Object_3 so shared stock vehicle/effect materials are untouched.
            return renderer.transform.parent?.parent?.gameObject;
        }

        return null;
    }

    private static bool IsTransparentOrCutout(Material material)
    {
        if (material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
            material.IsKeywordEnabled("_ALPHATEST_ON"))
        {
            return true;
        }

        if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f)
            return true;
        if (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f)
            return true;
        if (HasTransparentColor(material, "baseColorFactor") ||
            HasTransparentColor(material, "_BaseColor") ||
            HasTransparentColor(material, "_Color"))
        {
            return true;
        }

        var renderType = material.GetTag("RenderType", false, string.Empty);
        return material.renderQueue >= (int)RenderQueue.Transparent ||
               renderType.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
               renderType.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasTransparentColor(Material material, string property)
    {
        return material.HasProperty(property) && material.GetColor(property).a < 0.999f;
    }

    private static bool IsHdrpMaterial(Material material, string shaderName)
    {
        if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal) ||
            shaderName.StartsWith("High Definition Render Pipeline/", StringComparison.Ordinal))
        {
            return true;
        }

        var shaderGraphTarget = material.GetTag("ShaderGraphTargetId", false, string.Empty);
        return shaderGraphTarget.StartsWith("HD", StringComparison.Ordinal) ||
               material.HasProperty("_SupportDecals");
    }

    private static bool FixSolidHdrpMaterial(Material material)
    {
        SetOpaqueColor(material, "_BaseColor");
        SetOpaqueColor(material, "baseColorFactor");

        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_RefractionModel", 0f);
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetOverrideTag("RenderType", "Opaque");

        var validated = TryValidateHdrpMaterial(material);

        // Preserve the critical state even if HDRP validation changes in a future game build.
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        return validated;
    }

    private static void RebindToHdrpLit(Material material)
    {
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null || material.shader == hdrpLit)
            return;

        var baseColor = GetColor(material, "baseColorFactor", "_BaseColor", Color.white);
        baseColor.a = 1f;
        var baseTextureProperty = FirstTextureProperty(
            material, "baseColorTexture", "_BaseColorMap", "_MainTex");
        var baseTexture = baseTextureProperty != null ? material.GetTexture(baseTextureProperty) : null;
        var baseScale = baseTextureProperty != null ? material.GetTextureScale(baseTextureProperty) : Vector2.one;
        var baseOffset = baseTextureProperty != null ? material.GetTextureOffset(baseTextureProperty) : Vector2.zero;

        var normalTextureProperty = FirstTextureProperty(material, "normalTexture", "_NormalMap");
        var normalTexture = normalTextureProperty != null ? material.GetTexture(normalTextureProperty) : null;
        var normalScale = GetFloat(material, "normalTexture_scale", "normalScale", "_NormalScale", 1f);
        var metallic = GetFloat(material, "metallicFactor", "_Metallic", null, 0f);
        var roughness = GetFloat(material, "roughnessFactor", null, null, 1f);

        material.shader = hdrpLit;
        SetColor(material, "_BaseColor", baseColor);
        SetTexture(material, "_BaseColorMap", baseTexture, baseScale, baseOffset);
        SetTexture(material, "_NormalMap", normalTexture, Vector2.one, Vector2.zero);
        SetFloat(material, "_NormalScale", normalScale);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
    }

    private static string? FirstTextureProperty(Material material, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return property;
        }

        return null;
    }

    private static Color GetColor(
        Material material,
        string firstProperty,
        string secondProperty,
        Color fallback)
    {
        if (material.HasProperty(firstProperty))
            return material.GetColor(firstProperty);
        if (material.HasProperty(secondProperty))
            return material.GetColor(secondProperty);
        return fallback;
    }

    private static float GetFloat(
        Material material,
        string firstProperty,
        string? secondProperty,
        string? thirdProperty,
        float fallback)
    {
        if (material.HasProperty(firstProperty))
            return material.GetFloat(firstProperty);
        if (secondProperty != null && material.HasProperty(secondProperty))
            return material.GetFloat(secondProperty);
        if (thirdProperty != null && material.HasProperty(thirdProperty))
            return material.GetFloat(thirdProperty);
        return fallback;
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material.HasProperty(property))
            material.SetColor(property, value);
    }

    private static void SetTexture(
        Material material,
        string property,
        Texture? texture,
        Vector2 scale,
        Vector2 offset)
    {
        if (!material.HasProperty(property))
            return;

        material.SetTexture(property, texture);
        material.SetTextureScale(property, scale);
        material.SetTextureOffset(property, offset);
    }

    private static void SetOpaqueColor(Material material, string property)
    {
        if (!material.HasProperty(property))
            return;

        var color = material.GetColor(property);
        color.a = 1f;
        material.SetColor(property, color);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static bool TryValidateHdrpMaterial(Material material)
    {
        try
        {
            var method = ResolveValidateMaterialMethod();
            if (method != null)
            {
                var result = method.Invoke(null, new object[] { material });
                if (!(result is bool validated) || validated)
                    return true;
            }

            method = ResolveValidateShaderGraphMaterialMethod();
            if (method == null)
                return false;

            method.Invoke(null, new object[] { material });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo? ResolveValidateMaterialMethod()
    {
        if (validateMaterialMethodResolved)
            return validateMaterialMethod;

        validateMaterialMethodResolved = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(HdMaterialTypeName, false);
            if (type == null)
                continue;

            validateMaterialMethod = type.GetMethod(
                "ValidateMaterial",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null);
            if (validateMaterialMethod != null)
                break;
        }

        return validateMaterialMethod;
    }

    private static MethodInfo? ResolveValidateShaderGraphMaterialMethod()
    {
        if (validateShaderGraphMaterialMethodResolved)
            return validateShaderGraphMaterialMethod;

        validateShaderGraphMaterialMethodResolved = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(ShaderGraphApiTypeName, false);
            if (type == null)
                continue;

            validateShaderGraphMaterialMethod = type.GetMethod(
                "ValidateLightingMaterial",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null);
            if (validateShaderGraphMaterialMethod != null)
                break;
        }

        return validateShaderGraphMaterialMethod;
    }
}

internal sealed class AudiRS6RMaterialController : MonoBehaviour
{
    private bool applied;

    internal void Initialize(VehicleController vehicle, ModContext? context)
    {
        if (applied)
            return;

        var result = AudiRS6RMaterials.FixSolidVehicleMaterials(vehicle.gameObject);
        applied = true;
        context?.Logger.Info(
            $"AudiRS6R materials vehicle='{vehicle.name}' instance={vehicle.GetInstanceID()}: " +
            $"renderers={result.RendererCount} solidRenderers={result.SolidRendererCount} " +
            $"decalMasksCleared={result.DecalMasksCleared} materialsFixed={result.HdrpMaterialsFixed} " +
            $"materialsValidated={result.HdrpMaterialsValidated} shaders='{result.ShaderNames}'.");

        if (result.SolidRendererCount == 0 || result.HdrpMaterialsFixed == 0)
        {
            context?.Logger.Warn(
                $"AudiRS6R materials vehicle='{vehicle.name}' instance={vehicle.GetInstanceID()}: " +
                "no opaque HDRP vehicle materials were corrected; ground decals may still project onto the body.");
        }
    }
}
