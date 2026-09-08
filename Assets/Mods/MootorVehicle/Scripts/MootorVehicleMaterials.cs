#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

namespace MootorVehicle
{
    internal readonly struct CowMaterialFixResult
    {
        internal CowMaterialFixResult(
            int rendererCount,
            int decalMasksCleared,
            int hdrpLitMaterialsFixed,
            int hdrpLitMaterialsValidated,
            string shaderNames)
        {
            RendererCount = rendererCount;
            DecalMasksCleared = decalMasksCleared;
            HdrpLitMaterialsFixed = hdrpLitMaterialsFixed;
            HdrpLitMaterialsValidated = hdrpLitMaterialsValidated;
            ShaderNames = shaderNames;
        }

        internal int RendererCount { get; }
        internal int DecalMasksCleared { get; }
        internal int HdrpLitMaterialsFixed { get; }
        internal int HdrpLitMaterialsValidated { get; }
        internal string ShaderNames { get; }
    }

    internal static class MootorVehicleMaterials
    {
        private const uint HdrpDecalLayerMask = 0x0000FF00u;
        private const string HdrpLitShaderName = "HDRP/Lit";
        private const string HdMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
        private static MethodInfo? validateMaterialMethod;
        private static bool validateMaterialMethodResolved;

        internal static CowMaterialFixResult FixSolidCowMaterials(GameObject cowVisual)
        {
            var renderers = cowVisual.GetComponentsInChildren<Renderer>(true);
            var materials = new HashSet<Material>();
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            var decalMasksCleared = 0;
            var hdrpMaterialsFixed = 0;
            var hdrpMaterialsValidated = 0;

            foreach (var renderer in renderers)
            {
                var previousMask = renderer.renderingLayerMask;
                renderer.renderingLayerMask &= ~HdrpDecalLayerMask;
                if (renderer.renderingLayerMask != previousMask)
                    decalMasksCleared++;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !materials.Add(material))
                        continue;

                    var shaderName = material.shader != null ? material.shader.name : "<null>";
                    shaderNames.Add(shaderName);
                    if (!string.Equals(shaderName, HdrpLitShaderName, StringComparison.Ordinal))
                        continue;

                    if (FixSolidHdrpLitMaterial(material))
                        hdrpMaterialsValidated++;

                    hdrpMaterialsFixed++;
                }
            }

            var orderedShaderNames = new List<string>(shaderNames);
            orderedShaderNames.Sort(StringComparer.Ordinal);
            return new CowMaterialFixResult(
                renderers.Length,
                decalMasksCleared,
                hdrpMaterialsFixed,
                hdrpMaterialsValidated,
                string.Join("|", orderedShaderNames));
        }

        private static bool FixSolidHdrpLitMaterial(Material material)
        {
            var color = material.GetColor("_BaseColor");
            color.a = 1f;
            material.SetColor("_BaseColor", color);

            material.SetFloat("_SurfaceType", 0f);
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.SetFloat("_SupportDecals", 0f);
            material.SetFloat("_ReceivesSSR", 0f);
            material.SetFloat("_ReceivesSSRTransparent", 0f);
            material.SetFloat("_RefractionModel", 0f);
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");

            var validated = TryValidateHdrpMaterial(material);

            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            return validated;
        }

        private static bool TryValidateHdrpMaterial(Material material)
        {
            try
            {
                var method = ResolveValidateMaterialMethod();
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
    }

    internal sealed class MootorVehicleMaterialController : MonoBehaviour
    {
        private const string CowVisualName = "MootorVehicle_CowVisual";
        private bool applied;
        private bool missingVisualLogged;

        internal void Initialize(VehicleController vehicle, ModContext? context)
        {
            if (applied)
                return;

            Transform? cowVisual = null;
            foreach (var child in vehicle.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, CowVisualName, StringComparison.Ordinal))
                {
                    cowVisual = child;
                    break;
                }

            if (cowVisual == null)
            {
                if (!missingVisualLogged)
                {
                    missingVisualLogged = true;
                    context?.Logger.Warn(
                        $"Moo-tor Vehicle materials vehicle={vehicle.GetInstanceID()}: cow visual was not found.");
                }

                return;
            }

            // Asset loading can remap shaders for the active render pipeline. Reapply the complete
            // opaque HDRP state here even when the material already reports the expected shader.
            var result = MootorVehicleMaterials.FixSolidCowMaterials(cowVisual.gameObject);
            applied = true;
            context?.Logger.Info(
                $"Moo-tor Vehicle materials vehicle={vehicle.GetInstanceID()}: " +
                $"renderers={result.RendererCount} decalMasksCleared={result.DecalMasksCleared} " +
                $"hdrpLitFixed={result.HdrpLitMaterialsFixed} " +
                $"hdrpLitValidated={result.HdrpLitMaterialsValidated} shaders='{result.ShaderNames}'.");
            if (result.HdrpLitMaterialsValidated < result.HdrpLitMaterialsFixed)
                context?.Logger.Warn(
                    $"Moo-tor Vehicle materials vehicle={vehicle.GetInstanceID()}: " +
                    $"HDRP validation was unavailable for " +
                    $"{result.HdrpLitMaterialsFixed - result.HdrpLitMaterialsValidated} material(s).");
        }
    }
}
