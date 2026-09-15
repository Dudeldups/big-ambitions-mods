#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

internal sealed class KoenigseggJeskoPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "Exterior_mm_ext1";
    private const string CaliperMaterialMarker = "_Caliper";
    private const string InteriorAccentMaterialMarker = "Tela_Int";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private readonly Dictionary<Material, CaliperPaintTexture> caliperTextures =
        new Dictionary<Material, CaliperPaintTexture>();
    private readonly Dictionary<Material, ExteriorContrastTexture> bodyPaintTextures =
        new Dictionary<Material, ExteriorContrastTexture>();
    private readonly Dictionary<Material, ExteriorContrastTexture> exteriorContrastTextures =
        new Dictionary<Material, ExteriorContrastTexture>();
    private VehicleController? vehicle;
    private ModContext? context;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private VehicleColor? appliedColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        FindPaintSlots();
        ApplyCurrentColor();
    }

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = vehicleColorName;
        explicitVehicleColor = vehicleColor;
        appliedColor = null;
        hasAppliedTint = false;
        FindPaintSlots();
        ApplyCurrentColor();
    }

    internal bool HasAppliedColor => hasAppliedTint;

    private void LateUpdate()
    {
        // The game exposes no vehicle-paint-changed event. This comparison is
        // allocation-free and performs material work only when the saved color changes.
        if (vehicle != null)
            ApplyCurrentColor();
    }

    private void FindPaintSlots()
    {
        if (slots.Count > 0)
            return;

        ReleaseRuntimePaintTextures();
        slots.Clear();
        var bodySlots = 0;
        var caliperSlots = 0;
        var exteriorContrastSlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                if (material.name.IndexOf(BodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var bodyTexture = GetOrCreateBodyPaintTexture(renderer, index, material);
                    slots.Add(new PaintSlot(
                        renderer,
                        bodyTexture?.Material ?? material,
                        index,
                        PaintCategory.Body,
                        bodyPaintTexture: bodyTexture));
                    bodySlots++;
                }
                else if (IsCaliperRenderer(renderer.transform))
                {
                    var caliperTexture = GetOrCreateCaliperPaintTexture(
                        renderer, index, material);
                    slots.Add(new PaintSlot(
                        renderer,
                        caliperTexture?.Material ?? material,
                        index,
                        PaintCategory.Caliper,
                        caliperTexture));
                    caliperSlots++;
                }
                else if (IsExteriorContrastAccentRenderer(renderer, material))
                {
                    var contrastTexture = GetOrCreateExteriorContrastTexture(
                        renderer, index, material);
                    if (contrastTexture != null)
                    {
                        slots.Add(new PaintSlot(
                            renderer,
                            contrastTexture.Material,
                            index,
                            PaintCategory.ExteriorContrastAccent,
                            exteriorContrastTexture: contrastTexture));
                        exteriorContrastSlots++;
                    }
                }
                else if (material.name.IndexOf(
                             InteriorAccentMaterialMarker,
                             StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(new PaintSlot(renderer, material, index, PaintCategory.InteriorAccent));
                    interiorAccentSlots++;
                }
            }
        }

        context?.Logger.Info(
            $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: " +
            $"mapped bodySlots={bodySlots}, caliperSlots={caliperSlots}, " +
            $"exteriorContrastSlots={exteriorContrastSlots}, " +
            $"interiorAccentSlots={interiorAccentSlots}; " +
            "rims, tires, carbon, black trim, and glass remain factory materials.");
        if (bodySlots == 0 || caliperSlots != 4 || exteriorContrastSlots == 0 ||
            interiorAccentSlots == 0)
            context?.Logger.Warn(
                $"KoenigseggJesko paint mapping incomplete bodySlots={bodySlots}, " +
                $"caliperSlots={caliperSlots}, exteriorContrastSlots={exteriorContrastSlots}, " +
                $"interiorAccentSlots={interiorAccentSlots}.");
    }

    private void ApplyCurrentColor()
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return;
        var tint = (Color32)selected.tint;
        if (hasAppliedTint && ReferenceEquals(selected, appliedColor) && tint.Equals(appliedTint))
            return;

        var selectedColor = (Color)tint;
        selectedColor.a = 1f;
        foreach (var slot in slots)
        {
            if (slot.Category == PaintCategory.Body && slot.BodyPaintTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                slot.BodyPaintTexture.Apply(selectedColor);
                continue;
            }
            if (slot.Category == PaintCategory.Caliper && slot.CaliperTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                slot.CaliperTexture.Apply(selectedColor);
                continue;
            }
            if (slot.Category == PaintCategory.ExteriorContrastAccent &&
                slot.ExteriorContrastTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                slot.ExteriorContrastTexture.Apply(selectedColor);
                continue;
            }

            // Match the Bugatti's interior hierarchy: accent upholstery and trim
            // follow the selected paint while staying slightly lighter than the
            // body. Rims are intentionally not registered as paint slots.
            var color = slot.Category == PaintCategory.InteriorAccent
                ? Color.Lerp(selectedColor, Color.white, 0.12f)
                : selectedColor;
            color.a = 1f;
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        context?.Logger.Info(
            $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: " +
            $"applied color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"to {slots.Count} body/caliper slots.");
    }

    private CaliperPaintTexture? GetOrCreateCaliperPaintTexture(
        Renderer renderer,
        int materialIndex,
        Material sourceMaterial)
    {
        if (!caliperTextures.TryGetValue(sourceMaterial, out var state))
        {
            state = CaliperPaintTexture.Create(sourceMaterial);
            if (state == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: caliper " +
                    $"texture unavailable material='{sourceMaterial.name}'; using flat paint fallback.");
                return null;
            }
            caliperTextures.Add(sourceMaterial, state);
        }

        var materials = renderer.sharedMaterials;
        materials[materialIndex] = state.Material;
        renderer.sharedMaterials = materials;
        return state;
    }

    private static bool IsCaliperRenderer(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("KoenigseggFixedCaliper", StringComparison.Ordinal) ||
                current.name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private ExteriorContrastTexture? GetOrCreateBodyPaintTexture(
        Renderer renderer,
        int materialIndex,
        Material sourceMaterial)
    {
        if (!bodyPaintTextures.TryGetValue(sourceMaterial, out var state))
        {
            state = ExteriorContrastTexture.Create(sourceMaterial, tintNonAccentPixels: true);
            if (state == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: body " +
                    $"texture unavailable material='{sourceMaterial.name}'; using property-block fallback.");
                return null;
            }
            bodyPaintTextures.Add(sourceMaterial, state);
        }

        var materials = renderer.sharedMaterials;
        materials[materialIndex] = state.Material;
        renderer.sharedMaterials = materials;
        return state;
    }

    private ExteriorContrastTexture? GetOrCreateExteriorContrastTexture(
        Renderer renderer,
        int materialIndex,
        Material sourceMaterial)
    {
        if (!exteriorContrastTextures.TryGetValue(sourceMaterial, out var state))
        {
            state = ExteriorContrastTexture.Create(sourceMaterial, tintNonAccentPixels: false);
            if (state == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko paint vehicle={vehicle?.GetInstanceID()}: exterior " +
                    $"contrast texture unavailable material='{sourceMaterial.name}'.");
                return null;
            }
            exteriorContrastTextures.Add(sourceMaterial, state);
        }

        var materials = renderer.sharedMaterials;
        materials[materialIndex] = state.Material;
        renderer.sharedMaterials = materials;
        return state;
    }

    private static bool IsExteriorContrastAccentRenderer(Renderer renderer, Material material)
    {
        var rendererName = renderer.name;
        var supportedMaterial =
            material.name.IndexOf("Exterior_mm_misc1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            material.name.IndexOf("CARBON", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!supportedMaterial)
            return false;

        return rendererName.IndexOf("BODY_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("BOOT_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("HOOD_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("DOOR_LEFT_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("DOOR_RIGHT_mm_misc", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("FRONTBUMPER_mm_misc_CARBON", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("REARBUMPER_mm_misc_CARBON", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ReleaseRuntimePaintTextures()
    {
        var released = new HashSet<CaliperPaintTexture>();
        foreach (var state in caliperTextures.Values)
        {
            if (state != null && released.Add(state))
                state.Dispose();
        }
        caliperTextures.Clear();

        foreach (var state in bodyPaintTextures.Values)
            state?.Dispose();
        bodyPaintTextures.Clear();

        foreach (var state in exteriorContrastTextures.Values)
            state?.Dispose();
        exteriorContrastTextures.Clear();
    }

    private void OnDestroy() => ReleaseRuntimePaintTextures();

    private VehicleColor? ResolveVehicleColor()
    {
        if (explicitVehicleColor != null &&
            (string.IsNullOrEmpty(explicitVehicleColorName) ||
             string.Equals(explicitVehicleColor.name, explicitVehicleColorName,
                 StringComparison.Ordinal)))
            return explicitVehicleColor;
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null)
            return live;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        return !string.IsNullOrEmpty(colorName) && VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(
            Renderer renderer,
            Material material,
            int materialIndex,
            PaintCategory category,
            CaliperPaintTexture? caliperTexture = null,
            ExteriorContrastTexture? bodyPaintTexture = null,
            ExteriorContrastTexture? exteriorContrastTexture = null)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
            Category = category;
            CaliperTexture = caliperTexture;
            BodyPaintTexture = bodyPaintTexture;
            ExteriorContrastTexture = exteriorContrastTexture;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly PaintCategory Category;
        internal readonly CaliperPaintTexture? CaliperTexture;
        internal readonly ExteriorContrastTexture? BodyPaintTexture;
        internal readonly ExteriorContrastTexture? ExteriorContrastTexture;
    }

    private enum PaintCategory
    {
        Body,
        Caliper,
        ExteriorContrastAccent,
        InteriorAccent,
    }

    private sealed class CaliperPaintTexture
    {
        private readonly Color32[] sourcePixels;
        private readonly Texture2D texture;

        private CaliperPaintTexture(Material material, Texture2D texture, Color32[] sourcePixels)
        {
            Material = material;
            this.texture = texture;
            this.sourcePixels = sourcePixels;
        }

        internal Material Material { get; }

        internal static CaliperPaintTexture? Create(Material sourceMaterial)
        {
            var sourceTexture = FindBaseTexture(sourceMaterial);
            if (sourceTexture == null)
                return null;

            var width = sourceTexture.width;
            var height = sourceTexture.height;
            var temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D readable;
            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
                {
                    name = sourceTexture.name + "_RuntimeCaliperPaint",
                    hideFlags = HideFlags.DontSave,
                    filterMode = sourceTexture.filterMode,
                    wrapMode = sourceTexture.wrapMode,
                    anisoLevel = sourceTexture.anisoLevel,
                };
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(true, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            var runtimeMaterial = UnityEngine.Object.Instantiate(sourceMaterial);
            runtimeMaterial.name = sourceMaterial.name + "_RuntimeCaliperPaint";
            runtimeMaterial.hideFlags = HideFlags.DontSave;
            SetTexture(runtimeMaterial, "_BaseColorMap", readable);
            SetTexture(runtimeMaterial, "_MainTex", readable);
            SetTexture(runtimeMaterial, "baseColorTexture", readable);
            SetMaterialColor(runtimeMaterial, Color.white);
            return new CaliperPaintTexture(
                runtimeMaterial,
                readable,
                readable.GetPixels32());
        }

        internal void Apply(Color paint)
        {
            var linear = paint.linear;
            var luminance = linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
            var text = luminance >= 0.42f ? Color.black : Color.white;
            var output = new Color32[sourcePixels.Length];
            for (var index = 0; index < sourcePixels.Length; index++)
            {
                var source = (Color)sourcePixels[index];
                if (source.a <= 0.01f)
                {
                    output[index] = sourcePixels[index];
                    continue;
                }

                Color.RGBToHSV(source, out _, out var saturation, out var value);
                var greenBody = source.g > source.r * 1.08f &&
                                source.g > source.b * 1.08f &&
                                saturation > 0.18f;
                var lettering = saturation < 0.22f && value > 0.38f;
                Color recolored;
                if (greenBody)
                {
                    var shade = Mathf.Lerp(0.38f, 1.12f, value);
                    recolored = new Color(
                        Mathf.Clamp01(paint.r * shade),
                        Mathf.Clamp01(paint.g * shade),
                        Mathf.Clamp01(paint.b * shade),
                        source.a);
                }
                else if (lettering)
                {
                    var shade = Mathf.Lerp(0.72f, 1f, value);
                    recolored = new Color(text.r * shade, text.g * shade, text.b * shade, source.a);
                }
                else
                {
                    recolored = source;
                }
                output[index] = recolored;
            }

            texture.SetPixels32(output);
            texture.Apply(true, false);
            SetMaterialColor(Material, Color.white);
        }

        internal void Dispose()
        {
            if (Material != null) UnityEngine.Object.Destroy(Material);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        private static Texture? FindBaseTexture(Material material)
        {
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                return material.GetTexture("_BaseColorMap");
            if (material.HasProperty("baseColorTexture") && material.GetTexture("baseColorTexture") != null)
                return material.GetTexture("baseColorTexture");
            return material.mainTexture;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property)) material.SetTexture(property, texture);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
            if (material.HasProperty(ColorProperty)) material.SetColor(ColorProperty, color);
            if (material.HasProperty(BaseColorFactor)) material.SetColor(BaseColorFactor, color);
        }
    }

    private sealed class ExteriorContrastTexture
    {
        private readonly Color32[] sourcePixels;
        private readonly Texture2D texture;
        private readonly bool tintNonAccentPixels;

        private ExteriorContrastTexture(
            Material material,
            Texture2D texture,
            Color32[] sourcePixels,
            bool tintNonAccentPixels)
        {
            Material = material;
            this.texture = texture;
            this.sourcePixels = sourcePixels;
            this.tintNonAccentPixels = tintNonAccentPixels;
        }

        internal Material Material { get; }

        internal static ExteriorContrastTexture? Create(
            Material sourceMaterial,
            bool tintNonAccentPixels)
        {
            var sourceTexture = FindBaseTexture(sourceMaterial);
            if (sourceTexture == null)
                return null;

            var temporary = RenderTexture.GetTemporary(
                sourceTexture.width,
                sourceTexture.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D readable;
            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(
                    sourceTexture.width,
                    sourceTexture.height,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name = sourceTexture.name +
                           (tintNonAccentPixels
                               ? "_RuntimeBodyContrast"
                               : "_RuntimeExteriorContrast"),
                    hideFlags = HideFlags.DontSave,
                    filterMode = sourceTexture.filterMode,
                    wrapMode = sourceTexture.wrapMode,
                    anisoLevel = sourceTexture.anisoLevel,
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, sourceTexture.width, sourceTexture.height),
                    0,
                    0,
                    false);
                readable.Apply(true, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            var runtimeMaterial = UnityEngine.Object.Instantiate(sourceMaterial);
            runtimeMaterial.name = sourceMaterial.name +
                                   (tintNonAccentPixels
                                       ? "_RuntimeBodyContrast"
                                       : "_RuntimeExteriorContrast");
            runtimeMaterial.hideFlags = HideFlags.DontSave;
            SetTexture(runtimeMaterial, "_BaseColorMap", readable);
            SetTexture(runtimeMaterial, "_MainTex", readable);
            SetTexture(runtimeMaterial, "baseColorTexture", readable);
            SetMaterialColor(runtimeMaterial, Color.white);
            return new ExteriorContrastTexture(
                runtimeMaterial,
                readable,
                readable.GetPixels32(),
                tintNonAccentPixels);
        }

        internal void Apply(Color paint)
        {
            var linear = paint.linear;
            var luminance = linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
            var contrast = luminance >= 0.42f ? Color.black : Color.white;
            var output = new Color32[sourcePixels.Length];
            for (var index = 0; index < sourcePixels.Length; index++)
            {
                var source = (Color)sourcePixels[index];
                Color.RGBToHSV(source, out var hue, out var saturation, out var value);
                var yellowGreenAccent = source.a > 0.01f &&
                                        hue >= 0.14f && hue <= 0.38f &&
                                        saturation > 0.30f &&
                                        value > 0.18f;
                if (!yellowGreenAccent)
                {
                    output[index] = tintNonAccentPixels
                        ? new Color(
                            source.r * paint.r,
                            source.g * paint.g,
                            source.b * paint.b,
                            source.a)
                        : sourcePixels[index];
                    continue;
                }

                var shade = contrast == Color.white
                    ? Mathf.Lerp(0.72f, 1f, value)
                    : Mathf.Lerp(0.015f, 0.07f, value);
                output[index] = new Color(
                    contrast.r * shade,
                    contrast.g * shade,
                    contrast.b * shade,
                    source.a);
            }

            texture.SetPixels32(output);
            texture.Apply(true, false);
            SetMaterialColor(Material, Color.white);
        }

        internal void Dispose()
        {
            if (Material != null) UnityEngine.Object.Destroy(Material);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        private static Texture? FindBaseTexture(Material material)
        {
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                return material.GetTexture("_BaseColorMap");
            if (material.HasProperty("baseColorTexture") && material.GetTexture("baseColorTexture") != null)
                return material.GetTexture("baseColorTexture");
            return material.mainTexture;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property)) material.SetTexture(property, texture);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
            if (material.HasProperty(ColorProperty)) material.SetColor(ColorProperty, color);
            if (material.HasProperty(BaseColorFactor)) material.SetColor(BaseColorFactor, color);
        }
    }
}

