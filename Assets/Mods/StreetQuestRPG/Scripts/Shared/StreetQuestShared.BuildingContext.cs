using System;
using System.Reflection;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private static Type _cachedCinemachineBrainType;

        internal static void ResetIndoorBuildingContext()
        {
            StreetQuestIndoorAddressTracker.Reset();
        }

        internal static string GetCurrentIndoorBuildingAddressKey()
        {
            return StreetQuestIndoorAddressTracker.CurrentIndoorAddress;
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

        internal static bool IsCityMapOpenForGameplayChecks()
        {
            var cityMapType = FindType("CityMap");
            if (cityMapType == null)
                return false;

            var isOpenProperty = cityMapType.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
            return isOpenProperty?.GetValue(null) as bool? ?? false;
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
