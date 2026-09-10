#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

[AddComponentMenu("")]
internal sealed class BMWM4G82PaintController : MonoBehaviour
{
    private const string MainPaintMarker = "PaintTNR";
    private const string SecondaryPaintMarker = "Coloured";
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
    private bool initialized;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (!initialized || vehicle != controller)
        {
            vehicle = controller;
            context = modContext;
            FindPaintSlots();
            initialized = true;
        }

        ApplyCurrentColor("vehicle-configured", true);
    }

    internal bool ApplyCurrentColor(string source, bool force = false)
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return false;

        var tint = (Color32)selected.tint;
        if (!force && hasAppliedTint && ReferenceEquals(selected, appliedColor) && tint.Equals(appliedTint))
            return true;

        var color = (Color)tint;
        color.a = 1f;
        foreach (var slot in slots)
        {
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
            $"BMWM4G82 paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"slots={slots.Count} source='{source}'.");
        return slots.Count > 0;
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || !IsPaintMaterial(material.name))
                    continue;
                slots.Add(new PaintSlot(renderer, material, index));
            }
        }

        if (slots.Count == 0)
            context?.Logger.Warn(
                $"BMWM4G82 paint vehicle={vehicle?.GetInstanceID()}: no body-paint slots were found.");
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

    private static bool IsPaintMaterial(string materialName) =>
        materialName.IndexOf(MainPaintMarker, StringComparison.OrdinalIgnoreCase) >= 0 ||
        materialName.IndexOf(SecondaryPaintMarker, StringComparison.OrdinalIgnoreCase) >= 0;

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
