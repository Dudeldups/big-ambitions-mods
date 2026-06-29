#nullable enable
using BAModAPI;
using Localizor;
using System.Collections.Generic;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxOverlayUi
    {
        private const int WindowId = 348721;
        private static readonly string[] CustomerTrafficLabels = { "1x", "1.5x", "2x", "3x", "5x", "10x" };
        private const float WindowWidth = 720f;
        private const float WindowHeight = 560f;
        private const float WindowMargin = 24f;

        private Rect windowRect = new Rect(0f, 0f, WindowWidth, WindowHeight);
        private Vector2 scrollPosition = Vector2.zero;
        private int hotControlId;
        private bool isVisible;
        private bool needsCentering = true;

        private Texture2D? solidTexture;
        private GUIStyle? windowStyle;
        private GUIStyle? titleStyle;
        private GUIStyle? subtitleStyle;
        private GUIStyle? sectionTitleStyle;
        private GUIStyle? closeButtonStyle;
        private GUIStyle? primaryButtonStyle;
        private GUIStyle? selectedButtonStyle;
        private GUIStyle? toggleStyle;
        private GUIStyle? sliderValueStyle;
        private GUIStyle? sliderTrackStyle;
        private GUIStyle? sliderFillStyle;
        private GUIStyle? sliderThumbStyle;
        private GUIStyle? verticalScrollbarStyle;
        private GUIStyle? verticalScrollbarThumbStyle;

        public bool IsVisible => isVisible;

        public void Toggle()
        {
            isVisible = !isVisible;
            if (isVisible)
                CenterWindow();
        }

        public void Hide()
        {
            isVisible = false;
        }

        public void ConsumeGameplayInputIfNeeded()
        {
            if (!isVisible)
                return;

            if (IsMouseOverWindow() || GUIUtility.hotControl == hotControlId)
                Input.ResetInputAxes();
        }

        public void OnGui(ModContext context, BigHaxSettings settings)
        {
            if (!isVisible)
                return;

            EnsureStyles();
            EnsureWindowIsCenteredIfNeeded();
            CaptureOverlayHotControl();
            windowRect = GUI.Window(WindowId, windowRect, _ => DrawWindow(context, settings), GUIContent.none, windowStyle!);
        }

        private void DrawWindow(ModContext context, BigHaxSettings settings)
        {
            GUILayout.BeginVertical();
            DrawHeader(settings);
            DrawSeparator();
            var previousVerticalScrollbar = GUI.skin.verticalScrollbar;
            var previousVerticalScrollbarThumb = GUI.skin.verticalScrollbarThumb;
            var previousHorizontalScrollbar = GUI.skin.horizontalScrollbar;
            var previousHorizontalScrollbarThumb = GUI.skin.horizontalScrollbarThumb;
            GUI.skin.verticalScrollbar = verticalScrollbarStyle!;
            GUI.skin.verticalScrollbarThumb = verticalScrollbarThumbStyle!;
            GUI.skin.horizontalScrollbar = GUIStyle.none;
            GUI.skin.horizontalScrollbarThumb = GUIStyle.none;
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);

            DrawCustomerMultiplier(context, settings);
            DrawIntSlider(
                context,
                settings,
                Localize("bighax_employee_training_skill_increase_label"),
                settings.EmployeeTrainingSkillIncrease,
                BigHaxSettings.DefaultEmployeeTrainingSkillIncrease,
                100,
                value =>
                {
                    settings.EmployeeTrainingSkillIncrease = value;
                    BigHaxOptionPersistence.SaveEmployeeTrainingSkillIncrease(context.ModId, value);
                });
            DrawIntSlider(
                context,
                settings,
                Localize(
                    "bighax_standard_fridge_capacity_label",
                    new Dictionary<string, string>
                    {
                        { "itemName", Localize("ba:itemname_standardfridge") }
                    }),
                settings.StandardFridgeCapacity,
                BigHaxSettings.DefaultStandardFridgeCapacity,
                BigHaxTargetIds.SliderMaximum,
                value =>
                {
                    settings.StandardFridgeCapacity = value;
                    BigHaxOptionPersistence.SaveStandardFridgeCapacity(context.ModId, value);
                });
            DrawIntSlider(
                context,
                settings,
                Localize(
                    "bighax_pallet_shelf_capacity_label",
                    new Dictionary<string, string>
                    {
                        { "itemName", Localize("ba:itemname_palletshelf") }
                    }),
                settings.PalletShelfCapacity,
                BigHaxSettings.DefaultPalletShelfCapacity,
                BigHaxTargetIds.SliderMaximum,
                value =>
                {
                    settings.PalletShelfCapacity = value;
                    BigHaxOptionPersistence.SavePalletShelfCapacity(context.ModId, value);
                });
            DrawIntSlider(
                context,
                settings,
                Localize(
                    "bighax_freight_truck_delivery_places_label",
                    new Dictionary<string, string>
                    {
                        { "vehicleName", Localize("ba:vehicletype_freighttruckt1") }
                    }),
                settings.FreightTruckT1DeliveryPlaces,
                BigHaxSettings.DefaultFreightTruckT1DeliveryPlaces,
                BigHaxTargetIds.FreightTruckT1MaxDisplayedDeliveryPlaces,
                value =>
                {
                    settings.FreightTruckT1DeliveryPlaces = value;
                    BigHaxOptionPersistence.SaveFreightTruckT1DeliveryPlaces(context.ModId, value);
                });

            DrawSeparator();
            var activeVehicleEnabled = GUILayout.Toggle(
                settings.EnableActiveVehicleCapacityOverride,
                Localize("bighax_active_vehicle_enabled_label"),
                toggleStyle!);
            if (activeVehicleEnabled != settings.EnableActiveVehicleCapacityOverride)
            {
                settings.EnableActiveVehicleCapacityOverride = activeVehicleEnabled;
                BigHaxOptionPersistence.SaveActiveVehicleCapacityEnabled(context.ModId, activeVehicleEnabled);
                BigHaxRuntime.RequestImmediateApply();
            }

            if (settings.EnableActiveVehicleCapacityOverride)
            {
                DrawIntSlider(
                    context,
                    settings,
                    Localize("bighax_active_vehicle_label"),
                    settings.ActiveVehicleCapacity,
                    BigHaxSettings.DefaultActiveVehicleCapacity,
                    BigHaxTargetIds.SliderMaximum,
                    value =>
                    {
                        settings.ActiveVehicleCapacity = value;
                        BigHaxOptionPersistence.SaveActiveVehicleCapacity(context.ModId, value);
                    });
            }

            GUILayout.EndScrollView();
            GUI.skin.verticalScrollbar = previousVerticalScrollbar;
            GUI.skin.verticalScrollbarThumb = previousVerticalScrollbarThumb;
            GUI.skin.horizontalScrollbar = previousHorizontalScrollbar;
            GUI.skin.horizontalScrollbarThumb = previousHorizontalScrollbarThumb;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 68f));
        }

        private void DrawCustomerMultiplier(ModContext context, BigHaxSettings settings)
        {
            DrawSectionTitle(Localize("bighax_customer_traffic_multiplier_label"));
            const float buttonGap = 10f;
            var availableWidth = windowRect.width - 44f - 22f - 14f;
            var buttonWidth = Mathf.Floor((availableWidth - (buttonGap * 2f)) / 3f);
            var selectedIndex = settings.CustomerTrafficMultiplierIndex;
            for (var row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (var column = 0; column < 3; column++)
                {
                    var optionIndex = (row * 3) + column;
                    var style = optionIndex == settings.CustomerTrafficMultiplierIndex
                        ? selectedButtonStyle!
                        : primaryButtonStyle!;
                    if (GUILayout.Button(
                            CustomerTrafficLabels[optionIndex],
                            style,
                            GUILayout.Height(36f),
                            GUILayout.Width(buttonWidth)))
                        selectedIndex = optionIndex;

                    if (column < 2)
                        GUILayout.Space(buttonGap);
                }

                GUILayout.EndHorizontal();
                if (row == 0)
                    GUILayout.Space(buttonGap);
            }

            if (selectedIndex == settings.CustomerTrafficMultiplierIndex)
                return;

            settings.CustomerTrafficMultiplierIndex = selectedIndex;
            BigHaxOptionPersistence.SaveCustomerTrafficMultiplierIndex(context.ModId, selectedIndex);
            BigHaxRuntime.RequestImmediateApply();
        }

        private void DrawIntSlider(
            ModContext context,
            BigHaxSettings settings,
            string label,
            int currentValue,
            int minValue,
            int maxValue,
            System.Action<int> applyValue)
        {
            DrawSectionTitle(label);
            GUILayout.BeginHorizontal();
            GUILayout.Label(currentValue.ToString(), sliderValueStyle!, GUILayout.Width(76f));
            var sliderRect = GUILayoutUtility.GetRect(16f, 24f, GUILayout.ExpandWidth(true));
            var sliderValue = Mathf.RoundToInt(DrawStyledSlider(sliderRect, currentValue, minValue, maxValue));
            GUILayout.EndHorizontal();
            if (sliderValue == currentValue)
                return;

            applyValue(sliderValue);
            BigHaxRuntime.RequestImmediateApply();
        }

        private void CaptureOverlayHotControl()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (hotControlId == 0)
                hotControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (!IsMouseOverWindow())
            {
                if (GUIUtility.hotControl == hotControlId &&
                    (currentEvent.type == EventType.MouseUp || currentEvent.rawType == EventType.MouseUp))
                {
                    GUIUtility.hotControl = 0;
                }

                return;
            }

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                    GUIUtility.hotControl = hotControlId;
                    currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == hotControlId)
                        GUIUtility.hotControl = 0;

                    currentEvent.Use();
                    break;
            }
        }

        private bool IsMouseOverWindow()
        {
            var mousePosition = Input.mousePosition;
            var guiMousePosition = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            return windowRect.Contains(guiMousePosition);
        }

        private void DrawHeader(BigHaxSettings settings)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("Big Hax", titleStyle!);
            GUILayout.Label(
                $"{Localize("bighax_ui_hotkey_current_label")}: {BigHaxHotkeys.GetKeyCode(settings.UiHotkeyIndex)}",
                subtitleStyle!);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Localize("bighax_ui_close_button"), closeButtonStyle!, GUILayout.Width(150f), GUILayout.Height(42f)))
                Hide();

            GUILayout.EndHorizontal();
        }

        private void DrawSectionTitle(string title)
        {
            GUILayout.Space(10f);
            GUILayout.Label(title, sectionTitleStyle!);
            GUILayout.Space(2f);
        }

        private void DrawSeparator()
        {
            GUILayout.Space(12f);
            var rect = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
            var previousColor = GUI.color;
            GUI.color = new Color(0.78f, 0.82f, 0.87f, 1f);
            GUI.DrawTexture(rect, solidTexture!);
            GUI.color = previousColor;
            GUILayout.Space(12f);
        }

        private void CenterWindow()
        {
            windowRect.width = Mathf.Min(WindowWidth, Screen.width - (WindowMargin * 2f));
            windowRect.height = Mathf.Min(WindowHeight, Screen.height - (WindowMargin * 2f));
            windowRect.x = Mathf.Max(WindowMargin, (Screen.width - windowRect.width) * 0.5f);
            windowRect.y = Mathf.Max(WindowMargin, (Screen.height - windowRect.height) * 0.5f);
            needsCentering = false;
        }

        private void EnsureWindowIsCenteredIfNeeded()
        {
            if (needsCentering)
                CenterWindow();
        }

        private void EnsureStyles()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solidTexture.SetPixel(0, 0, Color.white);
                solidTexture.Apply();
            }

            windowStyle ??= CreateWindowStyle();
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 0, 2, 2)
            };
            subtitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.47f, 0.54f, 0.62f, 1f) },
                margin = new RectOffset(0, 0, 0, 0)
            };
            sectionTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 0, 0, 0)
            };
            sliderValueStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 12, 4, 0)
            };
            closeButtonStyle ??= CreateButtonStyle(
                new Color(0.95f, 0.32f, 0.36f, 1f),
                new Color(0.89f, 0.24f, 0.28f, 1f),
                15);
            primaryButtonStyle ??= CreateButtonStyle(
                new Color(0.22f, 0.56f, 0.93f, 1f),
                new Color(0.17f, 0.47f, 0.84f, 1f),
                14);
            selectedButtonStyle ??= CreateButtonStyle(
                new Color(0.18f, 0.44f, 0.75f, 1f),
                new Color(0.15f, 0.39f, 0.68f, 1f),
                14);
            toggleStyle ??= new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                onNormal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(2, 2, 8, 4)
            };
            sliderTrackStyle ??= CreateSliderTrackStyle();
            sliderFillStyle ??= CreateSliderFillStyle();
            sliderThumbStyle ??= CreateSliderThumbStyle();
            verticalScrollbarStyle ??= CreateVerticalScrollbarStyle();
            verticalScrollbarThumbStyle ??= CreateVerticalScrollbarThumbStyle();
        }

        private GUIStyle CreateWindowStyle()
        {
            var backgroundTexture = MakeRoundedRectTexture(64, 64, new Color(0.97f, 0.97f, 0.98f, 1f), 14);
            var style = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(22, 22, 20, 20),
                border = new RectOffset(14, 14, 14, 14),
                normal =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                hover =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                active =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                focused =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                onNormal =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                onHover =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                onActive =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                },
                onFocused =
                {
                    background = backgroundTexture,
                    textColor = Color.clear
                }
            };
            return style;
        }

        private GUIStyle CreateButtonStyle(Color normalColor, Color activeColor, int fontSize)
        {
            var normalBackground = MakeRoundedRectTexture(48, 48, normalColor, 8);
            var activeBackground = MakeRoundedRectTexture(48, 48, activeColor, 8);

            return new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 40f,
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(8, 8, 8, 8),
                normal =
                {
                    background = normalBackground,
                    textColor = Color.white
                },
                hover =
                {
                    background = normalBackground,
                    textColor = Color.white
                },
                active =
                {
                    background = activeBackground,
                    textColor = Color.white
                },
                focused =
                {
                    background = normalBackground,
                    textColor = Color.white
                },
                onNormal =
                {
                    background = activeBackground,
                    textColor = Color.white
                },
                onHover =
                {
                    background = activeBackground,
                    textColor = Color.white
                },
                onActive =
                {
                    background = activeBackground,
                    textColor = Color.white
                },
                onFocused =
                {
                    background = activeBackground,
                    textColor = Color.white
                }
            };
        }

        private GUIStyle CreateSliderTrackStyle()
        {
            var background = MakeRoundedRectTexture(64, 16, new Color(0.34f, 0.38f, 0.41f, 1f), 8);
            return new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 14f,
                margin = new RectOffset(0, 0, 6, 8),
                border = new RectOffset(8, 8, 8, 8),
                normal = { background = background },
                hover = { background = background },
                active = { background = background },
                focused = { background = background }
            };
        }

        private GUIStyle CreateSliderFillStyle()
        {
            var background = MakeRoundedRectTexture(64, 16, new Color(0.59f, 0.84f, 0.31f, 1f), 8);
            return new GUIStyle
            {
                normal = { background = background },
                border = new RectOffset(8, 8, 8, 8)
            };
        }

        private GUIStyle CreateSliderThumbStyle()
        {
            var normalBackground = MakeRoundedRectTexture(28, 28, new Color(0.75f, 0.78f, 0.81f, 1f), 14);
            var activeBackground = MakeRoundedRectTexture(28, 28, new Color(0.66f, 0.69f, 0.73f, 1f), 14);
            return new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 28f,
                fixedHeight = 28f,
                margin = new RectOffset(0, 0, -7, -7),
                border = new RectOffset(14, 14, 14, 14),
                normal = { background = normalBackground },
                hover = { background = normalBackground },
                active = { background = activeBackground },
                focused = { background = normalBackground }
            };
        }

        private GUIStyle CreateVerticalScrollbarStyle()
        {
            var background = MakeRoundedRectTexture(10, 64, new Color(0.80f, 0.82f, 0.84f, 1f), 5);
            return new GUIStyle(GUI.skin.verticalScrollbar)
            {
                fixedWidth = 10f,
                margin = new RectOffset(10, 0, 0, 0),
                border = new RectOffset(5, 5, 5, 5),
                normal = { background = background },
                hover = { background = background },
                active = { background = background },
                focused = { background = background }
            };
        }

        private GUIStyle CreateVerticalScrollbarThumbStyle()
        {
            var normalBackground = MakeRoundedRectTexture(10, 32, new Color(0.92f, 0.93f, 0.94f, 1f), 5);
            var activeBackground = MakeRoundedRectTexture(10, 32, new Color(0.87f, 0.88f, 0.90f, 1f), 5);
            return new GUIStyle(GUI.skin.verticalScrollbarThumb)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                normal = { background = normalBackground },
                hover = { background = normalBackground },
                active = { background = activeBackground },
                focused = { background = normalBackground }
            };
        }

        private float DrawStyledSlider(Rect rect, int currentValue, int minValue, int maxValue)
        {
            var sliderValue = GUI.HorizontalSlider(rect, currentValue, minValue, maxValue, sliderTrackStyle!, sliderThumbStyle!);
            var clampedTrackWidth = Mathf.Max(0f, rect.width - sliderThumbStyle!.fixedWidth);
            var normalizedValue = Mathf.InverseLerp(minValue, maxValue, sliderValue);
            var fillWidth = clampedTrackWidth * normalizedValue;
            if (fillWidth > 0f)
            {
                var fillRect = new Rect(
                    rect.x,
                    rect.y,
                    fillWidth + (sliderThumbStyle.fixedWidth * 0.5f),
                    sliderTrackStyle!.fixedHeight);
                GUI.Box(fillRect, GUIContent.none, sliderFillStyle!);
            }

            sliderValue = GUI.HorizontalSlider(rect, sliderValue, minValue, maxValue, GUIStyle.none, sliderThumbStyle!);
            return sliderValue;
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private Texture2D MakeRoundedRectTexture(int width, int height, Color color, int radius)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var transparent = new Color(0f, 0f, 0f, 0f);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var drawPixel = true;

                    if (x < radius && y < radius)
                        drawPixel = IsInsideCorner(x, y, radius - 1, radius - 1, radius);
                    else if (x >= width - radius && y < radius)
                        drawPixel = IsInsideCorner(x, y, width - radius, radius - 1, radius);
                    else if (x < radius && y >= height - radius)
                        drawPixel = IsInsideCorner(x, y, radius - 1, height - radius, radius);
                    else if (x >= width - radius && y >= height - radius)
                        drawPixel = IsInsideCorner(x, y, width - radius, height - radius, radius);

                    texture.SetPixel(x, y, drawPixel ? color : transparent);
                }
            }

            texture.Apply();
            return texture;
        }

        private bool IsInsideCorner(int x, int y, int centerX, int centerY, int radius)
        {
            var deltaX = x - centerX;
            var deltaY = y - centerY;
            return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
        }

        private static string Localize(string key)
        {
            return key.Localize().ToString();
        }

        private static string Localize(string key, Dictionary<string, string> arguments)
        {
            return key.Localize(arguments).ToString();
        }
    }
}
