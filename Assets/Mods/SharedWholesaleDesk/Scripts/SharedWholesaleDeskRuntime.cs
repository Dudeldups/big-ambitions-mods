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
        private const int DebugCatalogPageSize = 8;

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

        internal static CatalogPageResult BuildDebugCatalogPage(int pageIndex)
        {
            var evaluations = DiscoverEligibleModdedProducts().ToList();
            if (evaluations.Count == 0)
            {
                return new CatalogPageResult(
                    0,
                    1,
                    "No eligible modded products were detected.",
                    false,
                    false);
            }

            var pageCount = Mathf.Max(1, Mathf.CeilToInt(evaluations.Count / (float)DebugCatalogPageSize));
            var clampedPageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            var entries = evaluations.Skip(clampedPageIndex * DebugCatalogPageSize).Take(DebugCatalogPageSize).ToArray();

            var builder = new StringBuilder();
            builder.Append($"Showing page {clampedPageIndex + 1}/{pageCount} ({evaluations.Count} eligible items)");

            foreach (var entry in entries)
            {
                builder.Append("<br><br>");
                builder.Append(BuildCatalogItemTitle(entry.Item));
                builder.Append("<br>");
                builder.Append(
                    $"id={entry.Item.itemName}<br>wholesalePrice={entry.Item.wholesalePrice:0.##}, defaultMarketPrice={entry.Item.DefaultMarketPrice:0.##}, boxSize={entry.Item.boxSize}, productSalesRatio={entry.Item.productSalesRatio:0.###}, maxOrderAmountPerImporter={entry.Item.maxOrderAmountPerImporter}, canPlayerDoOrder={entry.Item.canPlayerDoOrder}, isADemandedProduct={entry.Item.isADemandedProduct}");
            }

            return new CatalogPageResult(
                clampedPageIndex,
                pageCount,
                builder.ToString(),
                clampedPageIndex > 0,
                clampedPageIndex < pageCount - 1);
        }

        private static string BuildCatalogItemTitle(Item item)
        {
            var localizedName = item.itemName.GetLocalization();
            return string.Equals(localizedName, item.itemName, StringComparison.Ordinal)
                ? item.itemName
                : $"{localizedName} ({item.itemName})";
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

        internal readonly struct CatalogPageResult
        {
            internal CatalogPageResult(int pageIndex, int pageCount, string message, bool hasPreviousPage, bool hasNextPage)
            {
                PageIndex = pageIndex;
                PageCount = pageCount;
                Message = message;
                HasPreviousPage = hasPreviousPage;
                HasNextPage = hasNextPage;
            }

            internal int PageIndex { get; }
            internal int PageCount { get; }
            internal string Message { get; }
            internal bool HasPreviousPage { get; }
            internal bool HasNextPage { get; }
        }
    }

    internal enum ServiceDeskKind
    {
        Wholesale
    }
}
