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
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

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
                    slots.Add(new PaintSlot(renderer, material, index));
                    bodySlots++;
                }
                else if (material.name.IndexOf(CaliperMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(new PaintSlot(renderer, material, index));
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
        foreach (var slot in slots)
        {
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, selectedColor);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, selectedColor);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, selectedColor);
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
        internal PaintSlot(Renderer renderer, Material material, int materialIndex)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
    }
}
