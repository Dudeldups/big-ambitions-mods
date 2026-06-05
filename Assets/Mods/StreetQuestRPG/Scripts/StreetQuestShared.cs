using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Dialogs;
using Entities;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    [Flags]
    internal enum StreetQuestPhysicalQuestGiverInstallResult
    {
        None = 0,
        RuntimeItem = 1 << 0,
        SpecialService = 1 << 1
    }

    internal static class StreetQuestShared
    {
        private const string QuestStateModDataKey = "streetquest:quest_state_v1";
        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<string, int> OriginalDialogTypesByAddress = new();
        private static readonly Dictionary<int, PatchedItemDialogTarget> OriginalDialogTypesByItemTarget = new();

        public const string HomelessContactId = "streetquest:homeless_contact";
        public const string CourierContactId = "streetquest:courier_contact";
        public const string HomelessNameKey = "streetquest:homeless_name";
        public const string CourierNameKey = "streetquest:courier_name";

        public static readonly Address HomelessAddress = new("ba:street_secondavenue", 6);
        public static readonly string[] ExperimentalItemHostNames =
        {
            "ba:itemname_casinoblackjacktable",
            "ba:itemname_casinoroulettetable"
        };

        public static StreetQuestPhysicalQuestGiverInstallResult TryInstallPhysicalQuestGiver(CallDialogType dialogType)
        {
            var result = StreetQuestPhysicalQuestGiverInstallResult.None;
            foreach (var itemHostName in ExperimentalItemHostNames)
            {
                if (TryOverrideRuntimeItemDialog(itemHostName, dialogType))
                    result |= StreetQuestPhysicalQuestGiverInstallResult.RuntimeItem;
            }

            if (TryOverrideSpecialServiceDialog(HomelessAddress, dialogType))
                result |= StreetQuestPhysicalQuestGiverInstallResult.SpecialService;

            return result;
        }

        public static void CleanupLegacyContacts()
        {
            try
            {
                SaveGameManager.Current?.Contacts?.RemoveAll(contact =>
                    contact != null && (contact.id == HomelessContactId || contact.id == CourierContactId));

                var notificationsField = typeof(Contact).GetField(
                    "AddedContactNotifications",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (notificationsField?.GetValue(null) is List<Contact> notifications)
                {
                    notifications.RemoveAll(contact =>
                        contact != null && (contact.id == HomelessContactId || contact.id == CourierContactId));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to clean legacy contacts. {exception}");
            }
        }

        public static void RestorePatchedDialogs()
        {
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
        }

        public static StreetQuestQuestDefinition GetCurrentQuest()
        {
            var record = GetQuestStateRecord();
            return StreetQuestQuestCatalog.Get(record.CurrentQuestId);
        }

        public static StreetQuestQuestProgressState GetQuestProgress(string questId)
        {
            var record = GetQuestStateRecord();
            if (record.CompletedQuestIds.Contains(questId))
                return StreetQuestQuestProgressState.Completed;

            return record.CurrentQuestId == questId
                ? record.CurrentQuestState
                : StreetQuestQuestProgressState.NotStarted;
        }

        public static bool HasIntroducedHomelessQuestline()
        {
            var record = GetQuestStateRecord();
            return record.CompletedQuestIds.Count > 0
                   || record.CurrentQuestState != StreetQuestQuestProgressState.NotStarted
                   || record.CurrentQuestId != StreetQuestQuestCatalog.FirstQuest.Id;
        }

        public static bool AcceptQuest(StreetQuestQuestDefinition quest)
        {
            if (quest == null)
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id ||
                record.CurrentQuestState != StreetQuestQuestProgressState.NotStarted)
                return false;

            record.CurrentQuestState = StreetQuestQuestProgressState.Active;
            SaveQuestStateRecord(record);
            return true;
        }

        public static bool CanTurnIn(StreetQuestQuestDefinition quest)
        {
            if (quest == null)
                return false;

            return GetPlayerItemAmount(quest.RequiredItemName) >= quest.RequiredAmount;
        }

        public static bool MarkReadyToTurnIn(StreetQuestQuestDefinition quest)
        {
            if (quest == null || !CanTurnIn(quest))
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id)
                return false;

            if (record.CurrentQuestState == StreetQuestQuestProgressState.Active)
            {
                record.CurrentQuestState = StreetQuestQuestProgressState.ReadyToTurnIn;
                SaveQuestStateRecord(record);
            }

            return record.CurrentQuestState == StreetQuestQuestProgressState.ReadyToTurnIn;
        }

        public static bool CompleteQuest(StreetQuestQuestDefinition quest)
        {
            if (quest == null || !CanTurnIn(quest) || !TryConsumeQuestItems(quest.RequiredItemName, quest.RequiredAmount))
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id)
                return false;

            GrantReward(quest.RewardAmount);
            record.CompletedQuestIds.Add(quest.Id);

            if (string.IsNullOrEmpty(quest.NextQuestId))
            {
                record.CurrentQuestId = string.Empty;
                record.CurrentQuestState = StreetQuestQuestProgressState.Completed;
            }
            else
            {
                record.CurrentQuestId = quest.NextQuestId;
                record.CurrentQuestState = StreetQuestQuestProgressState.NotStarted;
            }

            SaveQuestStateRecord(record);
            return true;
        }

        public static int GetPlayerItemAmount(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return 0;

            var holder = GetPlayerInventoryHolder();
            return holder?.GetAmountByItemName(itemName) ?? 0;
        }

        private static bool TryConsumeQuestItems(string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0)
                return false;

            var holder = GetPlayerInventoryHolder();
            if (holder == null || holder.GetAmountByItemName(itemName) < amount)
                return false;

            var remainingAmount = amount;
            var cargoInstances = holder.GetCargoInstances();
            if (cargoInstances == null)
                return false;

            foreach (var cargoInstance in cargoInstances.Where(x => x != null && x.itemName == itemName).ToList())
            {
                var amountToRemove = Math.Min(cargoInstance.amount, remainingAmount);
                holder.ReduceFromCargo(cargoInstance, amountToRemove);
                remainingAmount -= amountToRemove;
                if (remainingAmount <= 0)
                    return true;
            }

            return remainingAmount <= 0;
        }

        private static ICargoHolder GetPlayerInventoryHolder()
        {
            return PlayerHelper.GetCurrentCargoHolder();
        }

        private static void GrantReward(int rewardAmount)
        {
            if (rewardAmount <= 0)
                return;

            var transactionData = new Dictionary<string, string>
            {
                { "amount", rewardAmount.ToString() }
            };
            var transactionInfo = new TransactionInfo("streetquest:transaction_reward", transactionData, false);

            if (!GameManager.ChangeMoneySafe(rewardAmount, transactionInfo, showNotification: true))
            {
                var saveGame = SaveGameManager.Current;
                if (saveGame != null)
                    saveGame.Money += rewardAmount;
            }
        }

        private static StreetQuestQuestStateRecord GetQuestStateRecord()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.modData == null)
                return new StreetQuestQuestStateRecord();

            if (!saveGame.modData.TryGetValue(QuestStateModDataKey, out var serializedRecord))
                return new StreetQuestQuestStateRecord();

            var record = StreetQuestQuestStateRecord.Deserialize(serializedRecord);
            if (!string.IsNullOrEmpty(record.CurrentQuestId) &&
                StreetQuestQuestCatalog.Get(record.CurrentQuestId) == null)
                return new StreetQuestQuestStateRecord();

            return record;
        }

        private static void SaveQuestStateRecord(StreetQuestQuestStateRecord record)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            saveGame.modData ??= new Dictionary<string, string>();
            saveGame.modData[QuestStateModDataKey] = record.Serialize();
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

        private static string GetAddressKey(Address address) => $"{address.streetName}:{address.streetNumber}";

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            var instanceType = instance.GetType();
            var property = instanceType.GetProperty(memberName, ReflectionFlags);
            if (property != null)
                return property.GetValue(instance);

            var field = instanceType.GetField(memberName, ReflectionFlags);
            return field?.GetValue(instance);
        }

        private static bool SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return false;

            var instanceType = instance.GetType();
            var property = instanceType.GetProperty(memberName, ReflectionFlags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, ConvertMemberValue(value, property.PropertyType));
                return true;
            }

            var field = instanceType.GetField(memberName, ReflectionFlags);
            if (field == null)
                return false;

            field.SetValue(instance, ConvertMemberValue(value, field.FieldType));
            return true;
        }

        private static object ConvertMemberValue(object value, Type targetType)
        {
            if (targetType.IsEnum)
            {
                var intValue = Convert.ToInt32(value);
                return Enum.ToObject(targetType, intValue);
            }

            return Convert.ChangeType(value, targetType);
        }

        private sealed class PatchedItemDialogTarget
        {
            public string ItemName { get; set; } = string.Empty;
            public string MemberName { get; set; } = string.Empty;
            public int OriginalDialogType { get; set; }
            public object Target { get; set; }
        }
    }
}
