#nullable enable
namespace BigHax
{
    internal static class BigHaxOptionPersistence
    {
        public static void LoadIntoSettings(string modId, BigHaxSettings settings)
        {
            settings.CustomerTrafficMultiplierIndex = LoadCustomerTrafficMultiplierIndex(modId);

            settings.StandardFridgeCapacity = LoadInt(
                modId,
                BigHaxOptionIds.StandardFridgeCapacity,
                BigHaxSettings.DefaultStandardFridgeCapacity);

            settings.PalletShelfCapacity = LoadInt(
                modId,
                BigHaxOptionIds.PalletShelfCapacity,
                BigHaxSettings.DefaultPalletShelfCapacity);

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

        private static string BuildKey(string modId, string optionId)
        {
            return "m:" + modId + ":" + optionId;
        }
    }
}
