#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace BigHax
{
    internal static class BigHaxOptionIds
    {
        public const string CustomerTrafficMultiplier = "big_hax_customer_traffic_multiplier_v2";
        public const string LegacyCustomerTrafficMultiplier = "big_hax_customer_traffic_multiplier";
        public const string FreightTruckT1DeliveryPlaces = "big_hax_freight_truck_t1_delivery_places";
        public const string StandardFridgeCapacity = "big_hax_standard_fridge_capacity";
        public const string PalletShelfCapacity = "big_hax_pallet_shelf_capacity";
        public const string ActiveVehicleCapacityEnabled = "big_hax_active_vehicle_capacity_enabled";
        public const string ActiveVehicleCapacity = "big_hax_active_vehicle_capacity";
    }

    public sealed class BigHaxOptions
    {
        private static readonly string[] CustomerTrafficMultiplierChoices =
        {
            "bighax_customer_traffic_multiplier_1_0",
            "bighax_customer_traffic_multiplier_1_5",
            "bighax_customer_traffic_multiplier_2_0",
            "bighax_customer_traffic_multiplier_3_0",
            "bighax_customer_traffic_multiplier_5_0",
            "bighax_customer_traffic_multiplier_10_0"
        };

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
                        BigHaxOptionIds.CustomerTrafficMultiplier,
                        "bighax_customer_traffic_multiplier_label",
                        CustomerTrafficMultiplierChoices,
                        settings.CustomerTrafficMultiplierIndex,
                        index =>
                        {
                            settings.CustomerTrafficMultiplierIndex = index;
                            BigHaxRuntime.RequestImmediateApply();
                        })
                    .AddSlider(
                        BigHaxOptionIds.StandardFridgeCapacity,
                        "bighax_standard_fridge_label",
                        BigHaxSettings.DefaultStandardFridgeCapacity,
                        BigHaxTargetIds.SliderMaximum,
                        settings.StandardFridgeCapacity,
                        value =>
                        {
                            settings.StandardFridgeCapacity = value;
                            BigHaxRuntime.RequestImmediateApply();
                        },
                        "bighax_capacity_value")
                    .AddSlider(
                        BigHaxOptionIds.PalletShelfCapacity,
                        "bighax_pallet_shelf_label",
                        BigHaxSettings.DefaultPalletShelfCapacity,
                        BigHaxTargetIds.SliderMaximum,
                        settings.PalletShelfCapacity,
                        value =>
                        {
                            settings.PalletShelfCapacity = value;
                            BigHaxRuntime.RequestImmediateApply();
                        },
                        "bighax_capacity_value")
                    .AddSlider(
                        BigHaxOptionIds.FreightTruckT1DeliveryPlaces,
                        "bighax_freight_truck_label",
                        BigHaxSettings.DefaultFreightTruckT1DeliveryPlaces,
                        BigHaxTargetIds.FreightTruckT1MaxDisplayedDeliveryPlaces,
                        settings.FreightTruckT1DeliveryPlaces,
                        value =>
                        {
                            settings.FreightTruckT1DeliveryPlaces = value;
                            BigHaxRuntime.RequestImmediateApply();
                        },
                        "bighax_capacity_value")
                    .AddToggle(
                        BigHaxOptionIds.ActiveVehicleCapacityEnabled,
                        "bighax_active_vehicle_enabled_label",
                        settings.EnableActiveVehicleCapacityOverride,
                        value =>
                        {
                            settings.EnableActiveVehicleCapacityOverride = value;
                            BigHaxRuntime.RequestImmediateApply();
                        })
                    .AddSlider(
                        BigHaxOptionIds.ActiveVehicleCapacity,
                        "bighax_active_vehicle_label",
                        BigHaxSettings.DefaultActiveVehicleCapacity,
                        BigHaxTargetIds.SliderMaximum,
                        settings.ActiveVehicleCapacity,
                        value =>
                        {
                            settings.ActiveVehicleCapacity = value;
                            BigHaxRuntime.RequestImmediateApply();
                        },
                        "bighax_capacity_value");

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
