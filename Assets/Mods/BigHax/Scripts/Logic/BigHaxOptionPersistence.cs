#nullable enable
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxOptionPersistence
    {
        public static void LoadIntoSettings(string modId, BigHaxSettings settings)
        {
            settings.CustomerTrafficMultiplier = LoadInt(
                modId,
                BigHaxOptionIds.CustomerTrafficMultiplier,
                BigHaxSettings.DefaultCustomerTrafficMultiplier);

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
        }

        private static int LoadInt(string modId, string optionId, int defaultValue)
        {
            var key = BuildKey(modId, optionId);
            return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : defaultValue;
        }

        private static bool LoadBool(string modId, string optionId, bool defaultValue)
        {
            var key = BuildKey(modId, optionId);
            if (!PlayerPrefs.HasKey(key))
                return defaultValue;

            return PlayerPrefs.GetInt(key) != 0;
        }

        private static string BuildKey(string modId, string optionId)
        {
            return "m:" + modId + ":" + optionId;
        }
    }
}
