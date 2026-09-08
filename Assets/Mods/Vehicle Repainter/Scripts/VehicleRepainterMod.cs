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

        private static readonly FieldInfo? VehicleColorBackingField = typeof(CarFeatures).GetField(
            "<VehicleColor>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ModContext context;
        private OverlayUI? overlayUi;
        private GasStationOverlay? originalGasStationOverlay;
        private ExtendedGasStationOverlay? extendedGasStationOverlay;
        private RepaintPurchasableAsset? activeRepaintAsset;

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

            if (CurrentStationTriggerField == null || PurchaseButtonField == null || VehicleColorBackingField == null)
            {
                context.Logger.Error("Could not install: required cached vanilla UI fields were not found.");
                return;
            }

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

            var repaintAsset = new RepaintPurchasableAsset(context, vehicle, stationTrigger, HandleRepaintUiClosed);
            activeRepaintAsset = repaintAsset;
            GasStationOverlay.Hide(stationTrigger);
            purchaseUi.SetAsset(repaintAsset);

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
            private bool purchaseCompleted;

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

            public string GetLocalizeKey() => "vehicle-repainter:title";

            public float GetPurchasePrice() => RepaintPrice;

            public string GetInitialColor() => committedColorName;

            public List<(string key, string value)> GetSpecs() => new List<(string, string)>();

            public List<(string, Color32)> GetColors()
            {
                var colors = InstanceBehavior<GlobalReferences>.Instance.vehicleColors;
                return colors.Select(color => (((UnityEngine.Object)color).name, color.tint)).ToList();
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
                if (!purchaseCompleted)
                    originalPaint.Restore(vehicle.CarFeatures);

                RestoreGasStationOverlayIfStillRelevant();
                onClosed(this);
            }

            public bool Purchase()
            {
                var activeVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
                if (!ReferenceEquals(activeVehicle, vehicle) || vehicle.vehicleInstance == null ||
                    vehicle.CarFeatures == null || !Mathf.Approximately(vehicle.CurrentSpeed, 0f))
                {
                    context.Logger.Warn("Repaint confirmation was rejected because the serviced vehicle is no longer active and stopped.");
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
    }
}
