#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace StorageTools
{
    public sealed class StorageToolsOptions
    {
        private const string FreightTruckT1DeliveryPlacesKey = "storage_tools_freight_truck_t1_delivery_places";
        private const string StandardFridgeCapacityKey = "storage_tools_standard_fridge_capacity";
        private const string PalletShelfCapacityKey = "storage_tools_pallet_shelf_capacity";
        private const string ActiveVehicleCapacityKey = "storage_tools_active_vehicle_capacity";
        private const string ResetDefaultsKey = "storagetools_reset_defaults_label";

        private ModContext? context;
        private string? registeredModId;

        public void Initialize(ModContext modContext, StorageToolsSettings settings)
        {
            context = modContext;
            if (!string.IsNullOrEmpty(registeredModId))
                OptionsService.RemoveModOptions(registeredModId);

            OptionsService.RemoveModOptions(modContext.ModId);

            var options =
                new ModOptions()
                    .AddHeader("storagetools_options_header")
                    .AddSlider(
                        StandardFridgeCapacityKey,
                        "storagetools_standard_fridge_label",
                        StorageToolsTargetIds.SliderMinimum,
                        StorageToolsTargetIds.SliderMaximum,
                        settings.StandardFridgeCapacity,
                        value =>
                        {
                            settings.StandardFridgeCapacity = value;
                            StorageToolsRuntime.RequestImmediateApply();
                        },
                        "storagetools_capacity_value")
                    .AddSlider(
                        PalletShelfCapacityKey,
                        "storagetools_pallet_shelf_label",
                        StorageToolsTargetIds.SliderMinimum,
                        StorageToolsTargetIds.SliderMaximum,
                        settings.PalletShelfCapacity,
                        value =>
                        {
                            settings.PalletShelfCapacity = value;
                            StorageToolsRuntime.RequestImmediateApply();
                        },
                        "storagetools_capacity_value")
                    .AddSlider(
                        FreightTruckT1DeliveryPlacesKey,
                        "storagetools_freight_truck_label",
                        StorageToolsTargetIds.FreightTruckT1VanillaDisplayedDeliveryPlaces,
                        StorageToolsTargetIds.FreightTruckT1MaxDisplayedDeliveryPlaces,
                        settings.FreightTruckT1DeliveryPlaces,
                        value =>
                        {
                            settings.FreightTruckT1DeliveryPlaces = value;
                            StorageToolsRuntime.RequestImmediateApply();
                        },
                        "storagetools_capacity_value")
                    .AddSlider(
                        ActiveVehicleCapacityKey,
                        "storagetools_active_vehicle_label",
                        StorageToolsTargetIds.SliderMinimum,
                        StorageToolsTargetIds.SliderMaximum,
                        settings.ActiveVehicleCapacity,
                        value =>
                        {
                            settings.ActiveVehicleCapacity = value;
                            StorageToolsRuntime.RequestImmediateApply();
                        },
                        "storagetools_capacity_value")
                    .AddSplitter()
                    .AddButton(
                        ResetDefaultsKey,
                        () =>
                        {
                            settings.ResetToDefaults();
                            StorageToolsRuntime.RequestImmediateApply();
                            Initialize(modContext, settings);
                        });

            OptionsService.Register(modContext.ModId, options);
            registeredModId = modContext.ModId;
            StorageToolsLogger.Info(modContext, "StorageTools: options registered.");
        }

        public void Shutdown()
        {
            if (context == null)
                return;

            if (!string.IsNullOrEmpty(registeredModId))
                OptionsService.RemoveModOptions(registeredModId);

            StorageToolsLogger.Info(context, "StorageTools: options unregistered.");
            registeredModId = null;
            context = null;
        }
    }
}
