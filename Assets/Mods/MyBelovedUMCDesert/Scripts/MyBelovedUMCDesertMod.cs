#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Helpers;
using Services;
using UI;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(MyBelovedUMCDesert.MyBelovedUMCDesertMod))]

namespace MyBelovedUMCDesert
{
    [ModEntryOnInitializationLoad]
    public sealed class MyBelovedUMCDesertMod : IModBigAmbitions
    {
        internal const string ContactId = "mybelovedumcdesert:dealer_name";
        internal const string ContactDescription = "mybelovedumcdesert:description";
        internal const string DialogTypeKey = "mybelovedumcdesert_calldialogtype";
        internal const string VehicleTypeName = "ba:vehicletype_umcdesert";
        internal const string ShowcaseItemName = "ba:itemname_umcdesertshowcase";
        internal const string GeneralUSTrucksContactId = "General US Trucks";
        internal const int RestoredMaxSpeed = 80;
        internal const float RestoredEnginePower = 150f;
        internal const float RestoredBrakeForce = 6000f;

        private static GameObject? registrarObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            EnsureRegistrar(context);
            context.Logger.Info($"My Beloved UMC Desert: adding '{VehicleTypeName}' to '{GeneralUSTrucksContactId}'.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (registrarObject != null)
            {
                UnityEngine.Object.Destroy(registrarObject);
                registrarObject = null;
            }

            ContractItemsForSaleService.RemoveContact(ContactId);
            GeneralUSTrucksStockService.RestorePreviousPhoneStock();
            return Task.CompletedTask;
        }

        private static void EnsureRegistrar(ModContext context)
        {
            if (registrarObject == null)
            {
                registrarObject = new GameObject("MyBelovedUMCDesert.ContactRegistrar");
                UnityEngine.Object.DontDestroyOnLoad(registrarObject);
            }

            var registrar = registrarObject.GetComponent<MyBelovedUMCDesertContactRegistrar>();
            if (registrar == null)
                registrar = registrarObject.AddComponent<MyBelovedUMCDesertContactRegistrar>();

            registrar.Initialize(context);
        }

        internal static void RegisterDealerPhoneStock(ModContext? context)
        {
            UMCDesertStatsService.ApplyRestoredStats(context);
            GeneralUSTrucksStockService.ApplyPhoneStock();
            GeneralUSTrucksShowroomService.Apply(context);
            RemoveLegacyContact(context);
        }

        private static void RemoveLegacyContact(ModContext? context)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.Contacts == null)
                return;

