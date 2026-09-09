#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

internal sealed class LamborghiniRevueltoPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "_Body";
    private const string CaliperMaterialMarker = "_Caliper";
    private const string VehiclePaintShaderName = "Shader Graphs/SH_Vehicle";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private static readonly int VehicleTint =
        Shader.PropertyToID("Color_3d0f0cdbe6b74be28a1a5be5bab71dea");
    private static readonly int VehicleFresnel =
        Shader.PropertyToID("Color_f78fac473bac467092fb27521e9f71ea");
    private static readonly int VehicleFresnelPower =
        Shader.PropertyToID("Vector1_481fa2a8a5e94165a039319bfd512b76");
    private const float PaintMetallic = 0f;
    private const float PaintSmoothness = 0.68f;

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private VehicleController? vehicle;
    private ModContext? context;
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

    private void LateUpdate()
    {
        // The game exposes no vehicle-paint-changed event. This comparison is
        // allocation-free and performs material work only when the saved color changes.
        if (vehicle != null)
            ApplyCurrentColor();
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        var caliperSlots = 0;
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
                    slots.Add(new PaintSlot(renderer, material, index, true,
                        TryConfigureVehiclePaintShader(material)));
                    bodySlots++;
                }
                else if (material.name.IndexOf(CaliperMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(new PaintSlot(renderer, material, index, false, false));
                    caliperSlots++;
                }
            }
        }

        context?.Logger.Info(
            $"LamborghiniRevuelto paint vehicle={vehicle?.GetInstanceID()}: " +
            $"mapped bodySlots={bodySlots}, caliperSlots={caliperSlots}; " +
            "wheels, carbon, trim, glass, and interior remain factory materials.");
        if (bodySlots == 0 || caliperSlots != 4)
            context?.Logger.Warn(
                $"LamborghiniRevuelto paint mapping incomplete bodySlots={bodySlots}, " +
                $"caliperSlots={caliperSlots}.");
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
        var fresnelColor = selected.fresnelColor;
        fresnelColor.a = byte.MaxValue;
        foreach (var slot in slots)
        {
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.UsesVehiclePaintShader)
            {
                properties.SetColor(VehicleTint, selectedColor);
                properties.SetColor(VehicleFresnel, fresnelColor);
                properties.SetFloat(VehicleFresnelPower, selected.fresnelPower);
            }
            else
            {
                var color = selectedColor.linear;
                color.a = 1f;
                if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
                if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
                if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            }
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        context?.Logger.Info(
            $"LamborghiniRevuelto paint vehicle={vehicle?.GetInstanceID()}: " +
            $"applied color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"to {slots.Count} body/caliper slots.");
    }

    private static bool TryConfigureVehiclePaintShader(Material material)
    {
        var shader = Shader.Find(VehiclePaintShaderName);
        if (shader == null)
            return false;
        var fallback = material.shader;
        material.shader = shader;
        if (!material.HasProperty(VehicleTint) || !material.HasProperty(VehicleFresnel) ||
            !material.HasProperty(VehicleFresnelPower))
        {
            material.shader = fallback;
            return false;
        }
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", PaintMetallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", PaintSmoothness);
        if (material.HasProperty("_DoubleSidedEnable")) material.SetFloat("_DoubleSidedEnable", 0f);
        if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", 2f);
        if (material.HasProperty("_CullModeForward")) material.SetFloat("_CullModeForward", 2f);
        if (material.HasProperty("_OpaqueCullMode")) material.SetFloat("_OpaqueCullMode", 2f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
        if (material.HasProperty("_SupportDecals")) material.SetFloat("_SupportDecals", 0f);
        material.doubleSidedGI = false;
        material.DisableKeyword("_DOUBLESIDED_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = 2225;
        return true;
    }

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
        internal PaintSlot(Renderer renderer, Material material, int materialIndex,
            bool isBody, bool usesVehiclePaintShader)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
            IsBody = isBody;
            UsesVehiclePaintShader = usesVehiclePaintShader;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly bool IsBody;
        internal readonly bool UsesVehiclePaintShader;
    }
}
