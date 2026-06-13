#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace BigHax
{
    public sealed class BigHaxOptions
    {
        private const string FreightTruckT1DeliveryPlacesKey = "big_hax_freight_truck_t1_delivery_places";
        private const string StandardFridgeCapacityKey = "big_hax_standard_fridge_capacity";
        private const string PalletShelfCapacityKey = "big_hax_pallet_shelf_capacity";
        private const string ActiveVehicleCapacityEnabledKey = "big_hax_active_vehicle_capacity_enabled";
        private const string ActiveVehicleCapacityKey = "big_hax_active_vehicle_capacity";

        private ModContext? context;
        private string? registeredModId;

        public void Initialize(ModContext modContext, BigHaxSettings settings)
        {
            context = modContext;
            if (!string.IsNullOrEmpty(registeredModId))
                OptionsService.RemoveModOptions(registeredModId);

            OptionsService.RemoveModOptions(modContext.ModId);

            var options =
                new ModOptions()
                    .AddHeader("bighax_options_header")
                    .AddSlider(
                        StandardFridgeCapacityKey,
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
                        PalletShelfCapacityKey,
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
                        FreightTruckT1DeliveryPlacesKey,
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
                        ActiveVehicleCapacityEnabledKey,
                        "bighax_active_vehicle_enabled_label",
                        settings.EnableActiveVehicleCapacityOverride,
                        value =>
                        {
                            settings.EnableActiveVehicleCapacityOverride = value;
                            BigHaxRuntime.RequestImmediateApply();
                        })
                    .AddSlider(
                        ActiveVehicleCapacityKey,
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
