namespace StorageTools
{
    public sealed class StorageToolsSettings
    {
        public const int DefaultFreightTruckT1DeliveryPlaces = 8;
        public const int DefaultStandardFridgeCapacity = 10;
        public const int DefaultPalletShelfCapacity = 60;
        public const int DefaultActiveVehicleCapacity = 20;

        public bool EnableDebugLogging { get; set; } = false;

        public int FreightTruckT1DeliveryPlaces { get; set; } = DefaultFreightTruckT1DeliveryPlaces;

        public int StandardFridgeCapacity { get; set; } = DefaultStandardFridgeCapacity;

        public int PalletShelfCapacity { get; set; } = DefaultPalletShelfCapacity;

        public int ActiveVehicleCapacity { get; set; } = DefaultActiveVehicleCapacity;

        public void ResetToDefaults()
        {
            FreightTruckT1DeliveryPlaces = DefaultFreightTruckT1DeliveryPlaces;
            StandardFridgeCapacity = DefaultStandardFridgeCapacity;
            PalletShelfCapacity = DefaultPalletShelfCapacity;
            ActiveVehicleCapacity = DefaultActiveVehicleCapacity;
        }
    }
}
