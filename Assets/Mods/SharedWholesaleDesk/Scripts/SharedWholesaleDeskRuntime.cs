#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BigAmbitions.Items;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using UnityEngine;

namespace SharedWholesaleDesk
{
    internal static class SharedWholesaleDeskRuntime
    {
        internal const string ModdedDialogTypeKey = "sharedwholesale_moddedproducts_dialog";

        private const string WholesaleStoreSettingsTypeName = "Buildings.WholesaleStoreSettings";

        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, PatchedServiceDeskRecord> PatchedDesksByKey = new Dictionary<string, PatchedServiceDeskRecord>();
        private static readonly Dictionary<string, List<PatchedServiceDeskRecord>> PatchedDesksByAddressKey = new Dictionary<string, List<PatchedServiceDeskRecord>>();

        internal static CallDialogType ModdedDialogType { get; private set; }
        internal static int PatchedDeskCount => PatchedDesksByKey.Count;

        internal static void Initialize()
        {
            PatchedDesksByKey.Clear();
            PatchedDesksByAddressKey.Clear();
        }

        internal static void Reset()
        {
            PatchedDesksByKey.Clear();
            PatchedDesksByAddressKey.Clear();
            ModdedDialogType = default;
        }

        internal static void SetModdedDialogType(CallDialogType dialogType)
        {
            ModdedDialogType = dialogType;
        }

        internal static PatchScanResult TryPatchServiceDesks()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null)
            {
                SharedWholesaleDeskLog.Info("Wholesale desk patch scan skipped because save game or building registrations are unavailable.");
                return PatchScanResult.NotReady();
            }

