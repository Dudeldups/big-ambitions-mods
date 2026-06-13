namespace StorageTools
{
    public sealed class StorageToolsSettings
    {
        public bool EnableDebugLogging { get; set; } = false;

        public int FreightTruckT1DeliveryPlaces { get; set; } = 8;

        public int StandardFridgeCapacity { get; set; } = 10;

        public int PalletShelfCapacity { get; set; } = 60;

        public int ActiveVehicleCapacity { get; set; } = 60;
    }
}
