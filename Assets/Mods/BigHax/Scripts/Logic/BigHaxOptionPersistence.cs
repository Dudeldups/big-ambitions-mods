#nullable enable
namespace BigHax
{
    internal static class BigHaxOptionPersistence
    {
        public static void LoadIntoSettings(string modId, BigHaxSettings settings)
        {
            settings.UiHotkeyIndex = LoadInt(
                modId,
                BigHaxOptionIds.UiToggleHotkey,
                BigHaxSettings.DefaultUiHotkeyIndex);
            settings.DisableCasinoBetLimit = LoadBool(
                modId,
                BigHaxOptionIds.DisableCasinoBetLimit,
                BigHaxSettings.DefaultDisableCasinoBetLimit);
            settings.DisableIllegalParkingPenalties = LoadBool(
                modId,
                BigHaxOptionIds.DisableIllegalParkingPenalties,
                BigHaxSettings.DefaultDisableIllegalParkingPenalties);
            settings.CustomerTrafficMultiplierIndex = LoadCustomerTrafficMultiplierIndex(modId);
            settings.DisableInvestmentLimit = LoadDisableInvestmentLimit(modId);
            settings.EnableVantanderMaxLoanOverride = LoadBool(
                modId,
                BigHaxOptionIds.EnableVantanderMaxLoanOverride,
                BigHaxSettings.DefaultEnableVantanderMaxLoanOverride);

            settings.StandardFridgeCapacity = LoadInt(
                modId,
                BigHaxOptionIds.StandardFridgeCapacity,
                BigHaxSettings.DefaultStandardFridgeCapacity);

            settings.PalletShelfCapacity = LoadInt(
                modId,
                BigHaxOptionIds.PalletShelfCapacity,
                BigHaxSettings.DefaultPalletShelfCapacity);

            settings.EmployeeTrainingSkillIncrease = LoadInt(
                modId,
                BigHaxOptionIds.EmployeeTrainingSkillIncrease,
                BigHaxSettings.DefaultEmployeeTrainingSkillIncrease);

            settings.EnableRecruitmentCandidateMaximumSkill = LoadEnableRecruitmentCandidateMaximumSkill(modId);
            settings.RemoveEmployeeDemands = LoadBool(
                modId,
                BigHaxOptionIds.RemoveEmployeeDemands,
                BigHaxSettings.DefaultRemoveEmployeeDemands);

            settings.FreightTruckT1DeliveryPlaces = LoadInt(
                modId,
                BigHaxOptionIds.FreightTruckT1DeliveryPlaces,
                BigHaxSettings.DefaultFreightTruckT1DeliveryPlaces);

            settings.EnableActiveVehicleCapacityOverride = LoadBool(
                modId,
                BigHaxOptionIds.ActiveVehicleCapacityEnabled,
                false);

            settings.ActiveVehicleCapacity = LoadInt(
                modId,
                BigHaxOptionIds.ActiveVehicleCapacity,
                BigHaxSettings.DefaultActiveVehicleCapacity);

            if (settings.CustomerTrafficMultiplierIndex < 0 ||
                settings.CustomerTrafficMultiplierIndex >= BigHaxSettings.CustomerTrafficMultiplierValues.Length)
            {
                settings.CustomerTrafficMultiplierIndex = BigHaxSettings.DefaultCustomerTrafficMultiplierIndex;
            }

            settings.UiHotkeyIndex = BigHaxHotkeys.ClampIndex(settings.UiHotkeyIndex);
        }

        public static void SaveCustomerTrafficMultiplierIndex(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.CustomerTrafficMultiplier, value);
        }

