#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace BigHax
{
    internal static class BigHaxOptionIds
    {
        public const string UiToggleHotkey = "big_hax_ui_toggle_hotkey";
        public const string CustomerTrafficMultiplier = "big_hax_customer_traffic_multiplier_v2";
        public const string LegacyCustomerTrafficMultiplier = "big_hax_customer_traffic_multiplier";
        public const string MaximumInvestmentBillions = "big_hax_maximum_investment_billions";
        public const string FreightTruckT1DeliveryPlaces = "big_hax_freight_truck_t1_delivery_places";
        public const string StandardFridgeCapacity = "big_hax_standard_fridge_capacity";
        public const string PalletShelfCapacity = "big_hax_pallet_shelf_capacity";
        public const string ActiveVehicleCapacityEnabled = "big_hax_active_vehicle_capacity_enabled";
        public const string ActiveVehicleCapacity = "big_hax_active_vehicle_capacity";
        public const string EmployeeTrainingSkillIncrease = "big_hax_employee_training_skill_increase";
    }

    public sealed class BigHaxOptions
    {
        private ModContext? context;
        private string? registeredModId;

        public void Initialize(ModContext modContext, BigHaxSettings settings)
        {
            context = modContext;
            BigHaxOptionPersistence.LoadIntoSettings(modContext.ModId, settings);

            if (!string.IsNullOrEmpty(registeredModId))
                OptionsService.RemoveModOptions(registeredModId);

            OptionsService.RemoveModOptions(modContext.ModId);

            var options =
                new ModOptions()
                    .AddHeader("bighax_options_header")
                    .AddDropdown(
                        BigHaxOptionIds.UiToggleHotkey,
                        "bighax_ui_hotkey_label",
                        BigHaxHotkeys.ChoiceKeys,
                        settings.UiHotkeyIndex,
                        index =>
                        {
                            settings.UiHotkeyIndex = BigHaxHotkeys.ClampIndex(index);
                            BigHaxOptionPersistence.SaveUiHotkeyIndex(modContext.ModId, settings.UiHotkeyIndex);
                        });

            OptionsService.Register(modContext.ModId, options);
            registeredModId = modContext.ModId;
            BigHaxLogger.Info(modContext, "BigHax: options registered.");
        }

        public void Shutdown()
        {
            if (context == null)
                return;

            if (!string.IsNullOrEmpty(registeredModId))
                OptionsService.RemoveModOptions(registeredModId);

            BigHaxLogger.Info(context, "BigHax: options unregistered.");
            registeredModId = null;
            context = null;
        }
    }
}
