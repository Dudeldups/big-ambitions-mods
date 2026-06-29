namespace BigHax
{
    public sealed class BigHaxSettings
    {
        public const int DefaultUiHotkeyIndex = 1;
        public const int DefaultCustomerTrafficMultiplierIndex = 0;
        public const int DefaultFreightTruckT1DeliveryPlaces = 8;
        public const int DefaultStandardFridgeCapacity = 50;
        public const int DefaultPalletShelfCapacity = 60;
        public const int DefaultActiveVehicleCapacity = 20;
        public const int DefaultEmployeeTrainingSkillIncrease = 10;

        public static readonly float[] CustomerTrafficMultiplierValues = { 1f, 1.5f, 2f, 3f, 5f, 10f };

        public bool EnableDebugLogging { get; set; } = false;

        public bool EnableActiveVehicleCapacityOverride { get; set; } = false;

        public int UiHotkeyIndex { get; set; } = DefaultUiHotkeyIndex;

        public int CustomerTrafficMultiplierIndex { get; set; } = DefaultCustomerTrafficMultiplierIndex;

        public int FreightTruckT1DeliveryPlaces { get; set; } = DefaultFreightTruckT1DeliveryPlaces;

        public int StandardFridgeCapacity { get; set; } = DefaultStandardFridgeCapacity;

        public int PalletShelfCapacity { get; set; } = DefaultPalletShelfCapacity;

        public int ActiveVehicleCapacity { get; set; } = DefaultActiveVehicleCapacity;

        public int EmployeeTrainingSkillIncrease { get; set; } = DefaultEmployeeTrainingSkillIncrease;

        public float CustomerTrafficMultiplier
        {
            get
            {
                var index = CustomerTrafficMultiplierIndex;
                if (index < 0 || index >= CustomerTrafficMultiplierValues.Length)
                    index = DefaultCustomerTrafficMultiplierIndex;

                return CustomerTrafficMultiplierValues[index];
            }
        }

        public UnityEngine.KeyCode UiHotkey => BigHaxHotkeys.GetKeyCode(UiHotkeyIndex);
    }
}
