#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.SoundSystem;
using Data.VehicleColors;
using Entities;
using Extensions;
using Helpers;
using Localizor.LanguageChangeEvent;
using UI;
using UI.Elements;
using UI.Overlays;
using UI.PurchaseVehicle;
using UnityEngine;
using UnityEngine.UI;

[assembly: RegisterModClass(typeof(VehicleRepainter.VehicleRepainterMod))]

namespace VehicleRepainter
{
    [ModEntryOnCityLoad]
    public sealed class VehicleRepainterMod : IModBigAmbitions
    {
        private VehicleRepainterRuntime? runtime;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            runtime = new VehicleRepainterRuntime(context);
            runtime.Install();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Uninstall();
            runtime = null;
            return Task.CompletedTask;
        }
    }

    internal sealed class VehicleRepainterRuntime
    {
        internal const float RepaintPrice = 800f;

        private static readonly FieldInfo? CurrentStationTriggerField = typeof(GasStationOverlay).GetField(
            "_currentStationTrigger",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? PurchaseButtonField = typeof(PurchaseVehicleUI).GetField(
            "purchaseButton",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? ColorsGridLayoutGroupField = typeof(PurchaseVehicleUI).GetField(
            "colorsGridLayoutGroup",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? VehicleColorBackingField = typeof(CarFeatures).GetField(
            "<VehicleColor>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly CustomColorDefinition[] AdditionalColors =
        {
            new CustomColorDefinition("VehicleRepainter_Charcoal", new Color32(43, 47, 54, 255), new Color32(105, 112, 125, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_White", new Color32(238, 238, 232, 255), new Color32(255, 255, 255, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Ivory", new Color32(245, 235, 210, 255), new Color32(255, 250, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Slate", new Color32(80, 95, 110, 255), new Color32(150, 170, 190, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_DeepRed", new Color32(120, 0, 0, 255), new Color32(205, 55, 45, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Burgundy", new Color32(105, 16, 38, 255), new Color32(185, 65, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Scarlet", new Color32(220, 30, 20, 255), new Color32(255, 105, 80, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Coral", new Color32(238, 83, 74, 255), new Color32(255, 160, 140, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Peach", new Color32(255, 160, 105, 255), new Color32(255, 215, 175, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Orange", new Color32(255, 106, 0, 255), new Color32(255, 175, 85, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Copper", new Color32(166, 79, 45, 255), new Color32(235, 145, 95, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Brown", new Color32(83, 43, 27, 255), new Color32(160, 95, 60, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Amber", new Color32(255, 170, 0, 255), new Color32(255, 225, 95, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Gold", new Color32(196, 145, 35, 255), new Color32(255, 220, 115, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Olive", new Color32(110, 110, 20, 255), new Color32(190, 190, 75, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lime", new Color32(104, 190, 35, 255), new Color32(180, 255, 100, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Emerald", new Color32(0, 120, 72, 255), new Color32(70, 220, 145, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Mint", new Color32(85, 210, 150, 255), new Color32(160, 255, 210, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Turquoise", new Color32(0, 157, 154, 255), new Color32(80, 240, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Teal", new Color32(0, 105, 110, 255), new Color32(65, 195, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Cyan", new Color32(0, 174, 239, 255), new Color32(95, 225, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SkyBlue", new Color32(85, 180, 240, 255), new Color32(165, 225, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Azure", new Color32(0, 112, 221, 255), new Color32(90, 185, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Indigo", new Color32(55, 45, 145, 255), new Color32(120, 105, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Violet", new Color32(105, 66, 180, 255), new Color32(175, 135, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Lavender", new Color32(170, 125, 215, 255), new Color32(225, 190, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Magenta", new Color32(194, 0, 151, 255), new Color32(255, 90, 225, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Rose", new Color32(230, 70, 125, 255), new Color32(255, 150, 190, 255), 1f)
        };

        private readonly ModContext context;
        private readonly Dictionary<string, VehicleColor> customVehicleColors =
            new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
        private readonly List<VehicleColor> ownedCustomVehicleColors = new List<VehicleColor>();
        private OverlayUI? overlayUi;
        private GasStationOverlay? originalGasStationOverlay;
        private ExtendedGasStationOverlay? extendedGasStationOverlay;
        private RepaintPurchasableAsset? activeRepaintAsset;
        private GlobalReferences? registeredGlobalReferences;

        internal VehicleRepainterRuntime(ModContext context)
        {
            this.context = context;
        }

        internal void Install()
        {
            var uis = InstanceBehavior<UIs>.Instance;
            if (uis == null || uis.overlayUI == null)
            {
                context.Logger.Error("Could not install: the game's overlay UI is unavailable.");
                return;
            }

            if (CurrentStationTriggerField == null || PurchaseButtonField == null ||
                ColorsGridLayoutGroupField == null || VehicleColorBackingField == null)
            {
                context.Logger.Error("Could not install: required cached vanilla UI fields were not found.");
                return;
            }

            if (!RegisterCustomVehicleColors())
                return;

            overlayUi = uis.overlayUI;
            originalGasStationOverlay = overlayUi.gasStation;
            extendedGasStationOverlay = new ExtendedGasStationOverlay(this);
            overlayUi.gasStation = extendedGasStationOverlay;
        }

        internal void Uninstall()
        {
            if (activeRepaintAsset != null && PurchaseVehicleUI.IsPanelOpen)
                InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI?.Close();

            activeRepaintAsset = null;
            var uis = InstanceBehavior<UIs>.Instance;
            if (uis != null && uis.overlayUI != null && extendedGasStationOverlay != null &&
                ReferenceEquals(uis.overlayUI.gasStation, extendedGasStationOverlay))
            {
                GasStationOverlay.Hide();
                uis.overlayUI.gasStation = originalGasStationOverlay ?? new GasStationOverlay();
            }

            overlayUi = null;
            originalGasStationOverlay = null;
            extendedGasStationOverlay = null;
            UnregisterCustomVehicleColors();
        }

        internal GasStationTrigger? GetCurrentStationTrigger(GasStationOverlay overlay)
        {
            return CurrentStationTriggerField?.GetValue(overlay) as GasStationTrigger;
        }

        internal void OpenRepaintUi(GasStationTrigger stationTrigger)
        {
            var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            var colors = InstanceBehavior<GlobalReferences>.Instance?.vehicleColors;
            if (vehicle == null || vehicle.vehicleInstance == null || vehicle.CarFeatures == null ||
                colors == null || colors.Length == 0)
            {
                context.Logger.Warn("Repaint was requested, but the active vehicle does not expose the normal vehicle color system.");
                return;
            }

            var purchaseUi = InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI;
            if (purchaseUi == null)
            {
                context.Logger.Error("Could not open Repaint: the vanilla vehicle color UI is unavailable.");
                return;
            }

            if (PurchaseVehicleUI.IsPanelOpen)
            {
                context.Logger.Warn("Could not open Repaint because another vehicle purchase panel is already open.");
                return;
            }

            if (vehicle.vehicleCollider == null || !stationTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds) ||
                !Mathf.Approximately(vehicle.CurrentSpeed, 0f))
            {
                context.Logger.Warn("Could not open Repaint because the active vehicle is no longer stopped inside the service bay.");
                return;
            }

            var repaintAsset = new RepaintPurchasableAsset(context, vehicle, stationTrigger, HandleRepaintUiClosed);
            activeRepaintAsset = repaintAsset;
            GasStationOverlay.Hide(stationTrigger);
            purchaseUi.SetAsset(repaintAsset);
            repaintAsset.ApplyColorGridLayout(purchaseUi);
            repaintAsset.BeginSession();

            if (PurchaseButtonField!.GetValue(purchaseUi) is Button purchaseButton)
            {
                purchaseButton.interactable = SaveGameManager.Current != null &&
                                              SaveGameManager.Current.Money >= RepaintPrice;

                var label = purchaseButton.GetComponentInChildren<TextLocalizationComponent>(true);
                if (label != null)
                    label.Key = "vehicle-repainter:confirm";
            }
        }

        private void HandleRepaintUiClosed(RepaintPurchasableAsset repaintAsset)
        {
            if (ReferenceEquals(activeRepaintAsset, repaintAsset))
                activeRepaintAsset = null;
        }

        private bool RegisterCustomVehicleColors()
        {
            var globalReferences = InstanceBehavior<GlobalReferences>.Instance;
            if (globalReferences == null || globalReferences.vehicleColors == null)
            {
                context.Logger.Error("Could not install: the game's vehicle color registry is unavailable.");
                return false;
            }

            var colors = globalReferences.vehicleColors.Where(color => color != null).ToList();
            foreach (var definition in AdditionalColors)
            {
                var color = colors.FirstOrDefault(existing =>
                    string.Equals(((UnityEngine.Object)existing).name, definition.Name, StringComparison.Ordinal));
                if (color == null)
                {
                    color = ScriptableObject.CreateInstance<VehicleColor>();
                    ((UnityEngine.Object)color).name = definition.Name;
                    color.tint = definition.Tint;
                    color.fresnelColor = definition.FresnelColor;
                    color.fresnelPower = definition.FresnelPower;
                    color.randomWeight = 0f;
                    color.hideFlags = HideFlags.HideAndDontSave;
                    colors.Add(color);
                    ownedCustomVehicleColors.Add(color);
                }

                customVehicleColors[definition.Name] = color;
            }

            globalReferences.vehicleColors = colors.ToArray();
            registeredGlobalReferences = globalReferences;
            RestoreSavedCustomVehicleColors();
            return true;
        }

        private void RestoreSavedCustomVehicleColors()
        {
            foreach (var vehicle in VehicleHelper.AllPlayerVehicles.ToArray())
            {
                if (vehicle == null || vehicle.vehicleInstance == null || vehicle.CarFeatures == null ||
                    !customVehicleColors.TryGetValue(vehicle.vehicleInstance.vehicleColorName, out var color))
                {
                    continue;
                }

                vehicle.CarFeatures.SetColor(color);
            }
        }

        private void UnregisterCustomVehicleColors()
        {
            if (registeredGlobalReferences != null && registeredGlobalReferences.vehicleColors != null &&
                ownedCustomVehicleColors.Count > 0)
            {
                var ownedColors = new HashSet<VehicleColor>(ownedCustomVehicleColors);
                registeredGlobalReferences.vehicleColors = registeredGlobalReferences.vehicleColors
                    .Where(color => color != null && !ownedColors.Contains(color))
                    .ToArray();
            }

            foreach (var color in ownedCustomVehicleColors)
            {
                if (color != null)
                    UnityEngine.Object.Destroy(color);
            }

            customVehicleColors.Clear();
            ownedCustomVehicleColors.Clear();
            registeredGlobalReferences = null;
        }

        private sealed class ExtendedGasStationOverlay : GasStationOverlay, IOverlay
        {
            private readonly VehicleRepainterRuntime runtime;

            internal ExtendedGasStationOverlay(VehicleRepainterRuntime runtime)
            {
                this.runtime = runtime;
            }

            ButtonInfo[]? IOverlay.GetButtons()
            {
                var vanillaButtons = base.GetButtons();
                var stationTrigger = runtime.GetCurrentStationTrigger(this);
                var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;

                if (vanillaButtons == null || stationTrigger == null || !stationTrigger.isRepairStation ||
                    vehicle == null || vehicle.vehicleInstance == null || vehicle.CarFeatures == null ||
                    !vehicle.vehicleType.IsMotorVehicle)
                {
                    return vanillaButtons;
                }

                var result = new ButtonInfo[vanillaButtons.Length + 1];
                Array.Copy(vanillaButtons, result, vanillaButtons.Length);
                result[result.Length - 1] = new ButtonInfo(
                    "RepaintVehicle",
                    "vehicle-repainter:repaint",
                    new { price = RepaintPrice.ToShortCurrencyFormat() },
                    "blue",
                    () => runtime.OpenRepaintUi(stationTrigger),
                    PlayerAction.SpecialInteract,
                    Mathf.Approximately(vehicle.CurrentSpeed, 0f));
                return result;
            }
        }

        private sealed class RepaintPurchasableAsset : IPurchasableAsset
        {
            private readonly ModContext context;
            private readonly VehicleController vehicle;
            private readonly GasStationTrigger stationTrigger;
            private readonly Action<RepaintPurchasableAsset> onClosed;
            private readonly VehiclePaintSnapshot originalPaint;
            private string committedColorName;
            private string selectedColorName;
            private bool closed;
            private bool movementLocked;
            private bool purchaseCompleted;
            private GridLayoutGroup? colorGridLayout;
            private ColorGridLayoutSnapshot? originalColorGridLayout;
            private Image? colorGridBackground;
            private bool colorGridBackgroundWasEnabled;

            internal RepaintPurchasableAsset(
                ModContext context,
                VehicleController vehicle,
                GasStationTrigger stationTrigger,
                Action<RepaintPurchasableAsset> onClosed)
            {
                this.context = context;
                this.vehicle = vehicle;
                this.stationTrigger = stationTrigger;
                this.onClosed = onClosed;
                originalPaint = new VehiclePaintSnapshot(vehicle.CarFeatures);
                committedColorName = ResolveInitialColorName(vehicle);
                selectedColorName = committedColorName;
            }

            internal void BeginSession()
            {
                if (closed || movementLocked)
                    return;

                stationTrigger.onExited += HandleStationExited;
                GlobalEvents.onExitVehicle += HandleVehicleExited;
                vehicle.SetFreeze(true);
                movementLocked = true;
            }

            internal void ApplyColorGridLayout(PurchaseVehicleUI purchaseUi)
            {
                if (ColorsGridLayoutGroupField!.GetValue(purchaseUi) is not GridLayoutGroup gridLayout)
                {
                    context.Logger.Warn("Could not resize the repaint color grid because the vanilla layout is unavailable.");
                    return;
                }

                colorGridLayout = gridLayout;
                originalColorGridLayout = new ColorGridLayoutSnapshot(gridLayout);
                colorGridBackground = gridLayout.GetComponent<Image>();
                if (colorGridBackground != null)
                {
                    colorGridBackgroundWasEnabled = colorGridBackground.enabled;
                    colorGridBackground.enabled = false;
                }

                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = 7;
                gridLayout.cellSize = new Vector2(50f, 50f);
                gridLayout.spacing = new Vector2(7f, 7f);
                gridLayout.padding = new RectOffset(22, 22, 16, 16);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)gridLayout.transform);
            }

            public string GetLocalizeKey() => "vehicle-repainter:title";

            public float GetPurchasePrice() => RepaintPrice;

            public string GetInitialColor() => committedColorName;

            public List<(string key, string value)> GetSpecs() => new List<(string, string)>();

            public List<(string, Color32)> GetColors()
            {
                var colors = InstanceBehavior<GlobalReferences>.Instance.vehicleColors;
                return colors
                    .Where(color => color != null)
                    .Select((color, index) => new SortableVehicleColor(color, index))
                    .OrderBy(color => color.Group)
                    .ThenBy(color => color.Hue)
                    .ThenBy(color => color.Value)
                    .ThenByDescending(color => color.Saturation)
                    .ThenBy(color => color.OriginalIndex)
                    .Select(color => (color.Name, color.Tint))
                    .ToList();
            }

            public void SetColor(string colorName, bool updateVisuals = true)
            {
                if (!VehicleHelper.TryGetVehicleColor(colorName, out var vehicleColor))
                    return;

                selectedColorName = colorName;
                if (updateVisuals && vehicle.CarFeatures != null)
                    vehicle.CarFeatures.SetColor(vehicleColor);
            }

            public void ResetColor()
            {
                if (closed)
                    return;

                closed = true;
                stationTrigger.onExited -= HandleStationExited;
                GlobalEvents.onExitVehicle -= HandleVehicleExited;

                if (!purchaseCompleted && vehicle != null && vehicle.CarFeatures != null)
                    originalPaint.Restore(vehicle.CarFeatures);

                if (movementLocked && vehicle != null)
                    vehicle.SetFreeze(false);

                RestoreColorGridLayout();

                RestoreGasStationOverlayIfStillRelevant();
                onClosed(this);
            }

            public bool Purchase()
            {
                var activeVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
                if (!ReferenceEquals(activeVehicle, vehicle) || vehicle.vehicleInstance == null ||
                    vehicle.CarFeatures == null || vehicle.vehicleCollider == null ||
                    !stationTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds) ||
                    !Mathf.Approximately(vehicle.CurrentSpeed, 0f))
                {
                    context.Logger.Warn(
                        "Repaint confirmation was rejected because the serviced vehicle is no longer active, stopped, and inside the service bay.");
                    return false;
                }

                var transaction = new TransactionInfo("vehicle-repainter:transaction");
                if (vehicle.vehicleType.taxDeductible)
                    transaction.SetTaxDeductibleName("ba:businesstype_gasstation");

                if (!GameManager.ChangeMoneySafe(
                        -RepaintPrice,
                        transaction,
                        null,
                        stationTrigger.cbc?.buildingRegistration?.Address,
                        force: false,
                        showNotification: true))
                {
                    context.Logger.Warn(
                        $"Repaint confirmation was rejected for vehicle id '{vehicle.vehicleInstance.id}' due to insufficient money.");
                    return false;
                }

                vehicle.vehicleInstance.vehicleColorName = selectedColorName;
                SetColor(selectedColorName);
                committedColorName = selectedColorName;
                purchaseCompleted = true;
                SaveGameManager.MarkChange();
                GameEvent.Invoke(string.Empty);
                InstanceBehavior<SfxManager>.Instance.PlayAudio(
                    SoundType.PurchaseSuccess,
                    vehicle.transform.position,
                    1f,
                    isPlayerCreatedSound: true);

                return true;
            }

            public void Order(Address deliveryAddress, Contact storeContact, bool showNotification)
            {
                // Delivery is hidden by the vanilla UI because repainting is opened outside a vehicle store.
            }

            public IEnumerator ShowcaseAnimation()
            {
                yield break;
            }

            public IEnumerator CancelShowcaseAnimation()
            {
                yield break;
            }

            private void HandleStationExited(GasStationTrigger exitedStation)
            {
                if (closed || !ReferenceEquals(exitedStation, stationTrigger))
                    return;

                CancelSession();
            }

            private void HandleVehicleExited(VehicleController exitedVehicle)
            {
                if (closed || !ReferenceEquals(exitedVehicle, vehicle))
                    return;

                CancelSession();
            }

            private void CancelSession()
            {
                var purchaseUi = InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI;
                if (purchaseUi != null && PurchaseVehicleUI.IsPanelOpen)
                    purchaseUi.Close();
                else
                    ResetColor();
            }

            private void RestoreGasStationOverlayIfStillRelevant()
            {
                var activeVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
                if (!ReferenceEquals(activeVehicle, vehicle) || !PlayerHelper.IsUsingVehicle ||
                    vehicle.vehicleCollider == null || !stationTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds))
                {
                    return;
                }

                GasStationOverlay.Show(stationTrigger);
            }

            private void RestoreColorGridLayout()
            {
                if (colorGridLayout == null || originalColorGridLayout == null)
                    return;

                originalColorGridLayout.Restore(colorGridLayout);
                if (colorGridBackground != null)
                    colorGridBackground.enabled = colorGridBackgroundWasEnabled;

                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)colorGridLayout.transform);
                colorGridLayout = null;
                originalColorGridLayout = null;
                colorGridBackground = null;
            }

            private readonly struct SortableVehicleColor
            {
                internal readonly string Name;
                internal readonly Color32 Tint;
                internal readonly int Group;
                internal readonly float Hue;
                internal readonly float Saturation;
                internal readonly float Value;
                internal readonly int OriginalIndex;

                internal SortableVehicleColor(VehicleColor vehicleColor, int originalIndex)
                {
                    Name = ((UnityEngine.Object)vehicleColor).name;
                    Tint = vehicleColor.tint;
                    OriginalIndex = originalIndex;
                    Color.RGBToHSV(Tint, out var hue, out var saturation, out var value);
                    Group = saturation < 0.14f ? 0 : 1;
                    Hue = Group == 0 ? 0f : hue >= 0.95f ? hue - 1f : hue;
                    Saturation = saturation;
                    Value = value;
                }
            }

            private sealed class ColorGridLayoutSnapshot
            {
                private readonly GridLayoutGroup.Constraint constraint;
                private readonly int constraintCount;
                private readonly Vector2 cellSize;
                private readonly Vector2 spacing;
                private readonly RectOffset padding;

                internal ColorGridLayoutSnapshot(GridLayoutGroup gridLayout)
                {
                    constraint = gridLayout.constraint;
                    constraintCount = gridLayout.constraintCount;
                    cellSize = gridLayout.cellSize;
                    spacing = gridLayout.spacing;
                    padding = new RectOffset(
                        gridLayout.padding.left,
                        gridLayout.padding.right,
                        gridLayout.padding.top,
                        gridLayout.padding.bottom);
                }

                internal void Restore(GridLayoutGroup gridLayout)
                {
                    gridLayout.constraint = constraint;
                    gridLayout.constraintCount = constraintCount;
                    gridLayout.cellSize = cellSize;
                    gridLayout.spacing = spacing;
                    gridLayout.padding = padding;
                }
            }

            private static string ResolveInitialColorName(VehicleController vehicle)
            {
                if (!string.IsNullOrEmpty(vehicle.vehicleInstance.vehicleColorName) &&
                    VehicleHelper.TryGetVehicleColor(vehicle.vehicleInstance.vehicleColorName, out _))
                {
                    return vehicle.vehicleInstance.vehicleColorName;
                }

                var liveColor = vehicle.CarFeatures?.VehicleColor;
                if (liveColor != null)
                    return ((UnityEngine.Object)liveColor).name;

                VehicleColor[] colors = InstanceBehavior<GlobalReferences>.Instance.vehicleColors;
                return colors.Length > 0 ? ((UnityEngine.Object)colors[0]).name : string.Empty;
            }

            private sealed class VehiclePaintSnapshot
            {
                private readonly VehicleColor? vehicleColor;
                private readonly List<RendererPaintSnapshot> rendererSnapshots = new List<RendererPaintSnapshot>();

                internal VehiclePaintSnapshot(CarFeatures carFeatures)
                {
                    vehicleColor = carFeatures.VehicleColor;
                    var bodyMeshes = carFeatures.bodyMeshes;
                    if (bodyMeshes == null)
                        return;

                    foreach (var renderer in bodyMeshes)
                    {
                        if (renderer == null)
                            continue;

                        var propertyBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(propertyBlock);
                        rendererSnapshots.Add(new RendererPaintSnapshot(renderer, propertyBlock));
                    }
                }

                internal void Restore(CarFeatures carFeatures)
                {
                    foreach (var snapshot in rendererSnapshots)
                    {
                        if (snapshot.Renderer != null)
                            snapshot.Renderer.SetPropertyBlock(snapshot.PropertyBlock);
                    }

                    VehicleColorBackingField!.SetValue(carFeatures, vehicleColor);
                }
            }

            private readonly struct RendererPaintSnapshot
            {
                internal readonly Renderer Renderer;
                internal readonly MaterialPropertyBlock PropertyBlock;

                internal RendererPaintSnapshot(Renderer renderer, MaterialPropertyBlock propertyBlock)
                {
                    Renderer = renderer;
                    PropertyBlock = propertyBlock;
                }
            }
        }

        private readonly struct CustomColorDefinition
        {
            internal readonly string Name;
            internal readonly Color32 Tint;
            internal readonly Color32 FresnelColor;
            internal readonly float FresnelPower;

            internal CustomColorDefinition(string name, Color32 tint, Color32 fresnelColor, float fresnelPower)
            {
                Name = name;
                Tint = tint;
                FresnelColor = fresnelColor;
                FresnelPower = fresnelPower;
            }
        }
    }
}