        public static void SaveDisableCasinoBetLimit(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisableCasinoBetLimit, value);
        }

        public static void SaveDisableIllegalParkingPenalties(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisableIllegalParkingPenalties, value);
        }

        public static void SaveStandardFridgeCapacity(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.StandardFridgeCapacity, value);
        }

        public static void SaveDisableInvestmentLimit(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.DisableInvestmentLimit, value);
        }

        public static void SavePalletShelfCapacity(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.PalletShelfCapacity, value);
        }

        public static void SaveEnableVantanderMaxLoanOverride(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableVantanderMaxLoanOverride, value);
        }

        public static void SaveEmployeeTrainingSkillIncrease(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.EmployeeTrainingSkillIncrease, value);
        }

        public static void SaveEnableRecruitmentCandidateMaximumSkill(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.EnableRecruitmentCandidateMaximumSkill, value);
        }

        public static void SaveRemoveEmployeeDemands(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.RemoveEmployeeDemands, value);
        }

        public static void SaveFreightTruckT1DeliveryPlaces(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.FreightTruckT1DeliveryPlaces, value);
        }

        public static void SaveActiveVehicleCapacityEnabled(string modId, bool value)
        {
            SaveBool(modId, BigHaxOptionIds.ActiveVehicleCapacityEnabled, value);
        }

        public static void SaveActiveVehicleCapacity(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.ActiveVehicleCapacity, value);
        }

        public static void SaveUiHotkeyIndex(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.UiToggleHotkey, value);
        }

        public static int LoadUpdateNoticeSeenVersion(string modId)
        {
            return LoadInt(modId, BigHaxOptionIds.UpdateNoticeSeenVersion, 0);
        }

        public static void SaveUpdateNoticeSeenVersion(string modId, int value)
        {
            SaveInt(modId, BigHaxOptionIds.UpdateNoticeSeenVersion, value);
        }

        private static int LoadInt(string modId, string optionId, int defaultValue)
        {
            var key = BuildKey(modId, optionId);
            return UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetInt(key) : defaultValue;
        }

        private static int LoadCustomerTrafficMultiplierIndex(string modId)
        {
            var currentKey = BuildKey(modId, BigHaxOptionIds.CustomerTrafficMultiplier);
            if (UnityEngine.PlayerPrefs.HasKey(currentKey))
                return UnityEngine.PlayerPrefs.GetInt(currentKey);

            var legacyKey = BuildKey(modId, BigHaxOptionIds.LegacyCustomerTrafficMultiplier);
            if (!UnityEngine.PlayerPrefs.HasKey(legacyKey))
                return BigHaxSettings.DefaultCustomerTrafficMultiplierIndex;

            var legacyValue = UnityEngine.PlayerPrefs.GetInt(legacyKey);
            return MapLegacyCustomerTrafficMultiplierToIndex(legacyValue);
        }

        private static bool LoadDisableInvestmentLimit(string modId)
        {
            var currentKey = BuildKey(modId, BigHaxOptionIds.DisableInvestmentLimit);
            if (UnityEngine.PlayerPrefs.HasKey(currentKey))
                return UnityEngine.PlayerPrefs.GetInt(currentKey) != 0;

            var hundredsMillionsKey = BuildKey(modId, BigHaxOptionIds.MaximumInvestmentHundredsMillions);
            if (UnityEngine.PlayerPrefs.HasKey(hundredsMillionsKey))
                return UnityEngine.PlayerPrefs.GetInt(hundredsMillionsKey) > 10;

            var legacyKey = BuildKey(modId, BigHaxOptionIds.LegacyMaximumInvestmentBillions);
            if (!UnityEngine.PlayerPrefs.HasKey(legacyKey))
                return BigHaxSettings.DefaultDisableInvestmentLimit;

            var legacyBillions = UnityEngine.PlayerPrefs.GetInt(legacyKey);
            return legacyBillions > 1;
        }

        private static bool LoadEnableRecruitmentCandidateMaximumSkill(string modId)
        {
            var currentKey = BuildKey(modId, BigHaxOptionIds.EnableRecruitmentCandidateMaximumSkill);
            if (UnityEngine.PlayerPrefs.HasKey(currentKey))
                return UnityEngine.PlayerPrefs.GetInt(currentKey) != 0;

            var legacyKey = BuildKey(modId, BigHaxOptionIds.LegacyRecruitmentCandidateMaximumSkill);
            return UnityEngine.PlayerPrefs.HasKey(legacyKey) &&
                   UnityEngine.PlayerPrefs.GetInt(legacyKey) >= BigHaxSettings.RecruitmentCandidateMaximumSkillOverride;
        }

        private static int MapLegacyCustomerTrafficMultiplierToIndex(int legacyValue)
        {
            return legacyValue switch
            {
                <= 1 => 0,
                2 => 2,
                3 => 3,
                4 => 3,
                _ => 4
            };
        }

        private static bool LoadBool(string modId, string optionId, bool defaultValue)
        {
            var key = BuildKey(modId, optionId);
            if (!UnityEngine.PlayerPrefs.HasKey(key))
                return defaultValue;

            return UnityEngine.PlayerPrefs.GetInt(key) != 0;
        }

        private static void SaveInt(string modId, string optionId, int value)
        {
            var key = BuildKey(modId, optionId);
            UnityEngine.PlayerPrefs.SetInt(key, value);
            UnityEngine.PlayerPrefs.Save();
        }

        private static void SaveBool(string modId, string optionId, bool value)
        {
            var key = BuildKey(modId, optionId);
            UnityEngine.PlayerPrefs.SetInt(key, value ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }

        private static string BuildKey(string modId, string optionId)
        {
            return "m:" + modId + ":" + optionId;
        }
    }
}
