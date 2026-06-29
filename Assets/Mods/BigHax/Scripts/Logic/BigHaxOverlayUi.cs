#nullable enable
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxOverlayUi
    {
        private const int WindowId = 348721;
        private static readonly string[] CustomerTrafficLabels = { "1x", "1.5x", "2x", "3x", "5x", "10x" };

        private Rect windowRect = new Rect(140f, 100f, 470f, 520f);
        private Vector2 scrollPosition = Vector2.zero;
        private int hotControlId;
        private bool isVisible;

        public bool IsVisible => isVisible;

        public void Toggle()
        {
            isVisible = !isVisible;
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

            CaptureOverlayHotControl();
            windowRect = GUI.Window(WindowId, windowRect, _ => DrawWindow(context, settings), "Big Hax");
        }

        private void DrawWindow(ModContext context, BigHaxSettings settings)
        {
            GUILayout.BeginVertical();
            GUILayout.Label($"Hotkey: {BigHaxHotkeys.GetKeyCode(settings.UiHotkeyIndex)}");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);

            DrawCustomerMultiplier(context, settings);
            DrawIntSlider(
                context,
                settings,
                "Employee Training Skill Gain",
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
                "Standard Fridge Capacity",
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
                "Pallet Shelf Capacity",
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
                "Freight Truck T1 Delivery Places",
                settings.FreightTruckT1DeliveryPlaces,
                BigHaxSettings.DefaultFreightTruckT1DeliveryPlaces,
                BigHaxTargetIds.FreightTruckT1MaxDisplayedDeliveryPlaces,
                value =>
                {
                    settings.FreightTruckT1DeliveryPlaces = value;
                    BigHaxOptionPersistence.SaveFreightTruckT1DeliveryPlaces(context.ModId, value);
                });

            var activeVehicleEnabled = GUILayout.Toggle(
                settings.EnableActiveVehicleCapacityOverride,
                "Enable Active Vehicle Capacity Override");
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
                    "Active Vehicle Capacity",
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

            GUILayout.Space(10f);
            if (GUILayout.Button("Close", GUILayout.Height(30f)))
                Hide();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private void DrawCustomerMultiplier(ModContext context, BigHaxSettings settings)
        {
            GUILayout.Label("Player Business Customer Multiplier");
            var selectedIndex = GUILayout.SelectionGrid(settings.CustomerTrafficMultiplierIndex, CustomerTrafficLabels, 3);
            if (selectedIndex == settings.CustomerTrafficMultiplierIndex)
                return;

            settings.CustomerTrafficMultiplierIndex = selectedIndex;
            BigHaxOptionPersistence.SaveCustomerTrafficMultiplierIndex(context.ModId, selectedIndex);
            BigHaxRuntime.RequestImmediateApply();
        }

        private static void DrawIntSlider(
            ModContext context,
            BigHaxSettings settings,
            string label,
            int currentValue,
            int minValue,
            int maxValue,
            System.Action<int> applyValue)
        {
            GUILayout.Label($"{label}: {currentValue}");
            var sliderValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(currentValue, minValue, maxValue));
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
    }
}
