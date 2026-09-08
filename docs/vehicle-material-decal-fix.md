# Preventing road decals on vehicle materials

## Symptom

Road markings, manhole covers, and similar ground details appear across a vehicle body, making it look transparent. This is usually an HDRP decal being projected onto the material, not an opacity problem.

## Required material setup

Apply this only to opaque body materials. Do not apply it to glass, alpha-cutout details, or particles.

Use `HDRP/Lit`. Some imported glTF shader graphs continue to receive decals even after `_SupportDecals` is disabled and their HDRP state is validated. For those materials, first preserve the source color and textures, change the shader, and restore them:

```csharp
var color = material.HasProperty("baseColorFactor")
    ? material.GetColor("baseColorFactor")
    : Color.white;
var baseMap = material.HasProperty("baseColorTexture")
    ? material.GetTexture("baseColorTexture")
    : null;
var normalMap = material.HasProperty("normalTexture")
    ? material.GetTexture("normalTexture")
    : null;
var roughness = material.HasProperty("roughnessFactor")
    ? material.GetFloat("roughnessFactor")
    : 1f;

var hdrpLit = Shader.Find("HDRP/Lit")
    ?? Shader.Find("High Definition Render Pipeline/Lit");
if (hdrpLit == null)
    return;
material.shader = hdrpLit;
material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
material.SetTexture("_BaseColorMap", baseMap);
material.SetTexture("_NormalMap", normalMap);
material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(roughness));
```

Then configure and validate the material:

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

static void FixSolidVehicleMaterial(Material material)
{
    if (material == null || material.shader.name != "HDRP/Lit")
        return;

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

    // Rebuild HDRP keywords, passes, and stencil state.
    HDMaterial.ValidateMaterial(material);

    material.EnableKeyword("_DISABLE_DECALS");
    material.SetFloat("_ZWrite", 1f);
    material.SetFloat("_SrcBlend", (float)BlendMode.One);
    material.SetFloat("_DstBlend", (float)BlendMode.Zero);
}
```

Also remove decal-layer bits from each solid-body renderer:

```csharp
renderer.renderingLayerMask &= ~0x0000FF00u;
```

Apply the setup before building the AssetBundle and again after any runtime shader remapping. Rebuild the bundle, reinstall the mod, and test over road markings and manhole covers. Changing opacity alone is not sufficient.

Do not assign a glTF metallic-roughness texture directly to HDRP's `_MaskMap`; the channel packing differs and must be converted first.

`MootorVehicleMaterials.cs` and `MootorVehicleAssetBuilder.cs` contain a runtime-safe implementation that uses reflection when the mod assembly cannot directly reference HDRP APIs.