            var foundTargetCount = 0;
            var patchedCount = 0;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null)
                    continue;

                var address = TryGetRegistrationAddress(registration);
                if (address == null)
                    continue;

                var building = TryGetBuilding(address);
                if (building == null)
                    continue;

                var specialService = GetMemberValue(building, "SpecialService") ?? GetMemberValue(building, "specialService");
                if (specialService == null)
                    continue;

                var settings = GetMemberValue(specialService, "settings");
                if (settings == null || !IsWholesaleSettings(settings))
                    continue;

                foundTargetCount++;

                var serviceInstanceKey = BuildServiceInstanceKey(address, specialService);
                if (PatchedDesksByKey.ContainsKey(serviceInstanceKey))
                    continue;

                var currentDialogValue = GetMemberValue(specialService, "dialogType");
                if (currentDialogValue == null)
                {
                    SharedWholesaleDeskLog.Warn($"Skipping wholesale desk at {GetAddressKey(address)} because dialogType was unavailable.");
                    continue;
                }

                var originalDialogType = (CallDialogType)Convert.ToInt32(currentDialogValue, CultureInfo.InvariantCulture);
                if (originalDialogType.Equals(ModdedDialogType))
                {
                    SharedWholesaleDeskLog.Info(
                        $"Skipping wholesale desk at {GetAddressKey(address)} because it already uses the modded dialog type {(int)ModdedDialogType}.");
                    continue;
                }

                if (!SetMemberValue(specialService, "dialogType", ModdedDialogType))
                {
                    SharedWholesaleDeskLog.Warn(
                        $"Failed to patch wholesale desk at {GetAddressKey(address)}. Could not assign dialogType {(int)ModdedDialogType}.");
                    continue;
                }

                var record = new PatchedServiceDeskRecord(
                    serviceInstanceKey,
                    GetAddressKey(address),
                    address,
                    specialService,
                    ServiceDeskKind.Wholesale,
                    originalDialogType,
                    TryGetUnityInstanceId(specialService));

                PatchedDesksByKey[serviceInstanceKey] = record;
                if (!PatchedDesksByAddressKey.TryGetValue(record.AddressKey, out var records))
                {
                    records = new List<PatchedServiceDeskRecord>();
                    PatchedDesksByAddressKey[record.AddressKey] = records;
                }

                records.Add(record);
                patchedCount++;

                SharedWholesaleDeskLog.Info(
                    $"Patched wholesale desk at {record.AddressKey}. ServiceKey={record.ServiceInstanceKey}, ServiceInstanceId={record.ServiceInstanceId}, OriginalDialogType={(int)record.OriginalDialogType}, ModdedDialogType={(int)ModdedDialogType}.");
            }

            if (foundTargetCount == 0)
                SharedWholesaleDeskLog.Info("Wholesale desk patch scan found no wholesale desks yet.");

            return PatchScanResult.CreateReady(foundTargetCount, patchedCount);
        }

        internal static void RestorePatchedServiceDesks()
        {
            foreach (var record in PatchedDesksByKey.Values.ToArray())
            {
                var currentDialogValue = GetMemberValue(record.SpecialService, "dialogType");
                if (currentDialogValue == null)
                {
                    SharedWholesaleDeskLog.Warn($"Restore skipped for wholesale desk at {record.AddressKey} because dialogType was unavailable.");
                    continue;
                }

                var currentDialogType = (CallDialogType)Convert.ToInt32(currentDialogValue, CultureInfo.InvariantCulture);
                if (!currentDialogType.Equals(ModdedDialogType))
                {
                    SharedWholesaleDeskLog.Info(
                        $"Restore skipped for wholesale desk at {record.AddressKey} because current dialog {(int)currentDialogType} no longer belongs to this mod.");
                    continue;
                }

                if (!SetMemberValue(record.SpecialService, "dialogType", record.OriginalDialogType))
                {
                    SharedWholesaleDeskLog.Warn(
                        $"Restore failed for wholesale desk at {record.AddressKey}. Original dialog {(int)record.OriginalDialogType} could not be reassigned.");
                    continue;
                }

                SharedWholesaleDeskLog.Info($"Restored wholesale desk at {record.AddressKey} to original dialog {(int)record.OriginalDialogType}.");
            }
        }

        internal static PatchedServiceDeskRecord? TryGetCurrentDeskRecord()
        {
            var address = DialogController.current?.contact?.Address;
            if (address == null)
                return null;

            return PatchedDesksByAddressKey.TryGetValue(GetAddressKey(address), out var records) && records.Count > 0
                ? records[0]
                : null;
        }

        internal static bool TryOpenOriginalVanillaDialog(PatchedServiceDeskRecord record)
        {
            try
            {
                SharedWholesaleDeskLog.Info(
                    $"Attempting vanilla delegation for wholesale desk at {record.AddressKey} using original dialog {(int)record.OriginalDialogType}.");

                var dialog = CallDialogFactory.GetDialog(record.OriginalDialogType);
                if (dialog == null)
                {
                    SharedWholesaleDeskLog.Warn(
                        $"Vanilla delegation returned null for wholesale desk at {record.AddressKey}. Falling back to confirmed wholesale dialog constructor.");
                    return TryOpenConfirmedVanillaFallback(record);
                }

                SharedWholesaleDeskLog.Info(
                    $"Vanilla delegation succeeded for wholesale desk at {record.AddressKey} using original dialog {(int)record.OriginalDialogType}.");
                return true;
            }
            catch (Exception exception)
            {
                SharedWholesaleDeskLog.Warn(
                    $"Vanilla delegation threw for wholesale desk at {record.AddressKey}. Falling back to confirmed wholesale dialog constructor. {exception}");
                return TryOpenConfirmedVanillaFallback(record);
            }
        }

        internal static ProductBrowserResult BuildProductBrowserPage(int productIndex)
        {
            var evaluations = DiscoverEligibleModdedProducts().ToList();
            if (evaluations.Count == 0)
            {
                return new ProductBrowserResult(
                    null,
                    0,
                    0,
                    "No eligible modded products were detected.",
                    false);
            }

            var clampedIndex = Mathf.Clamp(productIndex, 0, evaluations.Count - 1);
            var selected = evaluations[clampedIndex];

            var builder = new StringBuilder();
            builder.Append($"Product {clampedIndex + 1}/{evaluations.Count}");
            builder.Append("<br><br>");
            builder.Append(BuildCatalogItemTitle(selected.Item));
            builder.Append("<br>");
            builder.Append(
                $"id={selected.Item.itemName}<br>wholesalePrice={selected.Item.wholesalePrice:0.##}, defaultMarketPrice={selected.Item.DefaultMarketPrice:0.##}, boxSize={selected.Item.boxSize}, productSalesRatio={selected.Item.productSalesRatio:0.###}, maxOrderAmountPerImporter={selected.Item.maxOrderAmountPerImporter}, canPlayerDoOrder={selected.Item.canPlayerDoOrder}, isADemandedProduct={selected.Item.isADemandedProduct}<br><br>Select this product to create a wholesale delivery contract.");

            return new ProductBrowserResult(
                selected,
                clampedIndex,
                evaluations.Count,
                builder.ToString(),
                evaluations.Count > 1);
        }

        internal static BusinessBrowserResult BuildBusinessBrowserPage(
            PatchedServiceDeskRecord record,
            ProductEligibilityResult selectedProduct,
            int businessIndex)
        {
            var candidates = GetEligibleBusinessTargets(record, selectedProduct.Item).ToList();
            if (candidates.Count == 0)
            {
                return new BusinessBrowserResult(
                    null,
                    0,
                    0,
                    "No eligible target businesses were found. A business must be rented by the player, have a business name, contain business storage furniture, and not already have a contract with this wholesale desk.",
                    false);
            }

            var clampedIndex = Mathf.Clamp(businessIndex, 0, candidates.Count - 1);
            var selected = candidates[clampedIndex];
            var builder = new StringBuilder();
            builder.Append($"Business {clampedIndex + 1}/{candidates.Count}");
            builder.Append("<br><br>");
            builder.Append(selected.BusinessName);
            builder.Append("<br>");
            builder.Append(FormatAddress(selected.Address));
            builder.Append("<br><br>");
            builder.Append($"Selected product: {BuildCatalogItemTitle(selectedProduct.Item)}");

            return new BusinessBrowserResult(
                selected,
                clampedIndex,
                candidates.Count,
                builder.ToString(),
                candidates.Count > 1);
        }

        internal static QuantityBrowserResult BuildQuantityBrowserPage(
            ProductEligibilityResult selectedProduct,
            int quantityIndex)
        {
            var options = BuildQuantityOptions(selectedProduct.Item).ToList();
            if (options.Count == 0)
            {
                return new QuantityBrowserResult(
                    null,
                    0,
                    0,
                    "No valid quantity options were available for this product.",
                    false);
            }

            var clampedIndex = Mathf.Clamp(quantityIndex, 0, options.Count - 1);
            var selected = options[clampedIndex];
            var builder = new StringBuilder();
            builder.Append($"Quantity {clampedIndex + 1}/{options.Count}");
            builder.Append("<br><br>");
            builder.Append(BuildCatalogItemTitle(selectedProduct.Item));
            builder.Append("<br>");
            builder.Append($"Boxes: {selected.Boxes}");
            builder.Append("<br>");
            builder.Append($"Units: {selected.Amount}");
            builder.Append("<br>");
            builder.Append($"Estimated product cost: {(selectedProduct.Item.wholesalePrice * selected.Amount):0.##}");
            builder.Append("<br>");
            builder.Append($"Delivery fee added separately by the wholesale contract.");

            return new QuantityBrowserResult(
                selected,
                clampedIndex,
                options.Count,
                builder.ToString(),
                options.Count > 1);
        }

        internal static OrderCreationResult CreateModdedWholesaleContract(
            PatchedServiceDeskRecord record,
            ProductEligibilityResult selectedProduct,
            BusinessTargetRecord selectedBusiness,
            QuantityOption selectedQuantity)
        {
            try
            {
                SharedWholesaleDeskLog.Info(
                    $"Attempting modded wholesale contract creation. Desk={record.AddressKey}, Business={FormatAddress(selectedBusiness.Address)}, Product={selectedProduct.Item.itemName}, Boxes={selectedQuantity.Boxes}, Amount={selectedQuantity.Amount}.");

                var saveGame = SaveGameManager.Current;
                if (saveGame?.DeliveryContracts == null)
                    return OrderCreationResult.Failure("DeliveryContracts storage is unavailable.");

                if (HasExistingContractForPair(saveGame.DeliveryContracts, record.Address, selectedBusiness.Address))
                    return OrderCreationResult.Failure("This business already has a delivery contract with the selected wholesale desk.");

                if (!BusinessHasStorage(selectedBusiness.Registration))
                    return OrderCreationResult.Failure("The selected business does not contain business storage furniture.");

                #pragma warning disable 0612
                var contract = new DeliveryContract
                {
                    enabled = true,
                    isUrgentOrder = false,
                    nextDeliveryDay = DeliveryHelper.GetNextDeliveryDay(),
                    repeatingOrder = true,
                    wholesaleAddress = record.Address,
                    businessAddress = selectedBusiness.Address,
                    deliveryFee = GetWholesaleDeliveryFee(record.SpecialService),
                    items = new List<DeliveryContractItem>
                    {
                        new DeliveryContractItem
                        {
                            itemName = selectedProduct.Item.itemName,
                            boxes = selectedQuantity.Boxes,
                            amount = selectedQuantity.Amount,
                            amountOrderedLastWeek = 0,
                            amountOrderedThisWeek = 0
                        }
                    }
                };
                #pragma warning restore 0612

                saveGame.DeliveryContracts.Add(contract);
                SharedWholesaleDeskLog.Info(
                    $"Inserted DeliveryContract. Business={FormatAddress(selectedBusiness.Address)}, Product={selectedProduct.Item.itemName}, Boxes={selectedQuantity.Boxes}, Amount={selectedQuantity.Amount}, DeliveryFee={contract.deliveryFee:0.##}, NextDeliveryDay={contract.nextDeliveryDay}, Enabled={contract.enabled}, RepeatingOrder={contract.repeatingOrder}.");

                TryInvokeNewDeliveryContractEvent();

                return OrderCreationResult.Success(
                    $"Created wholesale contract for {BuildCatalogItemTitle(selectedProduct.Item)} to {selectedBusiness.BusinessName}. Boxes={selectedQuantity.Boxes}, Units={selectedQuantity.Amount}.");
            }
            catch (Exception exception)
            {
                SharedWholesaleDeskLog.Warn($"Modded wholesale contract creation failed. {exception}");
                return OrderCreationResult.Failure("An exception occurred while creating the wholesale contract. Check the SharedWholesaleDesk log file.");
            }
        }

        private static string BuildCatalogItemTitle(Item item)
        {
            var localizedName = item.itemName.GetLocalization();
            return string.Equals(localizedName, item.itemName, StringComparison.Ordinal)
                ? item.itemName
                : $"{localizedName} ({item.itemName})";
        }

        private static IEnumerable<BusinessTargetRecord> GetEligibleBusinessTargets(
            PatchedServiceDeskRecord record,
            Item selectedItem)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null)
                yield break;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null)
                    continue;

                if (!registration.RentedByPlayer)
                {
                    SharedWholesaleDeskLog.Info($"Excluded business candidate at {FormatAddress(registration.Address)}: not rented by player.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(registration.BusinessName))
                {
                    SharedWholesaleDeskLog.Info($"Excluded business candidate at {FormatAddress(registration.Address)}: missing business name.");
                    continue;
                }

                if (HasExistingContractForPair(saveGame.DeliveryContracts, record.Address, registration.Address))
                {
                    SharedWholesaleDeskLog.Info(
                        $"Excluded business candidate at {FormatAddress(registration.Address)}: duplicate contract already exists for wholesale desk {record.AddressKey}.");
                    continue;
                }

                if (!BusinessHasStorage(registration))
                {
                    SharedWholesaleDeskLog.Info($"Excluded business candidate at {FormatAddress(registration.Address)}: no business storage detected.");
                    continue;
                }

                SharedWholesaleDeskLog.Info(
                    $"Included business candidate at {FormatAddress(registration.Address)} for product {selectedItem.itemName}.");
                yield return new BusinessTargetRecord(registration, registration.Address, registration.BusinessName);
            }
        }

        private static bool HasExistingContractForPair(IEnumerable<DeliveryContract>? contracts, Address wholesaleAddress, Address businessAddress)
        {
            if (contracts == null)
                return false;

            foreach (var contract in contracts)
            {
                if (contract == null)
                    continue;

                if (contract.wholesaleAddress != null
                    && contract.businessAddress != null
                    && contract.wholesaleAddress.Equals(wholesaleAddress)
                    && contract.businessAddress.Equals(businessAddress))
                    return true;
            }

            return false;
        }

        private static bool BusinessHasStorage(BuildingRegistration registration)
        {
            if (registration.itemInstances == null || registration.itemInstances.Count == 0)
                return false;

            foreach (var instance in registration.itemInstances.Values)
            {
                if (instance?.ItemCached == null)
                    continue;

                if (ItemHasBusinessStorageTag(instance.ItemCached))
                    return true;
            }

            return false;
        }

        private static bool ItemHasBusinessStorageTag(Item item)
        {
            var tagsValue = GetMemberValue(item, "tags");
            if (!(tagsValue is System.Collections.IEnumerable tags))
                return false;

            foreach (var tag in tags)
            {
                if (tag == null)
                    continue;

                var value = tag.ToString();
                if (string.Equals(value, "isbusinessstorage", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "SharedItemTag.isbusinessstorage", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static IEnumerable<QuantityOption> BuildQuantityOptions(Item item)
        {
            var maxBoxes = item.boxSize > 0
                ? item.maxOrderAmountPerImporter / item.boxSize
                : 0;
            if (maxBoxes <= 0)
                yield break;

            var preferred = new[] { 1, 2, 5, 10, 20, 50, maxBoxes };
            var yielded = new HashSet<int>();

            foreach (var boxes in preferred)
            {
                var clamped = Mathf.Clamp(boxes, 1, maxBoxes);
                if (!yielded.Add(clamped))
                    continue;

                yield return new QuantityOption(clamped, clamped * item.boxSize);
            }
        }

        private static float GetWholesaleDeliveryFee(object specialService)
        {
            var settings = GetMemberValue(specialService, "settings");
            if (settings == null)
                return 0f;

            var feeValue = GetMemberValue(settings, "deliveryFee");
            return feeValue == null
                ? 0f
                : Convert.ToSingle(feeValue, CultureInfo.InvariantCulture);
        }

        private static void TryInvokeNewDeliveryContractEvent()
        {
            try
            {
                var gameEventType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("GameEvent", false))
                    .FirstOrDefault(type => type != null);
                var invokeMethod = gameEventType?.GetMethod(
                    "Invoke",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                invokeMethod?.Invoke(null, new object[] { "ba:gameevent_newdeliverycontract" });
                SharedWholesaleDeskLog.Info("Invoked GameEvent 'ba:gameevent_newdeliverycontract'.");
            }
            catch (Exception exception)
            {
                SharedWholesaleDeskLog.Warn($"Failed to invoke GameEvent 'ba:gameevent_newdeliverycontract'. {exception}");
            }
        }

        private static string FormatAddress(Address address)
        {
            return $"{address.streetName} {address.streetNumber}";
        }

        private static bool TryOpenConfirmedVanillaFallback(PatchedServiceDeskRecord record)
        {
            try
            {
                SharedWholesaleDeskLog.Info(
                    $"Using confirmed fallback constructor for wholesale desk at {record.AddressKey}: {typeof(WholesaleStoreManagerDialog).FullName}.");
                _ = new WholesaleStoreManagerDialog();
                return true;
            }
            catch (Exception exception)
            {
                SharedWholesaleDeskLog.Warn(
                    $"Confirmed fallback constructor failed for wholesale desk at {record.AddressKey}. {exception}");
                return false;
            }
        }

        private static IEnumerable<ProductEligibilityResult> DiscoverEligibleModdedProducts()
        {
            if (ItemsGetter.AllItems == null)
            {
                SharedWholesaleDeskLog.Warn("Product discovery skipped because ItemsGetter.AllItems is unavailable.");
                return Array.Empty<ProductEligibilityResult>();
            }

            var included = new List<ProductEligibilityResult>();
            foreach (var item in ItemsGetter.AllItems)
            {
                if (item == null)
                    continue;

                var itemName = item.itemName;
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    SharedWholesaleDeskLog.Info("Excluded non-ba item candidate with missing item ID: itemName was null or whitespace.");
                    continue;
                }

                if (itemName.StartsWith("ba:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var evaluation = EvaluateItemEligibility(item);
                SharedWholesaleDeskLog.Info(
                    $"{(evaluation.IsEligible ? "Included" : "Excluded")} non-ba item '{itemName}': {evaluation.Reason}");
                if (evaluation.IsEligible)
                    included.Add(evaluation);
            }

            return included;
        }

        private static ProductEligibilityResult EvaluateItemEligibility(Item item)
        {
            if (string.IsNullOrWhiteSpace(item.itemName))
                return ProductEligibilityResult.Excluded(item, "item ID missing");

            if (item.itemName.StartsWith("ba:", StringComparison.OrdinalIgnoreCase))
                return ProductEligibilityResult.Excluded(item, "vanilla item ID");

            if (item.wholesalePrice <= 0f)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "wholesalePrice <= 0"));

            if (item.DefaultMarketPrice <= 0f)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "defaultMarketPrice <= 0"));

            if (item.boxSize <= 0)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "boxSize <= 0 (runtime field used for amountPerBox check)"));

            if (item.productSalesRatio <= 0f)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "productSalesRatio <= 0"));

            if (item.maxOrderAmountPerImporter <= 0)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "maxOrderAmountPerImporter <= 0"));

            if (item.isFurniture)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "isFurniture = true"));

            if (item.isProducer)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "isProducer = true"));

            if (item.assignable)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "assignable = true"));

            if (item.canPlayerDoOrder)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "canPlayerDoOrder = true"));

            if (!item.isADemandedProduct)
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "isADemandedProduct = false"));

            if (!string.IsNullOrWhiteSpace(item.vehicleType))
                return ProductEligibilityResult.Excluded(item, BuildEligibilityReason(item, "vehicleType is set"));

            return ProductEligibilityResult.Included(item, BuildEligibilityReason(item, "eligible"));
        }

        private static string BuildEligibilityReason(Item item, string result)
        {
            return $"{result}; values: wholesalePrice={item.wholesalePrice:0.##}, defaultMarketPrice={item.DefaultMarketPrice:0.##}, boxSize={item.boxSize}, productSalesRatio={item.productSalesRatio:0.###}, maxOrderAmountPerImporter={item.maxOrderAmountPerImporter}, canPlayerDoOrder={item.canPlayerDoOrder}, isADemandedProduct={item.isADemandedProduct}, isFurniture={item.isFurniture}, isProducer={item.isProducer}, assignable={item.assignable}";
        }

        private static bool IsWholesaleSettings(object settings)
        {
            return string.Equals(settings.GetType().FullName, WholesaleStoreSettingsTypeName, StringComparison.Ordinal);
        }

        private static string BuildServiceInstanceKey(Address address, object specialService)
        {
            var addressKey = GetAddressKey(address);
            var instanceId = TryGetUnityInstanceId(specialService);
            return instanceId.HasValue
                ? $"{addressKey}#{instanceId.Value}"
                : $"{addressKey}#{specialService.GetHashCode()}";
        }

        private static int? TryGetUnityInstanceId(object instance)
        {
            try
            {
                var method = instance.GetType().GetMethod("GetInstanceID", ReflectionFlags, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(int))
                    return null;

                return (int)method.Invoke(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static Address? TryGetRegistrationAddress(BuildingRegistration registration)
        {
            return GetMemberValue(registration, "Address") as Address
                   ?? GetMemberValue(registration, "address") as Address;
        }

        private static Building? TryGetBuilding(Address address)
        {
            try
            {
                return BuildingHelper.GetBuilding(address);
            }
            catch (Exception exception)
            {
                SharedWholesaleDeskLog.Warn($"Failed to resolve building at {GetAddressKey(address)}. {exception}");
                return null;
            }
        }

        private static string GetAddressKey(Address address) => $"{address.streetName}:{address.streetNumber}";

        private static object? GetMemberValue(object instance, string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
                return null;

            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(memberName, ReflectionFlags);
                if (property != null)
                    return property.GetValue(instance);

                var field = type.GetField(memberName, ReflectionFlags);
                if (field != null)
                    return field.GetValue(instance);
            }

            return null;
        }

        private static bool SetMemberValue(object instance, string memberName, object value)
        {
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(memberName, ReflectionFlags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, ConvertMemberValue(value, property.PropertyType));
                    return true;
                }

                var field = type.GetField(memberName, ReflectionFlags);
                if (field == null)
                    continue;

                field.SetValue(instance, ConvertMemberValue(value, field.FieldType));
                return true;
            }

            return false;
        }

        private static object ConvertMemberValue(object value, Type targetType)
        {
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, Convert.ToInt32(value, CultureInfo.InvariantCulture));

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        internal readonly struct PatchScanResult
        {
            internal PatchScanResult(bool ready, int foundTargetCount, int patchedCount)
            {
                Ready = ready;
                FoundTargetCount = foundTargetCount;
                PatchedCount = patchedCount;
            }

            internal bool Ready { get; }
            internal int FoundTargetCount { get; }
            internal int PatchedCount { get; }

            internal static PatchScanResult NotReady() => new PatchScanResult(false, 0, 0);
            internal static PatchScanResult CreateReady(int foundTargetCount, int patchedCount) => new PatchScanResult(true, foundTargetCount, patchedCount);
        }

        internal sealed class PatchedServiceDeskRecord
        {
            internal PatchedServiceDeskRecord(
                string serviceInstanceKey,
                string addressKey,
                Address address,
                object specialService,
                ServiceDeskKind serviceKind,
                CallDialogType originalDialogType,
                int? serviceInstanceId)
            {
                ServiceInstanceKey = serviceInstanceKey;
                AddressKey = addressKey;
                Address = address;
                SpecialService = specialService;
                ServiceKind = serviceKind;
                OriginalDialogType = originalDialogType;
                ServiceInstanceId = serviceInstanceId;
            }

            internal string ServiceInstanceKey { get; }
            internal string AddressKey { get; }
            internal Address Address { get; }
            internal object SpecialService { get; }
            internal ServiceDeskKind ServiceKind { get; }
            internal CallDialogType OriginalDialogType { get; }
            internal int? ServiceInstanceId { get; }
        }

        internal sealed class ProductEligibilityResult
        {
            internal ProductEligibilityResult(Item item, bool isEligible, string reason)
            {
                Item = item;
                IsEligible = isEligible;
                Reason = reason;
            }

            internal Item Item { get; }
            internal bool IsEligible { get; }
            internal string Reason { get; }

            internal static ProductEligibilityResult Included(Item item, string reason) => new ProductEligibilityResult(item, true, reason);
            internal static ProductEligibilityResult Excluded(Item item, string reason) => new ProductEligibilityResult(item, false, reason);
        }

        internal readonly struct ProductBrowserResult
        {
            internal ProductBrowserResult(ProductEligibilityResult? selectedProduct, int productIndex, int productCount, string message, bool hasNextProduct)
            {
                SelectedProduct = selectedProduct;
                ProductIndex = productIndex;
                ProductCount = productCount;
                Message = message;
                HasNextProduct = hasNextProduct;
            }

            internal ProductEligibilityResult? SelectedProduct { get; }
            internal int ProductIndex { get; }
            internal int ProductCount { get; }
            internal string Message { get; }
            internal bool HasNextProduct { get; }
        }

        internal readonly struct BusinessBrowserResult
        {
            internal BusinessBrowserResult(BusinessTargetRecord? selectedBusiness, int businessIndex, int businessCount, string message, bool hasNextBusiness)
            {
                SelectedBusiness = selectedBusiness;
                BusinessIndex = businessIndex;
                BusinessCount = businessCount;
                Message = message;
                HasNextBusiness = hasNextBusiness;
            }

            internal BusinessTargetRecord? SelectedBusiness { get; }
            internal int BusinessIndex { get; }
            internal int BusinessCount { get; }
            internal string Message { get; }
            internal bool HasNextBusiness { get; }
        }

        internal readonly struct QuantityBrowserResult
        {
            internal QuantityBrowserResult(QuantityOption? selectedQuantity, int quantityIndex, int quantityCount, string message, bool hasNextQuantity)
            {
                SelectedQuantity = selectedQuantity;
                QuantityIndex = quantityIndex;
                QuantityCount = quantityCount;
                Message = message;
                HasNextQuantity = hasNextQuantity;
            }

            internal QuantityOption? SelectedQuantity { get; }
            internal int QuantityIndex { get; }
            internal int QuantityCount { get; }
            internal string Message { get; }
            internal bool HasNextQuantity { get; }
        }

        internal readonly struct BusinessTargetRecord
        {
            internal BusinessTargetRecord(BuildingRegistration registration, Address address, string businessName)
            {
                Registration = registration;
                Address = address;
                BusinessName = businessName;
            }

            internal BuildingRegistration Registration { get; }
            internal Address Address { get; }
            internal string BusinessName { get; }
        }

        internal readonly struct QuantityOption
        {
            internal QuantityOption(int boxes, int amount)
            {
                Boxes = boxes;
                Amount = amount;
            }

            internal int Boxes { get; }
            internal int Amount { get; }
        }

        internal readonly struct OrderCreationResult
        {
            private OrderCreationResult(bool succeeded, string message)
            {
                Succeeded = succeeded;
                Message = message;
            }

            internal bool Succeeded { get; }
            internal string Message { get; }

            internal static OrderCreationResult Success(string message) => new OrderCreationResult(true, message);
            internal static OrderCreationResult Failure(string message) => new OrderCreationResult(false, message);
        }
    }

    internal enum ServiceDeskKind
    {
        Wholesale
    }
}
