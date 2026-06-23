using System;
using System.Reflection;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private static Type _cachedCinemachineBrainType;
        private static Type _cachedCityMapType;
        private static PropertyInfo _cachedCityMapIsOpenProperty;
        private static int _cachedCityMapOpenFrame = -1;
        private static bool _cachedCityMapOpenValue;

        internal static void ResetIndoorBuildingContext()
        {
            ResetCityMapOpenCache();
            StreetQuestIndoorAddressTracker.Reset();
        }

        internal static string GetCurrentIndoorBuildingAddressKey()
        {
            return StreetQuestIndoorAddressTracker.CurrentIndoorAddress;
        }

        internal static string GetCurrentExteriorBuildingAddressKey()
        {
            return StreetQuestIndoorAddressTracker.CurrentExteriorAddressCandidate;
        }

        internal static string GetIndoorContextDisplayText()
        {
            var currentAddress = GetCurrentIndoorBuildingAddressKey();
            if (!string.IsNullOrWhiteSpace(currentAddress))
                return currentAddress;

            return StreetQuestIndoorAddressTracker.IsIndoors
                ? "Indoors / unresolved"
                : "Outdoors / none";
        }

        internal static string GetLiveVirtualCameraPathForDebug()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            return liveVirtualCamera == null ? "<none>" : GetHierarchyPath(liveVirtualCamera.transform);
        }

        internal static bool SetCurrentIndoorBuildingAddressKey(string addressKey)
        {
            return StreetQuestIndoorAddressTracker.SetCurrentIndoorAddress(addressKey);
        }

        internal static bool DoesBuildingContextMatch(StreetQuestCharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.buildingAddress))
                return true;

            var requiredAddress = NormalizeAddressKey(definition.buildingAddress);
            var currentAddress = GetCurrentIndoorBuildingAddressKey();
            if (string.IsNullOrWhiteSpace(currentAddress))
                return false;

            return string.Equals(requiredAddress, currentAddress, StringComparison.Ordinal);
        }

        internal static bool IsIndoorGameplayContextActive()
        {
            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            if (liveVirtualCamera == null)
                return false;

            var path = GetHierarchyPath(liveVirtualCamera.transform);
            return path.IndexOf("IndoorCam", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   path.IndexOf("VehicleCam", StringComparison.OrdinalIgnoreCase) < 0 &&
                   path.IndexOf("IndoorVehicle", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static bool IsCityMapOpen()
        {
            if (_cachedCityMapOpenFrame == Time.frameCount)
                return _cachedCityMapOpenValue;

            _cachedCityMapOpenFrame = Time.frameCount;
            _cachedCityMapOpenValue = ResolveCityMapOpenUncached();
            return _cachedCityMapOpenValue;
        }

        internal static bool IsCityMapOpenForGameplayChecks() => IsCityMapOpen();

        internal static void ResetCityMapOpenCache()
        {
            _cachedCityMapType = null;
            _cachedCityMapIsOpenProperty = null;
            _cachedCityMapOpenFrame = -1;
            _cachedCityMapOpenValue = false;
        }

        private static bool ResolveCityMapOpenUncached()
        {
            _cachedCityMapType ??= FindType("CityMap");
            if (_cachedCityMapType == null)
                return false;

            _cachedCityMapIsOpenProperty ??= _cachedCityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return _cachedCityMapIsOpenProperty?.GetValue(null) as bool? ?? false;
        }

        private static Component GetLiveVirtualCameraComponent()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
                return null;

            _cachedCinemachineBrainType ??= FindType("CinemachineBrain") ?? FindType("Cinemachine.CinemachineBrain");
            if (_cachedCinemachineBrainType == null)
                return null;

            var brain = mainCamera.GetComponent(_cachedCinemachineBrainType);
            if (brain == null)
                return null;

            var activeVirtualCamera = GetMemberValue(brain, "ActiveVirtualCamera");
            if (activeVirtualCamera is Component component)
                return component;

            var virtualCameraGameObject = GetMemberValue(activeVirtualCamera, "VirtualCameraGameObject") as GameObject;
            return virtualCameraGameObject != null ? virtualCameraGameObject.GetComponent<Component>() : null;
        }

        private static string NormalizeAddressKey(string addressKey)
        {
            return string.IsNullOrWhiteSpace(addressKey)
                ? string.Empty
                : addressKey.Trim().ToLowerInvariant();
        }

        internal static bool PersistIndoorBuildingAddressKey(string addressKey)
        {
            return SetPersistedIndoorBuildingAddress(NormalizeAddressKey(addressKey));
        }

        internal static string GetPersistedIndoorBuildingAddressKey()
        {
            return NormalizeAddressKey(GetPersistedIndoorBuildingAddress());
        }
    }
}
