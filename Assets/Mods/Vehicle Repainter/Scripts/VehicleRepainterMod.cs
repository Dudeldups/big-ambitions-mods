#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: RegisterModClass(typeof(VehicleRepainter.VehicleRepainterMod))]

namespace VehicleRepainter
{
    [ModEntryOnInitializationLoad]
    public sealed class VehicleRepainterMod : IModBigAmbitions
    {
        private VehicleRepainterRuntime? runtime;
        private VehicleRepainterLifecycle? lifecycle;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            runtime = new VehicleRepainterRuntime(context);
            lifecycle = VehicleRepainterLifecycle.Initialize(runtime);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            lifecycle?.Shutdown();
            lifecycle = null;
            runtime?.Uninstall();
            runtime = null;
            return Task.CompletedTask;
        }
    }

    internal sealed class VehicleRepainterLifecycle : MonoBehaviour
    {
        private Coroutine? pendingInstall;
        private VehicleRepainterRuntime? runtime;

        internal static VehicleRepainterLifecycle Initialize(VehicleRepainterRuntime runtime)
        {
            var lifecycleObject = new GameObject(nameof(VehicleRepainterLifecycle));
            DontDestroyOnLoad(lifecycleObject);
            var lifecycle = lifecycleObject.AddComponent<VehicleRepainterLifecycle>();
            lifecycle.runtime = runtime;
            lifecycle.SubscribeGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(lifecycle.HandleGameLoadedLate);
            lifecycle.ScheduleInstall("mod-load");
            return lifecycle;
        }

        internal void Shutdown()
        {
            if (pendingInstall != null)
            {
                StopCoroutine(pendingInstall);
                pendingInstall = null;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeGlobalEvents();
            runtime = null;
            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeGlobalEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeGlobalEvents();
        }

        private void SubscribeGlobalEvents()
        {
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        }

        private void UnsubscribeGlobalEvents()
        {
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(HandleGameLoadedLate);
            ScheduleInstall($"scene-loaded:{scene.name}");
        }

        private void HandleGameLoadedLate()
        {
            SubscribeGlobalEvents();
            ScheduleInstall("game-loaded-late");
        }

        private void HandleGameUnloaded()
        {
            if (pendingInstall != null)
            {
                StopCoroutine(pendingInstall);
                pendingInstall = null;
            }

            runtime?.Uninstall();
        }

        private void ScheduleInstall(string source)
        {
            if (pendingInstall != null)
                StopCoroutine(pendingInstall);

            pendingInstall = StartCoroutine(InstallAfterSceneSetup(source));
        }

        private IEnumerator InstallAfterSceneSetup(string source)
        {
            yield return null;
            pendingInstall = null;
            runtime?.Install(source);
        }
    }

    internal sealed class VehicleRepainterRuntime
    {
        internal const float RepaintPrice = 800f;

        private static class DebugOptions
        {
            private const string GlobalDebugMarker = "vehicle-repainter.debug";
            private const string ButtonDebugMarker = "button-diagnostics.debug";

            internal static bool EnableDebugLogging = false;
            internal static bool EnableButtonDiagnostics = false;

            internal static void Configure(string modId)
            {
                EnableDebugLogging = false;
                EnableButtonDiagnostics = false;
                if (string.IsNullOrWhiteSpace(modId) || !Directory.Exists(modId))
                    return;

                var configDirectory = Path.Combine(modId, "Config");
                EnableDebugLogging = File.Exists(Path.Combine(configDirectory, GlobalDebugMarker));
                EnableButtonDiagnostics = File.Exists(Path.Combine(configDirectory, ButtonDebugMarker));
            }
        }

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
            new CustomColorDefinition("VehicleRepainter_Onyx", new Color32(20, 23, 28, 255), new Color32(70, 78, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Charcoal", new Color32(43, 47, 54, 255), new Color32(105, 112, 125, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Graphite", new Color32(65, 68, 72, 255), new Color32(125, 130, 138, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Gunmetal", new Color32(70, 82, 90, 255), new Color32(135, 155, 170, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Smoke", new Color32(105, 110, 115, 255), new Color32(170, 178, 185, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Ash", new Color32(135, 140, 145, 255), new Color32(195, 202, 210, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Platinum", new Color32(185, 190, 195, 255), new Color32(240, 245, 250, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Pearl", new Color32(215, 220, 225, 255), new Color32(255, 255, 255, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_White", new Color32(238, 238, 232, 255), new Color32(255, 255, 255, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Cream", new Color32(250, 225, 175, 255), new Color32(255, 245, 215, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Ivory", new Color32(245, 235, 210, 255), new Color32(255, 250, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Champagne", new Color32(224, 202, 160, 255), new Color32(255, 235, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Slate", new Color32(80, 95, 110, 255), new Color32(150, 170, 190, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Maroon", new Color32(75, 0, 20, 255), new Color32(155, 45, 70, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Raspberry", new Color32(155, 25, 75, 255), new Color32(235, 90, 135, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_DeepRed", new Color32(120, 0, 0, 255), new Color32(205, 55, 45, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Burgundy", new Color32(105, 16, 38, 255), new Color32(185, 65, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Cherry", new Color32(170, 10, 45, 255), new Color32(245, 75, 105, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_BrickRed", new Color32(145, 45, 35, 255), new Color32(220, 105, 85, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Scarlet", new Color32(220, 30, 20, 255), new Color32(255, 105, 80, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Coral", new Color32(238, 83, 74, 255), new Color32(255, 160, 140, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Salmon", new Color32(245, 125, 115, 255), new Color32(255, 195, 180, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Terracotta", new Color32(185, 80, 55, 255), new Color32(245, 145, 115, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Chocolate", new Color32(90, 45, 25, 255), new Color32(170, 100, 65, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Taupe", new Color32(120, 100, 85, 255), new Color32(190, 165, 140, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Peach", new Color32(255, 160, 105, 255), new Color32(255, 215, 175, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Apricot", new Color32(240, 175, 95, 255), new Color32(255, 225, 155, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Sand", new Color32(194, 155, 105, 255), new Color32(245, 210, 165, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Tangerine", new Color32(245, 125, 25, 255), new Color32(255, 190, 95, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Orange", new Color32(255, 106, 0, 255), new Color32(255, 175, 85, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Copper", new Color32(166, 79, 45, 255), new Color32(235, 145, 95, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Bronze", new Color32(140, 90, 40, 255), new Color32(220, 160, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Brown", new Color32(83, 43, 27, 255), new Color32(160, 95, 60, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Amber", new Color32(255, 170, 0, 255), new Color32(255, 225, 95, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Gold", new Color32(196, 145, 35, 255), new Color32(255, 220, 115, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Ochre", new Color32(185, 120, 30, 255), new Color32(245, 185, 90, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Honey", new Color32(215, 160, 55, 255), new Color32(255, 220, 125, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Mustard", new Color32(170, 135, 25, 255), new Color32(235, 205, 85, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lemon", new Color32(240, 225, 35, 255), new Color32(255, 250, 120, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Khaki", new Color32(160, 150, 95, 255), new Color32(225, 215, 150, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Olive", new Color32(110, 110, 20, 255), new Color32(190, 190, 75, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Chartreuse", new Color32(155, 220, 25, 255), new Color32(215, 255, 105, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Avocado", new Color32(110, 145, 45, 255), new Color32(180, 215, 105, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lime", new Color32(104, 190, 35, 255), new Color32(180, 255, 100, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SpringGreen", new Color32(35, 200, 80, 255), new Color32(115, 255, 150, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Moss", new Color32(85, 110, 45, 255), new Color32(155, 190, 100, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Sage", new Color32(120, 150, 105, 255), new Color32(185, 215, 165, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Forest", new Color32(25, 85, 40, 255), new Color32(80, 170, 100, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Pine", new Color32(10, 70, 55, 255), new Color32(70, 155, 125, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Emerald", new Color32(0, 120, 72, 255), new Color32(70, 220, 145, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Jade", new Color32(35, 155, 105, 255), new Color32(105, 235, 175, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Mint", new Color32(85, 210, 150, 255), new Color32(160, 255, 210, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Celadon", new Color32(145, 210, 175, 255), new Color32(210, 255, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Seafoam", new Color32(120, 220, 185, 255), new Color32(195, 255, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Turquoise", new Color32(0, 157, 154, 255), new Color32(80, 240, 230, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Lagoon", new Color32(0, 130, 135, 255), new Color32(70, 215, 215, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Teal", new Color32(0, 105, 110, 255), new Color32(65, 195, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Petrol", new Color32(20, 80, 95, 255), new Color32(80, 165, 185, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Aqua", new Color32(45, 220, 220, 255), new Color32(140, 255, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Cyan", new Color32(0, 174, 239, 255), new Color32(95, 225, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_IceBlue", new Color32(155, 215, 235, 255), new Color32(220, 250, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Cerulean", new Color32(25, 145, 205, 255), new Color32(100, 210, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SkyBlue", new Color32(85, 180, 240, 255), new Color32(165, 225, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_SteelBlue", new Color32(65, 105, 145, 255), new Color32(130, 185, 230, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Denim", new Color32(45, 90, 145, 255), new Color32(105, 165, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Azure", new Color32(0, 112, 221, 255), new Color32(90, 185, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_RoyalBlue", new Color32(35, 65, 190, 255), new Color32(100, 135, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Cobalt", new Color32(30, 55, 125, 255), new Color32(85, 120, 220, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_MidnightBlue", new Color32(15, 30, 70, 255), new Color32(60, 90, 160, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Periwinkle", new Color32(115, 135, 220, 255), new Color32(180, 195, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Indigo", new Color32(55, 45, 145, 255), new Color32(120, 105, 225, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Violet", new Color32(105, 66, 180, 255), new Color32(175, 135, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Amethyst", new Color32(140, 75, 180, 255), new Color32(210, 145, 245, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Grape", new Color32(80, 35, 120, 255), new Color32(150, 95, 205, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Lilac", new Color32(145, 105, 190, 255), new Color32(210, 170, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Lavender", new Color32(170, 125, 215, 255), new Color32(225, 190, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Plum", new Color32(105, 40, 115, 255), new Color32(180, 100, 195, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Mauve", new Color32(160, 115, 150, 255), new Color32(225, 175, 215, 255), 2f),
            new CustomColorDefinition("VehicleRepainter_Orchid", new Color32(205, 95, 210, 255), new Color32(250, 165, 255, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Magenta", new Color32(194, 0, 151, 255), new Color32(255, 90, 225, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Fuchsia", new Color32(235, 30, 190, 255), new Color32(255, 125, 225, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_HotPink", new Color32(255, 80, 165, 255), new Color32(255, 170, 215, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Blush", new Color32(225, 145, 165, 255), new Color32(255, 205, 215, 255), 1f),
            new CustomColorDefinition("VehicleRepainter_Rose", new Color32(230, 70, 125, 255), new Color32(255, 150, 190, 255), 1f)
        };

        private readonly ModContext context;
        private readonly Dictionary<string, VehicleColor> customVehicleColors =
            new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
        private readonly List<VehicleColor> ownedCustomVehicleColors = new List<VehicleColor>();
        private readonly List<GasStationTrigger> observedStationTriggers = new List<GasStationTrigger>();
        private readonly HashSet<string> reportedButtonFailures = new HashSet<string>(StringComparer.Ordinal);
        private OverlayUI? overlayUi;
        private GasStationOverlay? originalGasStationOverlay;
        private ExtendedGasStationOverlay? extendedGasStationOverlay;
        private RepaintPurchasableAsset? activeRepaintAsset;
        private Coroutine? pendingRepairOverlayRefresh;

        internal VehicleRepainterRuntime(ModContext context)
        {
            this.context = context;
            DebugOptions.Configure(context.ModId);
            TraceButton($"Diagnostic mode enabled for modId='{context.ModId}'.");
        }

        internal void Install(string source)
        {
            var uis = InstanceBehavior<UIs>.Instance;
            if (uis == null || uis.overlayUI == null)
            {
                TraceButton($"Deferred installation source='{source}': the game's overlay UI is not ready.");
                return;
            }

            if (extendedGasStationOverlay != null && overlayUi != null &&
                ReferenceEquals(overlayUi, uis.overlayUI) &&
                ReferenceEquals(overlayUi.gasStation, extendedGasStationOverlay))
            {
                TraceButton($"Installation already active source='{source}'.");
                return;
            }

            if (extendedGasStationOverlay != null || ownedCustomVehicleColors.Count > 0)
                Uninstall();

            if (CurrentStationTriggerField == null || PurchaseButtonField == null ||
                ColorsGridLayoutGroupField == null || VehicleColorBackingField == null)
            {
                context.Logger.Error(
                    "Could not install the Repaint button: required cached vanilla fields were not found; " +
                    $"stationTriggerField={(CurrentStationTriggerField == null ? "missing" : "found")}, " +
                    $"purchaseButtonField={(PurchaseButtonField == null ? "missing" : "found")}, " +
                    $"colorsGridField={(ColorsGridLayoutGroupField == null ? "missing" : "found")}, " +
                    $"vehicleColorField={(VehicleColorBackingField == null ? "missing" : "found")}.");
                return;
            }

            if (!InitializeCustomVehicleColors())
                return;

            overlayUi = uis.overlayUI;
            originalGasStationOverlay = overlayUi.gasStation;
            extendedGasStationOverlay = new ExtendedGasStationOverlay(this);
            overlayUi.gasStation = extendedGasStationOverlay;
            ObserveStationTriggers();
            TraceButton(
                $"Installed gas-station overlay extension source='{source}'; previousOverlay='{originalGasStationOverlay?.GetType().FullName ?? "null"}', " +
                $"observedTriggers={observedStationTriggers.Count}.");
        }

        internal void Uninstall()
        {
            if (activeRepaintAsset != null && PurchaseVehicleUI.IsPanelOpen)
                InstanceBehavior<UIs>.Instance?.playerHUD?.purchaseVehicleUI?.Close();

            activeRepaintAsset = null;
            StopObservingStationTriggers();
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
            ReleaseCustomVehicleColors();
        }

        private void ObserveStationTriggers()
        {
            var repairStationCount = 0;
            foreach (var trigger in Resources.FindObjectsOfTypeAll<GasStationTrigger>())
            {
                if (trigger == null || !trigger.gameObject.scene.IsValid())
                    continue;

                trigger.onEntered += HandleStationEntered;
                observedStationTriggers.Add(trigger);
                if (trigger.isRepairStation)
                    repairStationCount++;
            }

            TraceButton(
                $"Observed gas-station triggers: total={observedStationTriggers.Count}, repair={repairStationCount}.");
            if (repairStationCount == 0)
            {
                TraceButton(
                    "No loaded repair-station triggers were found during installation. " +
                    "The Repaint button may be unavailable because the service-bay entry events could not be observed.");
            }
        }

        private void StopObservingStationTriggers()
        {
            if (pendingRepairOverlayRefresh != null && overlayUi != null)
                overlayUi.StopCoroutine(pendingRepairOverlayRefresh);

            pendingRepairOverlayRefresh = null;
            foreach (var trigger in observedStationTriggers)
            {
                if (trigger != null)
                    trigger.onEntered -= HandleStationEntered;
            }

            observedStationTriggers.Clear();
        }

        private void HandleStationEntered(GasStationTrigger enteredTrigger)
        {
            reportedButtonFailures.Clear();
            if (overlayUi == null || extendedGasStationOverlay == null ||
                !ReferenceEquals(overlayUi.gasStation, extendedGasStationOverlay))
            {
                WarnButtonFailureOnce(
                    "overlay-replaced",
                    "The Repaint button cannot be injected because the active gas-station overlay is no longer " +
                    $"Vehicle Repainter's extension; activeOverlay='{overlayUi?.gasStation?.GetType().FullName ?? "null"}'. " +
                    "Another mod or a later game initialization step may have replaced it.");
            }

            var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            if (vehicle == null || vehicle.vehicleCollider == null)
            {
                var message =
                    "A gas-station trigger was entered, but the Repaint button cannot be evaluated because " +
                    $"selectedVehicle={(vehicle == null ? "null" : "present")}, " +
                    $"vehicleCollider={(vehicle?.vehicleCollider == null ? "null" : "present")}, " +
                    $"trigger='{DescribeTrigger(enteredTrigger)}'.";
                if (enteredTrigger.isRepairStation)
                    WarnButtonFailureOnce("entry-no-vehicle", message);
                else
                    TraceButton(message);
                return;
            }

            var repairTrigger = enteredTrigger.isRepairStation
                ? enteredTrigger
                : observedStationTriggers.FirstOrDefault(trigger =>
                    trigger != null && trigger.isActiveAndEnabled && trigger.isRepairStation &&
                    trigger.stationCollider != null && trigger.IntersectsBounds(vehicle.vehicleCollider.bounds));
            if (repairTrigger == null)
            {
                TraceButton(
                    $"Skipped repaint overlay refresh for trigger='{DescribeTrigger(enteredTrigger)}': " +
                    "no intersecting repair-station trigger was found.");
                return;
            }

            TraceButton(
                $"Repair-station entry resolved: entered='{DescribeTrigger(enteredTrigger)}', " +
                $"repair='{DescribeTrigger(repairTrigger)}', vehicle='{DescribeVehicle(vehicle)}'.");

            GasStationOverlay.Show(repairTrigger);
            if (overlayUi == null)
                return;

            if (pendingRepairOverlayRefresh != null)
                overlayUi.StopCoroutine(pendingRepairOverlayRefresh);

            pendingRepairOverlayRefresh = overlayUi.StartCoroutine(
                ReassertRepairOverlayNextFrame(vehicle, repairTrigger));
        }

        private IEnumerator ReassertRepairOverlayNextFrame(
            VehicleController vehicle,
            GasStationTrigger repairTrigger)
        {
            yield return null;
            pendingRepairOverlayRefresh = null;

            if (vehicle != null && vehicle.vehicleCollider != null && repairTrigger != null &&
                repairTrigger.isActiveAndEnabled && repairTrigger.IntersectsBounds(vehicle.vehicleCollider.bounds))
            {
                GasStationOverlay.Show(repairTrigger);
                TraceButton(
                    $"Reasserted repair overlay for trigger='{DescribeTrigger(repairTrigger)}', " +
                    $"vehicle='{DescribeVehicle(vehicle)}'.");
            }
            else
            {
                WarnButtonFailureOnce(
                    "reassert-invalid-state",
                    "The Repaint button overlay was not reasserted on the next frame because the repair trigger " +
                    $"or vehicle was no longer valid; trigger='{DescribeTrigger(repairTrigger)}', " +
                    $"vehicle='{DescribeVehicle(vehicle)}'.");
            }
        }

        internal void WarnButtonFailureOnce(string reason, string message)
        {
            if (reportedButtonFailures.Add(reason))
                context.Logger.Warn($"Vehicle Repainter button unavailable [{reason}]: {message}");
        }

        internal void TraceButton(string message)
        {
            if (DebugOptions.EnableDebugLogging && DebugOptions.EnableButtonDiagnostics)
                context.Logger.Info($"Vehicle Repainter button diagnostic: {message}");
        }

        private static string DescribeTrigger(GasStationTrigger? trigger)
        {
            if (trigger == null)
                return "null";

            return $"{trigger.name}#{trigger.GetInstanceID()}" +
                   $"(active={trigger.isActiveAndEnabled}, repair={trigger.isRepairStation}, " +
                   $"truckGarage={trigger.isTruckGarage}, collider={(trigger.stationCollider == null ? "null" : "present")})";
        }

        private static string DescribeVehicle(VehicleController? vehicle)
        {
            if (vehicle == null)
                return "null";

            return $"{vehicle.name}#{vehicle.GetInstanceID()}" +
                   $"(instance={(vehicle.vehicleInstance == null ? "null" : vehicle.vehicleInstance.id)}, " +
                   $"type={(vehicle.vehicleType == null ? "null" : vehicle.vehicleType.vehicleTypeName)}, " +
                   $"motor={vehicle.vehicleType != null && vehicle.vehicleType.IsMotorVehicle}, " +
                   $"carFeatures={(vehicle.CarFeatures == null ? "null" : "present")}, " +
                   $"collider={(vehicle.vehicleCollider == null ? "null" : "present")})";
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

            var repaintAsset = new RepaintPurchasableAsset(
                this,
                context,
                vehicle,
                stationTrigger,
                HandleRepaintUiClosed);
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

        private bool InitializeCustomVehicleColors()
        {
            var globalReferences = InstanceBehavior<GlobalReferences>.Instance;
            if (globalReferences == null || globalReferences.vehicleColors == null)
            {
                context.Logger.Error("Could not install: the game's vehicle color registry is unavailable.");
                return false;
            }

            var registeredColors = globalReferences.vehicleColors.Where(color => color != null).ToList();
            foreach (var definition in AdditionalColors)
            {
                var color = registeredColors.FirstOrDefault(existing =>
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
                }

                customVehicleColors[definition.Name] = color;
                ownedCustomVehicleColors.Add(color);
            }

            // Older versions registered these colors globally, which caused vanilla dealers to
            // display the repaint-only palette. Remove any stale entries while keeping the assets
            // alive in this runtime-owned collection for previews and saved-vehicle restoration.
            var dealerColors = registeredColors
                .Where(color => !customVehicleColors.ContainsKey(((UnityEngine.Object)color).name))
                .ToArray();
            if (dealerColors.Length != registeredColors.Count)
                globalReferences.vehicleColors = dealerColors;

            RestoreSavedCustomVehicleColors();
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onEnterVehicle += HandleVehicleEntered;
            return true;
        }

        internal List<VehicleColor> GetRepaintColors()
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance?.vehicleColors?
                .Where(color => color != null &&
                                !customVehicleColors.ContainsKey(((UnityEngine.Object)color).name))
                .ToList() ?? new List<VehicleColor>();

            foreach (var definition in AdditionalColors)
            {
                if (customVehicleColors.TryGetValue(definition.Name, out var color))
                    colors.Add(color);
            }

            return colors;
        }

        internal bool TryResolveVehicleColor(string colorName, out VehicleColor vehicleColor)
        {
            return customVehicleColors.TryGetValue(colorName, out vehicleColor) ||
                   VehicleHelper.TryGetVehicleColor(colorName, out vehicleColor);
        }

        private int RestoreSavedCustomVehicleColors()
        {
            var restoredVehicleCount = 0;
            foreach (var vehicle in VehicleHelper.AllPlayerVehicles.ToArray())
            {
                if (RestoreSavedCustomVehicleColor(vehicle))
                    restoredVehicleCount++;
            }

            return restoredVehicleCount;
        }

        private bool RestoreSavedCustomVehicleColor(VehicleController? vehicle)
        {
            if (vehicle == null || vehicle.vehicleInstance == null || vehicle.CarFeatures == null ||
                !customVehicleColors.TryGetValue(vehicle.vehicleInstance.vehicleColorName, out var color))
            {
                return false;
            }

            vehicle.CarFeatures.SetColor(color);
            return true;
        }

        private void HandleVehicleEntered(VehicleController vehicle)
        {
            RestoreSavedCustomVehicleColor(vehicle);
        }

        private void ReleaseCustomVehicleColors()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            foreach (var color in ownedCustomVehicleColors)
            {
                if (color != null)
                    UnityEngine.Object.Destroy(color);
            }

            customVehicleColors.Clear();
            ownedCustomVehicleColors.Clear();
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
                var stationTrigger = runtime.GetCurrentStationTrigger(this);
                ButtonInfo[]? vanillaButtons;
                try
                {
                    vanillaButtons = base.GetButtons();
                }
                catch (Exception exception)
                {
                    runtime.WarnButtonFailureOnce(
                        "vanilla-get-buttons-exception",
                        $"The vanilla gas-station overlay threw {exception.GetType().Name} while producing its " +
                        $"buttons; trigger='{DescribeTrigger(stationTrigger)}', message='{exception.Message}'.");
                    throw;
                }

                var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;

                if (stationTrigger == null)
                {
                    runtime.WarnButtonFailureOnce(
                        "missing-current-trigger",
                        $"The extended overlay has no current station trigger; vanillaButtonCount={vanillaButtons?.Length ?? 0}.");
                    return vanillaButtons;
                }

                if (!stationTrigger.isRepairStation)
                {
                    runtime.TraceButton(
                        $"Skipped button injection for non-repair trigger='{DescribeTrigger(stationTrigger)}'.");
                    return vanillaButtons;
                }

                if (vehicle == null)
                {
                    runtime.WarnButtonFailureOnce(
                        "missing-selected-vehicle",
                        $"Repair trigger='{DescribeTrigger(stationTrigger)}' is active, but GameManager.selectedVehicle is null; " +
                        $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}, vanillaButtonCount={vanillaButtons?.Length ?? 0}.");
                    return vanillaButtons;
                }

                if (vehicle.vehicleInstance == null || vehicle.CarFeatures == null || vehicle.vehicleType == null ||
                    !vehicle.vehicleType.IsMotorVehicle)
                {
                    runtime.WarnButtonFailureOnce(
                        "unsupported-vehicle-state",
                        $"Repair trigger='{DescribeTrigger(stationTrigger)}' is active, but the selected vehicle " +
                        $"does not satisfy repaint requirements; vehicle='{DescribeVehicle(vehicle)}', " +
                        $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}, vanillaButtonCount={vanillaButtons?.Length ?? 0}.");
                    return vanillaButtons;
                }

                if (vanillaButtons == null)
                {
                    runtime.WarnButtonFailureOnce(
                        "vanilla-service-buttons-null",
                        $"The vanilla repair overlay returned no Repair/Wash buttons, so Repaint was not appended; " +
                        $"trigger='{DescribeTrigger(stationTrigger)}', vehicle='{DescribeVehicle(vehicle)}', " +
                        $"insideMotorVehicle={VehicleHelper.IsInsideMotorVehicle()}. This commonly indicates a " +
                        "car-versus-truck garage mismatch or that the player is no longer considered inside the vehicle.");
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
                runtime.TraceButton(
                    $"Appended Repaint button; trigger='{DescribeTrigger(stationTrigger)}', " +
                    $"vehicle='{DescribeVehicle(vehicle)}', vanillaButtonCount={vanillaButtons.Length}, " +
                    $"speed={vehicle.CurrentSpeed:0.###}.");
                return result;
            }
        }

        private sealed class RepaintPurchasableAsset : IPurchasableAsset
        {
            private readonly VehicleRepainterRuntime runtime;
            private readonly ModContext context;
            private readonly VehicleController vehicle;
            private readonly GasStationTrigger stationTrigger;
            private readonly Action<RepaintPurchasableAsset> onClosed;
            private readonly VehiclePaintSnapshot originalPaint;
            private readonly string originalSavedColorName;
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
                VehicleRepainterRuntime runtime,
                ModContext context,
                VehicleController vehicle,
                GasStationTrigger stationTrigger,
                Action<RepaintPurchasableAsset> onClosed)
            {
                this.runtime = runtime;
                this.context = context;
                this.vehicle = vehicle;
                this.stationTrigger = stationTrigger;
                this.onClosed = onClosed;
                originalSavedColorName = vehicle.vehicleInstance.vehicleColorName;
                committedColorName = ResolveInitialColorName(vehicle);
                selectedColorName = committedColorName;

                VehicleColor originalColor;
                if (!runtime.TryResolveVehicleColor(committedColorName, out originalColor))
                    originalColor = vehicle.CarFeatures.VehicleColor;

                originalPaint = new VehiclePaintSnapshot(vehicle.CarFeatures, originalColor);
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

                var gridRect = gridLayout.transform as RectTransform;
                var panelRect = gridRect?.parent as RectTransform;
                var colorsSectionRect = panelRect?.parent as RectTransform;
                var sectionsContainer = colorsSectionRect?.parent;
                var colorsLabelRect = colorsSectionRect?.Find("Label") as RectTransform;
                var specsSection = sectionsContainer?.Find("Specs")?.gameObject;
                if (gridRect == null || panelRect == null || colorsSectionRect == null ||
                    colorsLabelRect == null || specsSection == null)
                {
                    context.Logger.Warn("Could not expand the repaint color section because the vanilla UI hierarchy is unavailable.");
                    return;
                }

                colorGridLayout = gridLayout;
                originalColorGridLayout = new ColorGridLayoutSnapshot(
                    gridLayout,
                    gridRect,
                    panelRect,
                    colorsSectionRect,
                    colorsLabelRect,
                    specsSection);
                colorGridBackground = gridLayout.GetComponent<Image>();
                if (colorGridBackground != null)
                {
                    colorGridBackgroundWasEnabled = colorGridBackground.enabled;
                    colorGridBackground.enabled = false;
                }

                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                specsSection.SetActive(false);
                colorsSectionRect.anchoredPosition = new Vector2(0f, colorsSectionRect.anchoredPosition.y);
                colorsSectionRect.sizeDelta = new Vector2(1050f, colorsSectionRect.sizeDelta.y);
                panelRect.sizeDelta = new Vector2(990f, panelRect.sizeDelta.y);
                gridRect.sizeDelta = new Vector2(990f, gridRect.sizeDelta.y);
                colorsLabelRect.anchoredPosition = new Vector2(30f, colorsLabelRect.anchoredPosition.y);
                colorsLabelRect.sizeDelta = new Vector2(990f, colorsLabelRect.sizeDelta.y);

                gridLayout.constraintCount = 16;
                gridLayout.cellSize = new Vector2(52f, 52f);
                gridLayout.spacing = new Vector2(7f, 7f);
                gridLayout.padding = new RectOffset(25, 25, 16, 16);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)gridLayout.transform);
            }

            public string GetLocalizeKey() => "vehicle-repainter:title";

            public float GetPurchasePrice() => RepaintPrice;

            public string GetInitialColor() => committedColorName;

            public List<(string key, string value)> GetSpecs() => new List<(string, string)>();

            public List<(string, Color32)> GetColors()
            {
                return runtime.GetRepaintColors()
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
                if (!runtime.TryResolveVehicleColor(colorName, out var vehicleColor))
                {
                    context.Logger.Warn($"Could not preview unresolved vehicle color '{colorName}'.");
                    return;
                }

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
                {
                    originalPaint.Restore(vehicle.CarFeatures);
                    if (vehicle.vehicleInstance != null)
                        vehicle.vehicleInstance.vehicleColorName = originalSavedColorName;
                }

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
                    Hue = Group == 0
                        ? 0f
                        : hue >= 0.95f && saturation >= 0.5f
                            ? hue - 1f
                            : hue;
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
                private readonly RectTransformSnapshot gridRect;
                private readonly RectTransformSnapshot panelRect;
                private readonly RectTransformSnapshot colorsSectionRect;
                private readonly RectTransformSnapshot colorsLabelRect;
                private readonly GameObject specsSection;
                private readonly bool specsSectionWasActive;

                internal ColorGridLayoutSnapshot(
                    GridLayoutGroup gridLayout,
                    RectTransform gridRect,
                    RectTransform panelRect,
                    RectTransform colorsSectionRect,
                    RectTransform colorsLabelRect,
                    GameObject specsSection)
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
                    this.gridRect = new RectTransformSnapshot(gridRect);
                    this.panelRect = new RectTransformSnapshot(panelRect);
                    this.colorsSectionRect = new RectTransformSnapshot(colorsSectionRect);
                    this.colorsLabelRect = new RectTransformSnapshot(colorsLabelRect);
                    this.specsSection = specsSection;
                    specsSectionWasActive = specsSection.activeSelf;
                }

                internal void Restore(GridLayoutGroup gridLayout)
                {
                    gridLayout.constraint = constraint;
                    gridLayout.constraintCount = constraintCount;
                    gridLayout.cellSize = cellSize;
                    gridLayout.spacing = spacing;
                    gridLayout.padding = padding;
                    gridRect.Restore();
                    panelRect.Restore();
                    colorsSectionRect.Restore();
                    colorsLabelRect.Restore();
                    specsSection.SetActive(specsSectionWasActive);
                }
            }

            private sealed class RectTransformSnapshot
            {
                private readonly RectTransform target;
                private readonly Vector2 anchoredPosition;
                private readonly Vector2 sizeDelta;

                internal RectTransformSnapshot(RectTransform target)
                {
                    this.target = target;
                    anchoredPosition = target.anchoredPosition;
                    sizeDelta = target.sizeDelta;
                }

                internal void Restore()
                {
                    if (target == null)
                        return;

                    target.anchoredPosition = anchoredPosition;
                    target.sizeDelta = sizeDelta;
                }
            }

            private string ResolveInitialColorName(VehicleController vehicle)
            {
                if (!string.IsNullOrEmpty(vehicle.vehicleInstance.vehicleColorName) &&
                    runtime.TryResolveVehicleColor(vehicle.vehicleInstance.vehicleColorName, out _))
                {
                    return vehicle.vehicleInstance.vehicleColorName;
                }

                var liveColor = vehicle.CarFeatures?.VehicleColor;
                if (liveColor != null)
                    return ((UnityEngine.Object)liveColor).name;

                var colors = runtime.GetRepaintColors();
                return colors.Count > 0 ? ((UnityEngine.Object)colors[0]).name : string.Empty;
            }

            private sealed class VehiclePaintSnapshot
            {
                private readonly VehicleColor? vehicleColor;
                private readonly List<RendererPaintSnapshot> rendererSnapshots = new List<RendererPaintSnapshot>();

                internal VehiclePaintSnapshot(CarFeatures carFeatures, VehicleColor? vehicleColor)
                {
                    this.vehicleColor = vehicleColor;
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
                    if (vehicleColor != null)
                        carFeatures.SetColor(vehicleColor);

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
