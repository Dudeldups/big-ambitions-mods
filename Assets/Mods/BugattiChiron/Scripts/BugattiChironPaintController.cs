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
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private VehicleController? vehicle;
    private ModContext? context;
    private VehicleColor? appliedVehicleColor;
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
        if (vehicle != null)
            ApplyCurrentColor();
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        var rimSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                var isBody = material.name.IndexOf(BodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                var isRim = material.name.IndexOf(RimMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isBody && !isRim)
                    continue;
                slots.Add(new PaintSlot(renderer, material, index));
                if (isBody) bodySlots++;
                if (isRim) rimSlots++;
            }
        }

        context?.Logger.Info(
            $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
            $"mapped bodySlots={bodySlots}, rimSlots={rimSlots}; interior paint remains unchanged.");
        if (bodySlots == 0 || rimSlots == 0)
            context?.Logger.Warn(
                $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
                $"paint mapping incomplete bodySlots={bodySlots}, rimSlots={rimSlots}.");
    }

    private void ApplyCurrentColor()
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return;
        var tint = (Color32)selected.tint;
        if (hasAppliedTint && ReferenceEquals(selected, appliedVehicleColor) && tint.Equals(appliedTint))
            return;

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

        appliedVehicleColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        context?.Logger.Info(
            $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
            $"applied color='{((UnityEngine.Object)selected).name}' rgba={tint} to {slots.Count} body/rim slots.");
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
        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;

        internal PaintSlot(Renderer renderer, Material material, int materialIndex)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
        }
    }
}