            var removedCount = 0;
            for (var i = saveGame.Contacts.Count - 1; i >= 0; i--)
            {
                var contact = saveGame.Contacts[i];
                if (contact == null ||
                    !string.Equals(contact.id, ContactId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                saveGame.Contacts.RemoveAt(i);
                removedCount++;
            }

            if (removedCount == 0)
                return;

            ContractItemsForSaleService.RemoveContact(ContactId);
            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            RefreshContactsUi();
            context?.Logger.Info($"My Beloved UMC Desert: removed legacy standalone contact count={removedCount}.");
        }

        private static void RefreshContactsUi()
        {
            try
            {
                InstanceBehavior<UIs>.Instance?.fullMenu?.contactsApp?.RefreshHeader();
                ContactsApp.onContactAdded?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"My Beloved UMC Desert: contacts UI refresh failed: {exception.Message}");
            }
        }
    }

    internal static class GeneralUSTrucksStockService
    {
        private static List<string>? previousPhoneStock;
        private static bool previousPhoneStockCaptured;
        private static string? lastAppliedStockKey;

        internal static void ApplyPhoneStock()
        {
            CapturePreviousPhoneStock();

            var stock = CreateMergedPhoneStock();
            var stockKey = string.Join("|", stock);
            ContractItemsForSaleService.SetVehiclesForContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId, stock);
            lastAppliedStockKey = stockKey;
        }

        internal static void RestorePreviousPhoneStock()
        {
            if (!previousPhoneStockCaptured)
                return;

            if (previousPhoneStock == null)
                ContractItemsForSaleService.RemoveContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId);
            else
                ContractItemsForSaleService.SetVehiclesForContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId, previousPhoneStock);
        }

        private static void CapturePreviousPhoneStock()
        {
            if (previousPhoneStockCaptured)
                return;

            previousPhoneStockCaptured = true;
            if (ContractItemsForSaleService.TryGetVehiclesForContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId, out List<string> existingStock))
                previousPhoneStock = existingStock;
            else
                previousPhoneStock = null;
        }

        private static List<string> CreateMergedPhoneStock()
        {
            var stock = new List<string>();
            if (previousPhoneStock != null)
                AddUnique(stock, previousPhoneStock);

            AddUnique(stock, MyBelovedUMCDesertMod.VehicleTypeName);
            return stock;
        }

        private static void AddUnique(List<string> target, IEnumerable<string> vehicleNames)
        {
            foreach (var vehicleName in vehicleNames)
                AddUnique(target, vehicleName);
        }

        private static void AddUnique(List<string> target, string vehicleName)
        {
            if (string.IsNullOrWhiteSpace(vehicleName))
                return;

            foreach (var existing in target)
            {
                if (string.Equals(existing, vehicleName, StringComparison.Ordinal))
                    return;
            }

            target.Add(vehicleName);
        }
    }

    internal static class GeneralUSTrucksShowroomService
    {
        private const BindingFlags ReflectionFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string TruckDealerLayoutName = "IndustryCityCarDealershipTrucks";
        private const string ReplacedShowcaseItemName = "ba:itemname_mersaididashshowcase";

        private static bool hasLoggedRegistrationPatch;
        private static bool hasLoggedLivePatch;
        private static Type? itemControllerType;

        internal static void Apply(ModContext? context)
        {
            var patchedRegistrationCount = PatchRegistrations(context);
            var patchedLiveCount = PatchLiveItemControllers(context);

            if (patchedRegistrationCount > 0 && !hasLoggedRegistrationPatch)
            {
                hasLoggedRegistrationPatch = true;
                context?.Logger.Info(
                    $"My Beloved UMC Desert: replaced {patchedRegistrationCount} dealership layout showcase item(s) with '{MyBelovedUMCDesertMod.ShowcaseItemName}'.");
            }

            if (patchedLiveCount > 0 && !hasLoggedLivePatch)
            {
                hasLoggedLivePatch = true;
                context?.Logger.Info(
                    $"My Beloved UMC Desert: patched {patchedLiveCount} live showroom controller(s) to '{MyBelovedUMCDesertMod.VehicleTypeName}'.");
            }
        }

        private static int PatchRegistrations(ModContext? context)
        {
            var patchedCount = 0;
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

            try
            {
                patchedCount += PatchRegistrationGraph(SaveGameManager.Current, visited, 0, 0);
            }
            catch (Exception exception)
            {
                context?.Logger.Warn($"My Beloved UMC Desert: save registration showroom patch failed: {exception.Message}");
            }

            try
            {
                patchedCount += PatchRegistrationGraph(BuildingManager.Instance, visited, 0, 0);
            }
            catch (Exception exception)
            {
                context?.Logger.Warn($"My Beloved UMC Desert: building registration showroom patch failed: {exception.Message}");
            }

            return patchedCount;
        }

        private static int PatchRegistrationGraph(object? value, HashSet<object> visited, int depth, int scanned)
        {
            if (value == null || depth > 7 || scanned > 2500)
                return 0;

            var valueType = value.GetType();
            if (IsTerminalType(valueType) || !visited.Add(value))
                return 0;

            var patchedCount = PatchRegistrationIfTruckDealer(value);

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    patchedCount += PatchRegistrationGraph(entry.Key, visited, depth + 1, scanned + 1);
                    patchedCount += PatchRegistrationGraph(entry.Value, visited, depth + 1, scanned + 1);
                }

                return patchedCount;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var entry in enumerable)
                    patchedCount += PatchRegistrationGraph(entry, visited, depth + 1, scanned + 1);

                return patchedCount;
            }

            foreach (var field in GetAllFields(valueType))
            {
                if (field.FieldType.IsPointer || field.FieldType.IsPrimitive)
                    continue;

                object? fieldValue;
                try
                {
                    fieldValue = field.GetValue(value);
                }
                catch
                {
                    continue;
                }

                patchedCount += PatchRegistrationGraph(fieldValue, visited, depth + 1, scanned + 1);
            }

            return patchedCount;
        }

        private static int PatchRegistrationIfTruckDealer(object candidate)
        {
            var layout = GetMemberValue(candidate, "Layout") as string ??
                         GetMemberValue(candidate, "layout") as string;
            if (!string.Equals(layout, TruckDealerLayoutName, StringComparison.Ordinal))
                return 0;

            return PatchItemInstanceDictionary(GetMemberValue(candidate, "itemInstances"));
        }

        private static int PatchItemInstanceDictionary(object? itemInstances)
        {
            if (itemInstances is not IDictionary dictionary)
                return 0;

            var patchedCount = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (TryPatchItemInstance(entry.Value))
                    patchedCount++;
            }

            return patchedCount;
        }

        private static int PatchLiveItemControllers(ModContext? context)
        {
            var type = itemControllerType ??= FindType("ItemController");
            if (type == null)
                return 0;

            UnityEngine.Object[] itemControllers;
            try
            {
                itemControllers = UnityEngine.Object.FindObjectsOfType(type, true);
            }
            catch (Exception exception)
            {
                context?.Logger.Warn($"My Beloved UMC Desert: could not scan live item controllers: {exception.Message}");
                return 0;
            }

            var patchedCount = 0;
            foreach (var controller in itemControllers)
            {
                if (controller == null)
                    continue;

                var itemInstance = GetMemberValue(controller, "itemInstance") ??
                                   GetMemberValue(controller, "_itemInstance") ??
                                   GetMemberValue(controller, "ItemInstance");
                if (!TryPatchItemInstance(itemInstance))
                    continue;

                PatchItemController(controller);
                if (controller is Component component)
                    PatchAttachedShowcaseControllers(component.gameObject);

                patchedCount++;
            }

            return patchedCount;
        }

        private static bool TryPatchItemInstance(object? itemInstance)
        {
            if (itemInstance == null)
                return false;

            var itemName = GetMemberValue(itemInstance, "itemName") as string;
            if (!string.Equals(itemName, ReplacedShowcaseItemName, StringComparison.Ordinal))
                return false;

            var desertItem = ResolveItemByName(MyBelovedUMCDesertMod.ShowcaseItemName);
            SetMemberValue(itemInstance, "itemName", MyBelovedUMCDesertMod.ShowcaseItemName);
            SetMemberValue(itemInstance, "_itemCached", desertItem);
            SetPlayerItemPurchaserItemName(GetMemberValue(itemInstance, "playerItemPurchaserSettings"));
            return true;
        }

        private static void PatchItemController(object controller)
        {
            var desertItem = ResolveItemByName(MyBelovedUMCDesertMod.ShowcaseItemName);
            foreach (var memberName in new[] { "item", "_item", "itemCached", "_itemCached", "Item", "ItemCached" })
                SetMemberValue(controller, memberName, desertItem);

            SetPlayerItemPurchaserItemName(GetMemberValue(controller, "playerItemPurchaserSettings"));
        }

        private static void PatchAttachedShowcaseControllers(GameObject gameObject)
        {
            if (gameObject == null)
                return;

            var desertVehicleType = VehicleTypeHelper.GetVehicleType(MyBelovedUMCDesertMod.VehicleTypeName);
            foreach (var behaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("ShowcaseVehicle", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("VehicleShowcase", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                foreach (var memberName in new[] { "_vehicleType", "vehicleType", "VehicleType" })
                    SetMemberValue(behaviour, memberName, desertVehicleType);

                foreach (var memberName in new[] { "vehicleTypeName", "_vehicleTypeName", "VehicleTypeName" })
                    SetMemberValue(behaviour, memberName, MyBelovedUMCDesertMod.VehicleTypeName);
            }
        }

        private static void SetPlayerItemPurchaserItemName(object? settings)
        {
            if (settings == null)
                return;

            SetMemberValue(settings, "itemName", MyBelovedUMCDesertMod.ShowcaseItemName);
        }

        private static object? ResolveItemByName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return null;

            try
            {
                return ItemsGetter.GetByName(itemName);
            }
            catch
            {
                return null;
            }
        }

        private static Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = null;
                try
                {
                    type = assembly.GetType(typeName, false) ??
                           assembly.GetType("Controllers." + typeName, false);
                }
                catch
                {
                }

                if (type != null)
                    return type;
            }

            return null;
        }

        private static IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var field in current.GetFields(ReflectionFlags))
                    yield return field;
            }
        }

        private static object? GetMemberValue(object? instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(memberName, ReflectionFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return property.GetValue(instance);
                    }
                    catch
                    {
                    }
                }

                var field = type.GetField(memberName, ReflectionFlags);
                if (field == null)
                    continue;

                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static bool SetMemberValue(object? instance, string memberName, object? value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(memberName, ReflectionFlags);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        property.SetValue(instance, value);
                        return true;
                    }
                    catch
                    {
                    }
                }

                var field = type.GetField(memberName, ReflectionFlags);
                if (field == null || field.IsInitOnly)
                    continue;

                try
                {
                    field.SetValue(instance, value);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsTerminalType(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   typeof(UnityEngine.Object).IsAssignableFrom(type) && type != typeof(BuildingManager);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object? x, object? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }

    internal static class UMCDesertStatsService
    {
        private static bool hasLoggedStats;

        internal static void ApplyRestoredStats(ModContext? context)
        {
            var vehicleType = VehicleTypeHelper.GetVehicleType(MyBelovedUMCDesertMod.VehicleTypeName);
            if (vehicleType == null)
                return;

            var changed = false;
            if (vehicleType.maxSpeed < MyBelovedUMCDesertMod.RestoredMaxSpeed)
            {
                vehicleType.maxSpeed = MyBelovedUMCDesertMod.RestoredMaxSpeed;
                changed = true;
            }

            if (vehicleType.enginePower < MyBelovedUMCDesertMod.RestoredEnginePower)
            {
                vehicleType.enginePower = MyBelovedUMCDesertMod.RestoredEnginePower;
                changed = true;
            }

            if (vehicleType.brakeForce < MyBelovedUMCDesertMod.RestoredBrakeForce)
            {
                vehicleType.brakeForce = MyBelovedUMCDesertMod.RestoredBrakeForce;
                changed = true;
            }

            if ((changed || !hasLoggedStats) && context != null)
            {
                hasLoggedStats = true;
                context.Logger.Info(
                    "My Beloved UMC Desert: restored vehicle stats " +
                    $"maxSpeed={vehicleType.maxSpeed}, " +
                    $"enginePower={vehicleType.enginePower}, " +
                    $"brakeForce={vehicleType.brakeForce}.");
            }
        }
    }

    internal sealed class MyBelovedUMCDesertContactRegistrar : MonoBehaviour
    {
        private const float RetryIntervalSeconds = 2f;

        private ModContext? context;
        private float nextAttemptAt;

        public void Initialize(ModContext context)
        {
            this.context = context;
            nextAttemptAt = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAttemptAt)
                return;

            nextAttemptAt = Time.unscaledTime + RetryIntervalSeconds;
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);
        }
    }
}
