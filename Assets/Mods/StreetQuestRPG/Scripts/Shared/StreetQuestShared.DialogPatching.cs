using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        public static void RestorePatchedDialogs()
        {
            RemoveLegacyQuestGiverCtaBehaviors();
            LogDebug("RestorePatchedDialogs start");
            DestroySpawnedOutdoorQuestGiver();

            foreach (var patchedTarget in OriginalDialogTypesByItemTarget.Values.ToList())
            {
                if (patchedTarget.Target == null)
                    continue;

                try
                {
                    SetMemberValue(
                        patchedTarget.Target,
                        patchedTarget.MemberName,
                        (CallDialogType)patchedTarget.OriginalDialogType);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"StreetQuestRPG: Failed to restore item dialog for {patchedTarget.ItemName}. {exception}");
                }
            }

            OriginalDialogTypesByItemTarget.Clear();

            foreach (var originalDialogType in OriginalDialogTypesByAddress.ToList())
            {
                var splitIndex = originalDialogType.Key.LastIndexOf(':');
                if (splitIndex < 0)
                    continue;

                if (!int.TryParse(originalDialogType.Key.Substring(splitIndex + 1), out var streetNumber))
                    continue;

                var streetName = originalDialogType.Key.Substring(0, splitIndex);
                TryOverrideSpecialServiceDialog(
                    new Address(streetName, streetNumber),
                    (CallDialogType)originalDialogType.Value,
                    preserveOriginal: false);
            }

            OriginalDialogTypesByAddress.Clear();
            LogDebug("RestorePatchedDialogs complete");
        }


        private static bool TryOverrideSpecialServiceDialog(
            Address address,
            CallDialogType dialogType,
            bool preserveOriginal = true)
        {
            if (address == null)
                return false;

            try
            {
                var building = BuildingHelper.GetBuilding(address);
                if (building == null)
                    return false;

                var specialService = GetMemberValue(building, "SpecialService") ?? GetMemberValue(building, "specialService");
                if (specialService == null)
                    return false;

                var currentDialogValue = GetMemberValue(specialService, "dialogType");
                if (currentDialogValue == null)
                    return false;

                if (preserveOriginal)
                {
                    var addressKey = GetAddressKey(address);
                    if (!OriginalDialogTypesByAddress.ContainsKey(addressKey))
                        OriginalDialogTypesByAddress[addressKey] = Convert.ToInt32(currentDialogValue);
                }

                return SetMemberValue(specialService, "dialogType", dialogType);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"StreetQuestRPG: Failed to override special service dialog at {GetAddressKey(address)}. {exception}");
                return false;
            }
        }


        private static bool TryOverrideRuntimeItemDialog(
            string itemName,
            CallDialogType dialogType,
            bool preserveOriginal = true)
        {
            if (string.IsNullOrEmpty(itemName))
                return false;

            var patchedAny = false;
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null)
                    continue;

                if (!string.Equals(GetMemberValue(behaviour, "itemName") as string, itemName, StringComparison.Ordinal))
                    continue;

                var currentDialogValue = GetMemberValue(behaviour, "callDialogType");
                if (currentDialogValue == null)
                    continue;

                var instanceId = behaviour.GetInstanceID();
                if (preserveOriginal && !OriginalDialogTypesByItemTarget.ContainsKey(instanceId))
                {
                    OriginalDialogTypesByItemTarget[instanceId] = new PatchedItemDialogTarget
                    {
                        ItemName = itemName,
                        MemberName = "callDialogType",
                        OriginalDialogType = Convert.ToInt32(currentDialogValue),
                        Target = behaviour
                    };
                }

                if (SetMemberValue(behaviour, "callDialogType", dialogType))
                    patchedAny = true;
            }

            return patchedAny;
        }


        internal static void TryOpenQuestDialog(CallDialogType dialogType)
        {
            try
            {
                LogDebug($"TryOpenQuestDialog start dialogType={dialogType}");
                var dialogUiType = FindType("UI.Dialog.DialogUI");
                if (dialogUiType == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not resolve UI.Dialog.DialogUI.");

                var dialogUi = Resources.FindObjectsOfTypeAll(dialogUiType).FirstOrDefault();
                if (dialogUi == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not find a DialogUI instance.");

                var showDialogMethod = dialogUiType.GetMethod(
                    "ShowDialog",
                    ReflectionFlags,
                    null,
                    new[]
                    {
                        typeof(CallDialogType),
                        FindType("NavigationBlocker"),
                        typeof(Contact),
                        typeof(Action),
                        FindType("ThirdPersonCharacter")
                    },
                    null);

                if (showDialogMethod == null)
                    throw new InvalidOperationException("StreetQuestRPG: Could not resolve DialogUI.ShowDialog.");

                showDialogMethod.Invoke(dialogUi, new object[] { dialogType, null, null, null, null });
                LogDebug($"TryOpenQuestDialog success dialogType={dialogType}");
            }
            catch (Exception exception)
            {
                LogDebug($"TryOpenQuestDialog failed: {exception}");
                Debug.LogWarning($"StreetQuestRPG: Failed to open physical quest dialog. {exception}");
            }
        }


        private static string GetAddressKey(Address address) => $"{address.streetName}:{address.streetNumber}";


        private sealed class PatchedItemDialogTarget
        {
            public string ItemName { get; set; } = string.Empty;
            public string MemberName { get; set; } = string.Empty;
            public int OriginalDialogType { get; set; }
            public object Target { get; set; }
        }
    }
}
