#nullable enable
using BAModAPI;
using Localizor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigHax
{
    /// <summary>
    /// Optional BAUnifiedUI-backed screen. All library calls are reflection-only so
    /// Big Hax remains loadable when the separate Workshop dependency is absent.
    /// </summary>
    internal sealed class BigHaxBaUnifiedOptionsUi
    {
        private const string MinimumLibraryVersion = "1.0.0";
        private const string RootName = "BigHax_BAUnifiedUI_Options";
        private const float PanelWidth = 740f;
        private const float PanelHeight = 720f;
        private const float HeaderScale = PanelWidth / 370f;
        private const float RowHeight = 48f;
        private const float RowGap = 6f;

        private readonly BaUiReflection api;
        private readonly Action close;
        private GameObject? root;
        private RectTransform? content;
        private ModContext? context;
        private BigHaxSettings? settings;

        private BigHaxBaUnifiedOptionsUi(BaUiReflection api, Action close)
        {
            this.api = api;
            this.close = close;
        }

        public string LibraryVersion => api.LibraryVersion;
        public string AssemblyName => api.AssemblyName;

        public static bool TryCreate(
            ModContext context,
            BigHaxSettings settings,
            bool visible,
            Action close,
            out BigHaxBaUnifiedOptionsUi? ui,
            out string reason)
        {
            ui = null;
            if (!BaUiReflection.TryResolve(MinimumLibraryVersion, out var api, out reason))
                return false;

            var candidate = new BigHaxBaUnifiedOptionsUi(api!, close);
            try
            {
                candidate.EnsureCreated(context, settings, visible);
                ui = candidate;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                candidate.Destroy();
                reason = "BAUnifiedUI initialization failed: " + exception.GetBaseException().Message;
                return false;
            }
        }

        public void EnsureCreated(ModContext modContext, BigHaxSettings currentSettings, bool visible)
        {
            context = modContext;
            settings = currentSettings;
            if (root == null)
            {
                Build();
                root!.SetActive(visible);
                if (visible && content != null)
                {
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                }
                return;
            }
            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            if (visible && root != null && !root.activeSelf)
            {
                // Rebuild on open so Localizor always uses the active game language.
                DestroyVisuals();
                Build();
            }

            if (root == null || root.activeSelf == visible)
                return;

            root.SetActive(visible);
            if (visible && content != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
        }

        public void ConsumeGameplayInputIfNeeded(bool visible)
        {
            if (visible)
                Input.ResetInputAxes();
        }

        public void Destroy()
        {
            DestroyVisuals();
            context = null;
            settings = null;
        }

        private void Build()
        {
            if (context == null || settings == null)
                throw new InvalidOperationException("Big Hax UI context is not initialized.");

            BigHaxUiDebugLogger.Log($"Building BAUnifiedUI {api.LibraryVersion} screen from {api.AssemblyName}.");
            api.EnsureEventSystem();

            root = new GameObject(RootName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);
            api.SetupOverlayCanvas(root, short.MaxValue - 8, interactive: true);
            api.CreateModalDimmer(root.transform, 0.62f, new UnityAction(close));

            var panel = api.BuildPanel(root.transform, PanelWidth, PanelHeight, "BigHaxOptions", out var header);
            api.CreateHeaderTitle(header, Localize("bighax_options_header"), HeaderScale, rightIconCount: 1);
            api.CreateHeaderCloseButton(header, new UnityAction(close));
            api.TryAttachDraggableWindow(panel, header, "BigHax.Options.BAUnifiedUI");

            var viewportObject = new GameObject("OptionsViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.transform.SetParent(panel, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(28f, 64f);
            viewport.offsetMax = new Vector2(-44f, -56f);
            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = api.ListInsetColor;
            viewportImage.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewport, false);
            content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = RowGap;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbar = CreateScrollbar(panel);
            var scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            AddToggle(Localize("bighax_disable_casino_bet_limit_label"),
                () => settings.DisableCasinoBetLimit,
                value => { settings.DisableCasinoBetLimit = value; BigHaxOptionPersistence.SaveDisableCasinoBetLimit(context.ModId, value); });
            AddToggle(Localize("bighax_disable_illegal_parking_penalties_label"),
                () => settings.DisableIllegalParkingPenalties,
                value => { settings.DisableIllegalParkingPenalties = value; BigHaxOptionPersistence.SaveDisableIllegalParkingPenalties(context.ModId, value); });
            AddToggle(Localize("bighax_disable_investment_limit_label"),
                () => settings.DisableInvestmentLimit,
                value => { settings.DisableInvestmentLimit = value; BigHaxOptionPersistence.SaveDisableInvestmentLimit(context.ModId, value); });
            AddToggle(Localize("bighax_vantander_maximum_loan_override_label"),
                () => settings.EnableVantanderMaxLoanOverride,
                value => { settings.EnableVantanderMaxLoanOverride = value; BigHaxOptionPersistence.SaveEnableVantanderMaxLoanOverride(context.ModId, value); });
            AddToggle(Localize("bighax_enable_recruitment_candidate_maximum_skill_label"),
                () => settings.EnableRecruitmentCandidateMaximumSkill,
                value => { settings.EnableRecruitmentCandidateMaximumSkill = value; BigHaxOptionPersistence.SaveEnableRecruitmentCandidateMaximumSkill(context.ModId, value); });
            AddToggle(Localize("bighax_remove_employee_demands_label"),
                () => settings.RemoveEmployeeDemands,
                value => { settings.RemoveEmployeeDemands = value; BigHaxOptionPersistence.SaveRemoveEmployeeDemands(context.ModId, value); });
            AddToggle(Localize("bighax_active_vehicle_enabled_label"),
                () => settings.EnableActiveVehicleCapacityOverride,
                value => { settings.EnableActiveVehicleCapacityOverride = value; BigHaxOptionPersistence.SaveActiveVehicleCapacityEnabled(context.ModId, value); });

            AddSlider(Localize("bighax_customer_traffic_multiplier_label"),
                () => settings.CustomerTrafficMultiplierIndex, 0, 5,
                value => { settings.CustomerTrafficMultiplierIndex = value; BigHaxOptionPersistence.SaveCustomerTrafficMultiplierIndex(context.ModId, value); },
                value => new[] { "1x", "1.5x", "2x", "3x", "5x", "10x" }[value]);
            AddSlider(Localize("bighax_employee_training_skill_increase_label"),
                () => settings.EmployeeTrainingSkillIncrease, 10, 100,
                value => { settings.EmployeeTrainingSkillIncrease = value; BigHaxOptionPersistence.SaveEmployeeTrainingSkillIncrease(context.ModId, value); }, value => value.ToString());
            AddSlider(Localize("bighax_standard_fridge_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_standardfridge") } }),
                () => settings.StandardFridgeCapacity, 50, 1000,
                value => { settings.StandardFridgeCapacity = value; BigHaxOptionPersistence.SaveStandardFridgeCapacity(context.ModId, value); }, value => value.ToString());
            AddSlider(Localize("bighax_pallet_shelf_capacity_label", new Dictionary<string, string> { { "itemName", Localize("ba:itemname_palletshelf") } }),
                () => settings.PalletShelfCapacity, 60, 1000,
                value => { settings.PalletShelfCapacity = value; BigHaxOptionPersistence.SavePalletShelfCapacity(context.ModId, value); }, value => value.ToString());
            AddSlider(Localize("bighax_freight_truck_delivery_places_label", new Dictionary<string, string> { { "vehicleName", Localize("ba:vehicletype_freighttruckt1") } }),
                () => settings.FreightTruckT1DeliveryPlaces, 8, BigHaxTargetIds.FreightTruckT1MaxDisplayedDeliveryPlaces,
                value => { settings.FreightTruckT1DeliveryPlaces = value; BigHaxOptionPersistence.SaveFreightTruckT1DeliveryPlaces(context.ModId, value); }, value => value.ToString());
            AddSlider(Localize("bighax_active_vehicle_label"),
                () => settings.ActiveVehicleCapacity, 20, 1000,
                value => { settings.ActiveVehicleCapacity = value; BigHaxOptionPersistence.SaveActiveVehicleCapacity(context.ModId, value); }, value => value.ToString());

            AddFooter(panel);
            api.ApplyUiLayer(root);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = Vector2.zero;
            root.SetActive(false);
            BigHaxUiDebugLogger.Log($"BAUnifiedUI screen built with {content.childCount} option rows.");
        }

        private void AddToggle(string label, Func<bool> read, Action<bool> write)
        {
            var row = CreateRow("ToggleRow");
            var labelText = api.CreateBodyText(row.transform, label, muted: false, "Label");
            SetRect(labelText.Rect, new Vector2(0f, 0f), new Vector2(0.82f, 1f), new Vector2(16f, 3f), new Vector2(-8f, -3f));

            var toggleObject = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(row.transform, false);
            var toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(1f, 0.5f);
            toggleRect.pivot = new Vector2(1f, 0.5f);
            toggleRect.anchoredPosition = new Vector2(-18f, 0f);
            toggleRect.sizeDelta = new Vector2(64f, 30f);

            var trackObject = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackObject.transform.SetParent(toggleRect, false);
            var trackRect = trackObject.GetComponent<RectTransform>();
            Stretch(trackRect);
            var track = trackObject.GetComponent<Image>();
            track.raycastTarget = true;

            var knobObject = new GameObject("Knob", typeof(RectTransform), typeof(Image));
            knobObject.transform.SetParent(trackRect, false);
            var knobRect = knobObject.GetComponent<RectTransform>();
            knobRect.anchorMin = knobRect.anchorMax = new Vector2(0.5f, 0.5f);
            knobRect.sizeDelta = new Vector2(24f, 24f);
            var knob = knobObject.GetComponent<Image>();
            knob.color = Color.white;
            knob.raycastTarget = false;

            void UpdateAppearance(bool value)
            {
                track.color = value ? api.AccentGreenColor : new Color(api.MutedTextColor.r, api.MutedTextColor.g, api.MutedTextColor.b, 0.42f);
                knobRect.anchoredPosition = new Vector2(value ? 16f : -16f, 0f);
            }

            var toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.graphic = null;
            toggle.isOn = read();
            UpdateAppearance(toggle.isOn);
            toggle.onValueChanged.AddListener(value =>
            {
                UpdateAppearance(value);
                write(value);
                BigHaxRuntime.RequestImmediateApply();
            });
        }

        private void AddSlider(string label, Func<int> read, int min, int max, Action<int> write, Func<int, string> format)
        {
            var row = CreateRow("SliderRow");
            var labelText = api.CreateBodyText(row.transform, label, muted: false, "Label");
            SetRect(labelText.Rect, Vector2.zero, new Vector2(0.56f, 1f), new Vector2(16f, 3f), new Vector2(-6f, -3f));
            var valueText = api.CreateBodyText(row.transform, format(read()), muted: false, "Value");
            var valueRect = valueText.Rect;
            SetRect(valueRect, new Vector2(0.56f, 0f), new Vector2(0.67f, 1f), new Vector2(2f, 3f), new Vector2(-4f, -3f));

            var sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(Slider), typeof(EventTrigger));
            sliderObject.transform.SetParent(row.transform, false);
            var sliderRect = sliderObject.GetComponent<RectTransform>();
            SetRect(sliderRect, new Vector2(0.68f, 0.5f), new Vector2(0.97f, 0.5f), Vector2.zero, Vector2.zero);
            sliderRect.sizeDelta = new Vector2(0f, 28f);
            var inputSurface = sliderObject.GetComponent<Image>();
            inputSurface.color = Color.clear;
            inputSurface.raycastTarget = true;

            var trackObject = new GameObject("Track", typeof(RectTransform), typeof(Image));
            trackObject.transform.SetParent(sliderRect, false);
            var trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.42f);
            trackRect.anchorMax = new Vector2(1f, 0.58f);
            trackRect.offsetMin = trackRect.offsetMax = Vector2.zero;
            var track = trackObject.GetComponent<Image>();
            track.color = api.ListInsetColor;
            track.raycastTarget = false;

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(trackRect, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            Stretch(fillRect);
            var fill = fillObject.GetComponent<Image>();
            fill.color = api.AccentGreenColor;
            fill.raycastTarget = false;

            // Slider rewrites both axes of its assigned handleRect. Give it a tiny
            // invisible drag target and keep the visible square as its child: this
            // preserves Unity's native pointer/drag path without stretching the art.
            var dragHandleObject = new GameObject("Drag Handle", typeof(RectTransform));
            dragHandleObject.transform.SetParent(sliderRect, false);
            var dragHandleRect = dragHandleObject.GetComponent<RectTransform>();
            dragHandleRect.anchorMin = dragHandleRect.anchorMax = new Vector2(0.5f, 0.5f);
            dragHandleRect.sizeDelta = new Vector2(2f, 2f);
            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(dragHandleRect, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(24f, 24f);
            var handle = handleObject.GetComponent<Image>();
            handle.color = api.ButtonGreenColor;

            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            // Unity's automatic fillRect math reserves handle width and produces a
            // vertical block at minimum values. Keep the real handle behavior, but
            // drive the thin track fill explicitly for a stable vanilla appearance.
            slider.fillRect = null;
            slider.handleRect = dragHandleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = read();
            void UpdateAppearance(float raw)
            {
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(Mathf.InverseLerp(min, max, raw), 1f);
                fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            }
            UpdateAppearance(slider.value);
            slider.onValueChanged.AddListener(raw =>
            {
                var value = Mathf.RoundToInt(raw);
                UpdateAppearance(raw);
                valueText.Text = format(value);
                write(value);
                BigHaxRuntime.RequestImmediateApply();
            });

            void ApplyPointer(BaseEventData rawEventData)
            {
                var eventData = rawEventData as PointerEventData;
                if (eventData == null || !slider.interactable)
                    return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        sliderRect, eventData.position, eventData.pressEventCamera, out var localPoint))
                    return;

                var normalized = Mathf.InverseLerp(sliderRect.rect.xMin, sliderRect.rect.xMax, localPoint.x);
                slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, normalized);
            }

            var trigger = sliderObject.GetComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();
            foreach (var eventType in new[]
            {
                EventTriggerType.PointerDown,
                EventTriggerType.BeginDrag,
                EventTriggerType.Drag,
                EventTriggerType.EndDrag
            })
            {
                var entry = new EventTrigger.Entry { eventID = eventType };
                entry.callback.AddListener(ApplyPointer);
                trigger.triggers.Add(entry);
            }
        }

        private GameObject CreateRow(string name)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(content!, false);
            var image = row.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.035f);
            image.raycastTarget = false;
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = RowHeight;
            element.preferredHeight = RowHeight;
            element.flexibleHeight = 0f;
            return row;
        }

        private Scrollbar CreateScrollbar(RectTransform panel)
        {
            var scrollbarObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(panel, false);
            var rect = scrollbarObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-28f, 4f);
            rect.sizeDelta = new Vector2(10f, -132f);
            var track = scrollbarObject.GetComponent<Image>();
            track.color = api.ListInsetColor;

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(rect, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            Stretch(handleRect);
            var handle = handleObject.GetComponent<Image>();
            handle.color = api.MutedTextColor;
            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handle;
            scrollbar.handleRect = handleRect;
            return scrollbar;
        }

        private void AddFooter(RectTransform panel)
        {
            var status = api.CreateBodyText(panel, $"LIB BA UNIFIED UI {api.LibraryVersion}", muted: true, "LibraryStatus");
            SetRect(status.Rect, Vector2.zero, new Vector2(0.55f, 0f), new Vector2(32f, 13f), new Vector2(-4f, 51f));

            var button = api.CreateVanillaButton(panel, Localize("bighax_ui_close_button"), 160f, 36f, new UnityAction(close), "Blue", 15f);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-30f, 14f);
            rect.sizeDelta = new Vector2(160f, 36f);
        }

        private void DestroyVisuals()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
            root = null;
            content = null;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static string Localize(string key) => key.Localize().ToString();
        private static string Localize(string key, Dictionary<string, string> arguments) => key.Localize(arguments).ToString();

        private sealed class BaUiText
        {
            private readonly object component;
            private readonly PropertyInfo textProperty;

            public BaUiText(Component component, PropertyInfo textProperty)
            {
                this.component = component;
                this.textProperty = textProperty;
                Rect = component.GetComponent<RectTransform>();
            }

            public RectTransform Rect { get; }

            public string Text
            {
                set => textProperty.SetValue(component, value);
            }
        }

        private sealed class BaUiReflection
        {
            private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
            private readonly Type textType;
            private readonly Type buttonStyleType;
            private readonly MethodInfo ensureEventSystem;
            private readonly MethodInfo setupOverlayCanvas;
            private readonly MethodInfo createModalDimmer;
            private readonly MethodInfo buildPanel;
            private readonly MethodInfo createHeaderTitle;
            private readonly MethodInfo createHeaderCloseButton;
            private readonly MethodInfo createVanillaButton;
            private readonly MethodInfo applyBodyStyle;
            private readonly MethodInfo applyUiLayer;
            private readonly MethodInfo? attachDraggableWindow;

            private BaUiReflection(Assembly assembly, string libraryVersion, Type bootstrap, Type chrome, Type widgets)
            {
                AssemblyName = assembly.GetName().Name ?? "LIB_BaUnifiedUI";
                LibraryVersion = libraryVersion;
                buttonStyleType = RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Chrome.BaVanillaButtonStyle");
                ensureEventSystem = RequireMethod(bootstrap, "EnsureEventSystem", 1);
                setupOverlayCanvas = RequireMethod(chrome, "SetupOverlayCanvas", 3);
                createModalDimmer = RequireMethod(widgets, "CreateModalDimmer", 3);
                buildPanel = RequireMethod(chrome, "BuildPanel", 5);
                createHeaderTitle = RequireMethod(widgets, "CreateHeaderTitleLeft", 5);
                createHeaderCloseButton = RequireMethod(chrome, "CreateHeaderCloseButton", 2);
                createVanillaButton = RequireMethod(chrome, "CreateVanillaButton", 10);
                applyBodyStyle = RequireMethod(chrome, "ApplyBodyStyle", 3);
                textType = applyBodyStyle.GetParameters()[0].ParameterType;
                applyUiLayer = RequireMethod(chrome, "ApplyUiLayer", 1);
                attachDraggableWindow = chrome.GetMethods(PublicStatic)
                    .FirstOrDefault(method => method.Name == "AttachDraggableWindow" && method.GetParameters().Length == 3);

                BodyTextColor = ReadColor(chrome, "BodyTextColor", Color.white);
                MutedTextColor = ReadColor(chrome, "MutedTextColor", new Color(0.72f, 0.76f, 0.8f, 1f));
                AccentGreenColor = ReadColor(chrome, "AccentGreenColor", new Color(0.35f, 0.95f, 0.45f, 1f));
                ListInsetColor = ReadColor(chrome, "ListInsetColor", new Color(0f, 0f, 0f, 0.22f));
                ButtonGreenColor = ReadColor(chrome, "ButtonGreenFallback", new Color(0.28f, 0.72f, 0.38f, 1f));
            }

            public string AssemblyName { get; }
            public string LibraryVersion { get; }
            public Color BodyTextColor { get; }
            public Color MutedTextColor { get; }
            public Color AccentGreenColor { get; }
            public Color ListInsetColor { get; }
            public Color ButtonGreenColor { get; }

            public static bool TryResolve(string minimumVersion, out BaUiReflection? api, out string reason)
            {
                api = null;
                var versionTypeName = "Capisoft.Lib.BaUnifiedUI.Core.BaUiVersion";
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => candidate.GetType(versionTypeName, throwOnError: false) != null);
                if (assembly == null)
                {
                    reason = "LIB_BaUnifiedUI is not loaded";
                    return false;
                }

                try
                {
                    var versionType = RequireType(assembly, versionTypeName);
                    var field = versionType.GetField("Version", PublicStatic)
                        ?? throw new MissingFieldException(versionType.FullName, "Version");
                    var libraryVersion = (field.GetRawConstantValue() ?? field.GetValue(null))?.ToString() ?? string.Empty;
                    if (!TryParseVersion(libraryVersion, out var parsed) ||
                        !TryParseVersion(minimumVersion, out var minimum) ||
                        parsed < minimum)
                    {
                        reason = $"LIB_BaUnifiedUI {libraryVersion} is below required {minimumVersion}";
                        return false;
                    }

                    api = new BaUiReflection(
                        assembly,
                        libraryVersion,
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Core.BaUiBootstrap"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Chrome.BaUiWidePanelChrome"),
                        RequireType(assembly, "Capisoft.Lib.BaUnifiedUI.Controls.BaUiWidgets"));
                    reason = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    reason = "LIB_BaUnifiedUI API is incompatible: " + exception.GetBaseException().Message;
                    return false;
                }
            }

            public void EnsureEventSystem() => Invoke(ensureEventSystem, null, new object?[] { "BigHax_BAUnifiedUI_EventSystem" });

            public void SetupOverlayCanvas(GameObject root, int sortingOrder, bool interactive) =>
                Invoke(setupOverlayCanvas, null, new object?[] { root, sortingOrder, interactive });

            public void CreateModalDimmer(Transform parent, float alpha, UnityAction onClick) =>
                Invoke(createModalDimmer, null, new object?[] { parent, alpha, onClick });

            public RectTransform BuildPanel(Transform parent, float width, float height, string name, out RectTransform header)
            {
                var arguments = new object?[] { parent, width, height, name, null };
                var result = Invoke(buildPanel, null, arguments) as RectTransform
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create a panel.");
                header = arguments[4] as RectTransform
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create a panel header.");
                return result;
            }

            public void CreateHeaderTitle(Transform header, string text, float scale, int rightIconCount) =>
                Invoke(createHeaderTitle, null, new object?[] { header, text, scale, rightIconCount, true });

            public void CreateHeaderCloseButton(Transform header, UnityAction onClick) =>
                Invoke(createHeaderCloseButton, null, new object?[] { header, onClick });

            public Button CreateVanillaButton(Transform parent, string label, float width, float height, UnityAction onClick, string style, float fontSize)
            {
                var styleValue = Enum.Parse(buttonStyleType, style, ignoreCase: true);
                return Invoke(createVanillaButton, null,
                    new object?[] { parent, label, width, height, 1f, onClick, styleValue, fontSize, true, null }) as Button
                    ?? throw new InvalidOperationException("BAUnifiedUI did not create a button.");
            }

            public BaUiText CreateBodyText(Transform parent, string text, bool muted, string name)
            {
                var gameObject = new GameObject(name, typeof(RectTransform));
                gameObject.transform.SetParent(parent, false);
                var component = gameObject.AddComponent(textType);
                Invoke(applyBodyStyle, null, new object?[] { component, 1.05f, muted });
                if (component is Graphic graphic)
                    graphic.raycastTarget = false;
                var property = textType.GetProperty("text", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new MissingMemberException(textType.FullName, "text");
                property.SetValue(component, text);
                return new BaUiText(component, property);
            }

            public void ApplyUiLayer(GameObject root) => Invoke(applyUiLayer, null, new object?[] { root });

            public void TryAttachDraggableWindow(RectTransform panel, RectTransform header, string persistentId)
            {
                if (attachDraggableWindow == null)
                    return;
                try
                {
                    Invoke(attachDraggableWindow, null, new object?[] { panel, header, persistentId });
                }
                catch (Exception exception)
                {
                    BigHaxUiDebugLogger.Log("BAUnifiedUI draggable-window attachment skipped: " + exception.GetBaseException().Message);
                }
            }

            private static Type RequireType(Assembly assembly, string fullName) =>
                assembly.GetType(fullName, throwOnError: false)
                ?? throw new TypeLoadException("Missing BAUnifiedUI type " + fullName);

            private static MethodInfo RequireMethod(Type type, string name, int parameterCount) =>
                type.GetMethods(PublicStatic).FirstOrDefault(method => method.Name == name && method.GetParameters().Length == parameterCount)
                ?? throw new MissingMethodException(type.FullName, name);

            private static Color ReadColor(Type type, string name, Color fallback)
            {
                var field = type.GetField(name, PublicStatic);
                return field?.GetValue(null) is Color value ? value : fallback;
            }

            private static object? Invoke(MethodInfo method, object? target, object?[] arguments)
            {
                try
                {
                    return method.Invoke(target, arguments);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    throw exception.InnerException;
                }
            }

            private static bool TryParseVersion(string value, out Version version)
            {
                var normalized = (value ?? string.Empty).Split('-', '+')[0];
                return Version.TryParse(normalized, out version);
            }
        }
    }
}
