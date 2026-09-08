#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

[DefaultExecutionOrder(150)]
internal sealed class BugattiChironPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "BugattiOpaque_04_Body";
    private const string RimMaterialMarker = "BugattiOpaque_01_Rims";
    private const string DarkBodyMaterialMarker = "BugattiOpaque_06_Darker_Parts";
    private const string InteriorSecondaryMaterialMarker = "BugattiOpaque_08_Interior_2";
    private const string InteriorDarkMaterialMarker = "BugattiOpaque_12_Interior_1_Darker";
    private const string InteriorPrimaryMaterialMarker = "BugattiOpaque_13_Interior_1";
    private const string CaliperMaterialMarker = "BugattiOpaque_02_Caliper";
    private const string SeatMaterialMarker = "BugattiOpaque_19_seats";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorTexture = Shader.PropertyToID("baseColorTexture");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private VehicleController? vehicle;
    private ModContext? context;
    private VehicleColor? appliedVehicleColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private Texture2D? rimSourceTexture;
    private Texture2D? seatSourceTexture;
    private Texture2D? rimPaintTexture;
    private Texture2D? seatPaintTexture;
    private bool textureFailureLogged;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        FindPaintSlots();
        ApplyCurrentColor();
    }

    private void LateUpdate()
    {
        if (vehicle != null)
            ApplyCurrentColor();
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        var darkBodySlots = 0;
        var rimSlots = 0;
        var interiorSlots = 0;
        var caliperSlots = 0;
        var seatSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                var category = GetCategory(material);
                if (category == PaintCategory.None)
                    continue;
                slots.Add(new PaintSlot(renderer, material, index, category));
                if (category == PaintCategory.MainBody) bodySlots++;
                if (category == PaintCategory.DarkBody) darkBodySlots++;
                if (category == PaintCategory.Rim) rimSlots++;
                if (category == PaintCategory.Caliper) caliperSlots++;
                if (category == PaintCategory.Seat) seatSlots++;
                if (category == PaintCategory.InteriorPrimary ||
                    category == PaintCategory.InteriorSecondary ||
                    category == PaintCategory.InteriorDark)
                    interiorSlots++;
                if (category == PaintCategory.Rim && rimSourceTexture == null)
                    rimSourceTexture = FindBaseTexture(material);
                if (category == PaintCategory.Seat && seatSourceTexture == null)
                    seatSourceTexture = FindBaseTexture(material);
            }
        }

        context?.Logger.Info(
            $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
            $"mapped bodySlots={bodySlots}, darkBodySlots={darkBodySlots}, " +
            $"rimSlots={rimSlots}, caliperSlots={caliperSlots}, seatSlots={seatSlots}, " +
            $"interiorSlots={interiorSlots}; chrome/black excluded.");
        if (bodySlots == 0 || darkBodySlots == 0 || rimSlots == 0 ||
            caliperSlots == 0 || seatSlots == 0 || interiorSlots == 0)
            context?.Logger.Warn(
                $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
                $"paint mapping incomplete bodySlots={bodySlots}, darkBodySlots={darkBodySlots}, " +
                $"rimSlots={rimSlots}, caliperSlots={caliperSlots}, seatSlots={seatSlots}, " +
                $"interiorSlots={interiorSlots}.");
    }

    private void ApplyCurrentColor()
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return;
        var tint = (Color32)selected.tint;
        if (hasAppliedTint && ReferenceEquals(selected, appliedVehicleColor) && tint.Equals(appliedTint))
            return;

        var selectedColor = (Color)tint;
        selectedColor.a = 1f;
        var mainColor = selectedColor.linear;
        mainColor.a = 1f;
        RebuildPaintTextures(tint, selectedColor, mainColor);
        foreach (var slot in slots)
        {
            var color = ColorForCategory(slot.Category, selectedColor, mainColor);
            if (slot.Category == PaintCategory.Seat && seatPaintTexture == null)
                color = mainColor;
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            var texture = slot.Category == PaintCategory.Rim
                ? rimPaintTexture
                : slot.Category == PaintCategory.Seat
                    ? seatPaintTexture
                    : null;
            if (texture != null)
            {
                if (slot.Material.HasProperty(BaseColorMap)) properties.SetTexture(BaseColorMap, texture);
                if (slot.Material.HasProperty(MainTexture)) properties.SetTexture(MainTexture, texture);
                if (slot.Material.HasProperty(BaseColorTexture))
                    properties.SetTexture(BaseColorTexture, texture);
            }
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedVehicleColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        context?.Logger.Info(
            $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
            $"applied color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"to {slots.Count} body/rim/caliper/seat/interior slots.");
    }

    private static PaintCategory GetCategory(Material material)
    {
        var name = material.name;
        if (name.IndexOf(BodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.MainBody;
        if (name.IndexOf(DarkBodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.DarkBody;
        if (name.IndexOf(RimMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.Rim;
        if (name.IndexOf(CaliperMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.Caliper;
        if (name.IndexOf(SeatMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.Seat;
        if (name.IndexOf(InteriorDarkMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.InteriorDark;
        if (name.IndexOf(InteriorPrimaryMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.InteriorPrimary;
        if (name.IndexOf(InteriorSecondaryMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.InteriorSecondary;
        return PaintCategory.None;
    }

    private static Color ColorForCategory(PaintCategory category, Color selectedColor, Color mainColor)
    {
        switch (category)
        {
            // VehicleColor stores sRGB bytes. HDRP Lit expects a linear base color,
            // while the textured rim material already produced the intended in-game shade.
            case PaintCategory.MainBody:
                return mainColor;
            case PaintCategory.DarkBody:
                return Scale(mainColor, 0.22f);
            case PaintCategory.Rim:
                return selectedColor;
            case PaintCategory.Caliper:
                return mainColor;
            case PaintCategory.Seat:
                return Color.white;
            case PaintCategory.InteriorPrimary:
                return Color.Lerp(mainColor, Color.white, 0.12f);
            case PaintCategory.InteriorSecondary:
                return Scale(mainColor, 0.55f);
            case PaintCategory.InteriorDark:
                return Scale(mainColor, 0.28f);
            default:
                return mainColor;
        }
    }

    private static Color Scale(Color color, float factor) =>
        new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

    private void RebuildPaintTextures(Color32 tint, Color selectedColor, Color mainColor)
    {
        DestroyPaintTextures();
        try
        {
            if (rimSourceTexture != null)
                rimPaintTexture = CreateRimPaintTexture(rimSourceTexture, selectedColor, mainColor);
            if (seatSourceTexture != null)
                seatPaintTexture = CreateSeatPaintTexture(seatSourceTexture, mainColor);
            textureFailureLogged = false;
        }
        catch (Exception exception)
        {
            DestroyPaintTextures();
            if (!textureFailureLogged)
            {
                textureFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: textured paint mapping " +
                    $"failed for rgba={tint}; using solid tint fallback: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static Texture2D CreateRimPaintTexture(
        Texture2D source,
        Color selectedColor,
        Color mainColor)
    {
        var pixels = source.GetPixels32();
        var desiredDark = Scale(mainColor, 0.22f);
        var darkFactorLinear = new Color(
            selectedColor.r > 0.001f ? desiredDark.r / selectedColor.r : 0.22f,
            selectedColor.g > 0.001f ? desiredDark.g / selectedColor.g : 0.22f,
            selectedColor.b > 0.001f ? desiredDark.b / selectedColor.b : 0.22f,
            1f);
        darkFactorLinear.r = Mathf.Clamp01(darkFactorLinear.r);
        darkFactorLinear.g = Mathf.Clamp01(darkFactorLinear.g);
        darkFactorLinear.b = Mathf.Clamp01(darkFactorLinear.b);
        var darkMapPixel = (Color32)darkFactorLinear.gamma;
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            if (Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) <= 28)
            {
                darkMapPixel.a = pixel.a;
                pixels[index] = darkMapPixel;
            }
        }

        return CreateRuntimeTexture(source, pixels, "BugattiChiron_RimPaint");
    }

    private static Texture2D CreateSeatPaintTexture(Texture2D source, Color mainColor)
    {
        var pixels = source.GetPixels32();
        const float brightSeatLinear = 0.90f;
        for (var index = 0; index < pixels.Length; index++)
        {
            var sourcePixel = pixels[index];
            var brightest = Math.Max(sourcePixel.r, Math.Max(sourcePixel.g, sourcePixel.b)) / 255f;
            var shade = Mathf.Clamp01(Mathf.GammaToLinearSpace(brightest) / brightSeatLinear);
            var output = new Color(
                mainColor.r * shade,
                mainColor.g * shade,
                mainColor.b * shade,
                sourcePixel.a / 255f).gamma;
            output.a = sourcePixel.a / 255f;
            pixels[index] = (Color32)output;
        }

        return CreateRuntimeTexture(source, pixels, "BugattiChiron_SeatPaint");
    }

    private static Texture2D CreateRuntimeTexture(Texture2D source, Color32[] pixels, string name)
    {
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false)
        {
            name = name,
            hideFlags = HideFlags.DontSave,
            filterMode = source.filterMode,
            wrapModeU = source.wrapModeU,
            wrapModeV = source.wrapModeV,
            anisoLevel = source.anisoLevel,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static Texture2D? FindBaseTexture(Material material)
    {
        if (material.HasProperty(BaseColorMap) && material.GetTexture(BaseColorMap) is Texture2D hdrp)
            return hdrp;
        if (material.HasProperty(MainTexture) && material.GetTexture(MainTexture) is Texture2D legacy)
            return legacy;
        return material.HasProperty(BaseColorTexture)
            ? material.GetTexture(BaseColorTexture) as Texture2D
            : null;
    }

    private void DestroyPaintTextures()
    {
        if (rimPaintTexture != null)
            Destroy(rimPaintTexture);
        if (seatPaintTexture != null)
            Destroy(seatPaintTexture);
        rimPaintTexture = null;
        seatPaintTexture = null;
    }

    private void OnDestroy() => DestroyPaintTextures();

    private VehicleColor? ResolveVehicleColor()
    {
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
        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly PaintCategory Category;

        internal PaintSlot(
            Renderer renderer,
            Material material,
            int materialIndex,
            PaintCategory category)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
            Category = category;
        }
    }

    private enum PaintCategory
    {
        None,
        MainBody,
        DarkBody,
        Rim,
        Caliper,
        Seat,
        InteriorPrimary,
        InteriorSecondary,
        InteriorDark,
    }
}
