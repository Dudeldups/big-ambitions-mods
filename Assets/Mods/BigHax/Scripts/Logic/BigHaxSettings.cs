namespace BigHax
{
    public sealed class BigHaxSettings
    {
        public const bool DefaultDisableCasinoBetLimit = false;
        public const bool DefaultDisableIllegalParkingPenalties = false;
        public const bool DefaultDisableInvestmentLimit = false;
        public const int DisabledCasinoBetLimitAmount = 100_000_000;
        public const long DisabledInvestmentLimitAmount = 2_100_000_000L;
        public const bool DefaultEnableVantanderMaxLoanOverride = false;
        public const int VantanderMaximumLoanOverrideAmount = 99_999_999;
        public const int DefaultUiHotkeyIndex = 1;
        public const int DefaultCustomerTrafficMultiplierIndex = 0;
        public const int DefaultFreightTruckT1DeliveryPlaces = 8;
        public const int DefaultStandardFridgeCapacity = 50;
        public const int DefaultPalletShelfCapacity = 60;
        public const int DefaultStorageShelfCapacity = 16;
        public const int DefaultActiveVehicleCapacity = 20;
        public const int DefaultEmployeeTrainingSkillIncrease = 10;
        public const bool DefaultEnableRecruitmentCandidateMaximumSkill = false;
        public const bool DefaultRemoveEmployeeDemands = false;
        public const bool DefaultEnableExtendedBedSleep = false;
        public const bool DefaultEnableNoVehicleDamage = false;
        public const bool DefaultEnableInfiniteVehicleFuel = false;
        public const bool DefaultEnableNeverDirtyVehicles = false;
        public const int RecruitmentCandidateMaximumSkillOverride = 100;

        public static readonly float[] CustomerTrafficMultiplierValues = { 1f, 1.5f, 2f, 3f, 5f, 10f };

        public bool EnableActiveVehicleCapacityOverride { get; set; } = false;

        public bool DisableCasinoBetLimit { get; set; } = DefaultDisableCasinoBetLimit;

        public bool DisableIllegalParkingPenalties { get; set; } = DefaultDisableIllegalParkingPenalties;

        public bool EnableVantanderMaxLoanOverride { get; set; } = DefaultEnableVantanderMaxLoanOverride;

        public bool DisableInvestmentLimit { get; set; } = DefaultDisableInvestmentLimit;

        public int UiHotkeyIndex { get; set; } = DefaultUiHotkeyIndex;

        public int CustomerTrafficMultiplierIndex { get; set; } = DefaultCustomerTrafficMultiplierIndex;

        public int FreightTruckT1DeliveryPlaces { get; set; } = DefaultFreightTruckT1DeliveryPlaces;

        public int StandardFridgeCapacity { get; set; } = DefaultStandardFridgeCapacity;

        public int PalletShelfCapacity { get; set; } = DefaultPalletShelfCapacity;

        public int StorageShelfCapacity { get; set; } = DefaultStorageShelfCapacity;

        public int ActiveVehicleCapacity { get; set; } = DefaultActiveVehicleCapacity;

        public int EmployeeTrainingSkillIncrease { get; set; } = DefaultEmployeeTrainingSkillIncrease;

        public bool EnableRecruitmentCandidateMaximumSkill { get; set; } = DefaultEnableRecruitmentCandidateMaximumSkill;

        public bool RemoveEmployeeDemands { get; set; } = DefaultRemoveEmployeeDemands;

        public bool EnableExtendedBedSleep { get; set; } = DefaultEnableExtendedBedSleep;

        public bool EnableNoVehicleDamage { get; set; } = DefaultEnableNoVehicleDamage;

        public bool EnableInfiniteVehicleFuel { get; set; } = DefaultEnableInfiniteVehicleFuel;

        public bool EnableNeverDirtyVehicles { get; set; } = DefaultEnableNeverDirtyVehicles;

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
