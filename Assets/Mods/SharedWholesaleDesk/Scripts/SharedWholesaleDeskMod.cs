#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using UI.Dialog;
using UnityEngine;

[assembly: RegisterModClass(typeof(SharedWholesaleDesk.SharedWholesaleDeskCityMod))]

namespace SharedWholesaleDesk
{
    [ModEntryOnCityLoad]
    public sealed class SharedWholesaleDeskCityMod : IModBigAmbitions
    {
        private static GameObject? _watcherObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            SharedWholesaleDeskRuntime.Initialize(context.Logger);

            var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(SharedWholesaleDeskRuntime.ModdedDialogTypeKey);
            SharedWholesaleDeskRuntime.SetModdedDialogType(dialogType);
            CallDialogFactory.RegisterDialog(dialogType, () => new SharedWholesaleDeskDialog());
            SharedWholesaleDeskRuntime.LogInfo(
                $"Registered shared wholesale dialog type '{SharedWholesaleDeskRuntime.ModdedDialogTypeKey}' = {(int)dialogType}.");

            EnsureWatcher();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_watcherObject != null)
            {
                UnityEngine.Object.Destroy(_watcherObject);
                _watcherObject = null;
            }

            SharedWholesaleDeskRuntime.RestorePatchedServiceDesks();
            SharedWholesaleDeskRuntime.Reset();
            return Task.CompletedTask;
        }

        private static void EnsureWatcher()
        {
            if (_watcherObject == null)
            {
                _watcherObject = new GameObject("SharedWholesaleDesk.Watcher");
                UnityEngine.Object.DontDestroyOnLoad(_watcherObject);
            }

            var watcher = _watcherObject.GetComponent<SharedWholesaleDeskWatcher>();
            if (watcher == null)
                watcher = _watcherObject.AddComponent<SharedWholesaleDeskWatcher>();

            watcher.Initialize();
        }
    }

    internal sealed class SharedWholesaleDeskWatcher : MonoBehaviour
    {
        private const float RetryIntervalSeconds = 2f;
        private const int StableScanThreshold = 3;

        private float _elapsedSeconds;
        private float _nextScanAtSeconds;
        private int _stableScans;
        private bool _stopped;

        public void Initialize()
        {
            _elapsedSeconds = 0f;
            _nextScanAtSeconds = 0f;
            _stableScans = 0;
            _stopped = false;
        }

        private void Update()
        {
            if (_stopped)
                return;

            _elapsedSeconds += Time.unscaledDeltaTime;
            if (_elapsedSeconds < _nextScanAtSeconds)
                return;

            var result = SharedWholesaleDeskRuntime.TryPatchServiceDesks();
            _nextScanAtSeconds = _elapsedSeconds + RetryIntervalSeconds;

            if (!result.Ready)
                return;

            if (result.FoundTargetCount > 0 && result.PatchedCount == 0)
                _stableScans++;
            else
                _stableScans = 0;

            if (_stableScans < StableScanThreshold)
                return;

            _stopped = true;
            SharedWholesaleDeskRuntime.LogInfo(
                $"Stopping wholesale desk scan after {_stableScans} stable passes. Targets={result.FoundTargetCount}, patched={SharedWholesaleDeskRuntime.PatchedDeskCount}.");
        }
    }

    internal static class SharedWholesaleDeskRuntime
    {
        internal const string ModdedDialogTypeKey = "sharedwholesale_moddedproducts_dialog";

        private const string WholesaleStoreSettingsTypeName = "Buildings.WholesaleStoreSettings";
        private const int DebugCatalogPageSize = 8;

        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, PatchedServiceDeskRecord> PatchedDesksByKey = new();
        private static readonly Dictionary<string, List<PatchedServiceDeskRecord>> PatchedDesksByAddressKey = new();

        private static IModLogger? _logger;

        internal static CallDialogType ModdedDialogType { get; private set; }
        internal static int PatchedDeskCount => PatchedDesksByKey.Count;

        internal static void Initialize(IModLogger logger)
        {
            _logger = logger;
            LogInfo($"File logging enabled={SharedWholesaleDeskDebugSettings.EnableFileLogging}. LogPath={SharedWholesaleDeskFileLogger.LogPath}");
        }

        internal static void Reset()
        {
            PatchedDesksByKey.Clear();
            PatchedDesksByAddressKey.Clear();
            _logger = null;
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
                LogInfo("Wholesale desk patch scan skipped because save game or building registrations are unavailable.");
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
                    LogWarning($"Skipping wholesale desk at {GetAddressKey(address)} because dialogType was unavailable.");
                    continue;
                }

                var originalDialogType = (CallDialogType)Convert.ToInt32(currentDialogValue, CultureInfo.InvariantCulture);
                if (originalDialogType.Equals(ModdedDialogType))
                {
                    LogInfo(
                        $"Skipping wholesale desk at {GetAddressKey(address)} because it already uses the modded dialog type {(int)ModdedDialogType}.");
                    continue;
                }

                if (!SetMemberValue(specialService, "dialogType", ModdedDialogType))
                {
                    LogWarning(
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

                LogInfo(
                    $"Patched wholesale desk at {record.AddressKey}. ServiceKey={record.ServiceInstanceKey}, ServiceInstanceId={record.ServiceInstanceId}, OriginalDialogType={(int)record.OriginalDialogType}, ModdedDialogType={(int)ModdedDialogType}.");
            }

            if (foundTargetCount == 0)
                LogInfo("Wholesale desk patch scan found no wholesale desks yet.");

            return PatchScanResult.CreateReady(foundTargetCount, patchedCount);
        }

        internal static void RestorePatchedServiceDesks()
        {
            foreach (var record in PatchedDesksByKey.Values.ToArray())
            {
                var currentDialogValue = GetMemberValue(record.SpecialService, "dialogType");
                if (currentDialogValue == null)
                {
                    LogWarning(
                        $"Restore skipped for wholesale desk at {record.AddressKey} because dialogType was unavailable.");
                    continue;
                }

                var currentDialogType = (CallDialogType)Convert.ToInt32(currentDialogValue, CultureInfo.InvariantCulture);
                if (!currentDialogType.Equals(ModdedDialogType))
                {
                    LogInfo(
                        $"Restore skipped for wholesale desk at {record.AddressKey} because current dialog {(int)currentDialogType} no longer belongs to this mod.");
                    continue;
                }

                if (!SetMemberValue(record.SpecialService, "dialogType", record.OriginalDialogType))
                {
                    LogWarning(
                        $"Restore failed for wholesale desk at {record.AddressKey}. Original dialog {(int)record.OriginalDialogType} could not be reassigned.");
                    continue;
                }

                LogInfo(
                    $"Restored wholesale desk at {record.AddressKey} to original dialog {(int)record.OriginalDialogType}.");
            }
        }

        internal static PatchedServiceDeskRecord? TryGetCurrentDeskRecord()
        {
            var address = DialogController.current?.contact?.Address;
            if (address == null)
                return null;

            var addressKey = GetAddressKey(address);
            if (!PatchedDesksByAddressKey.TryGetValue(addressKey, out var records) || records.Count == 0)
                return null;

            return records[0];
        }

        internal static bool TryOpenOriginalVanillaDialog(PatchedServiceDeskRecord record)
        {
            try
            {
                LogInfo(
                    $"Attempting vanilla delegation for wholesale desk at {record.AddressKey} using original dialog {(int)record.OriginalDialogType}.");
                var dialog = CallDialogFactory.GetDialog(record.OriginalDialogType);
                if (dialog == null)
                {
                    LogWarning(
                        $"Vanilla delegation returned null for wholesale desk at {record.AddressKey}. Falling back to confirmed wholesale dialog constructor.");
                    return TryOpenConfirmedVanillaFallback(record);
                }

                LogInfo(
                    $"Vanilla delegation succeeded for wholesale desk at {record.AddressKey} using original dialog {(int)record.OriginalDialogType}.");
                return true;
            }
            catch (Exception exception)
            {
                LogWarning(
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
                    "Shared wholesale debug catalog<br><br>No eligible modded products were detected.",
                    false,
                    false);
            }

            var pageCount = Mathf.Max(1, Mathf.CeilToInt(evaluations.Count / (float)DebugCatalogPageSize));
            var clampedPageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            var entries = evaluations
                .Skip(clampedPageIndex * DebugCatalogPageSize)
                .Take(DebugCatalogPageSize)
                .ToArray();

            var builder = new StringBuilder();
            builder.Append("Shared wholesale debug catalog");
            builder.Append("<br><br>");
            builder.Append($"Showing {clampedPageIndex + 1}/{pageCount} ({evaluations.Count} eligible items)");

            foreach (var entry in entries)
            {
                builder.Append("<br><br>");
                builder.Append(entry.Item.itemName);
                builder.Append("<br>");
                builder.Append(
                    $"wholesalePrice={entry.Item.wholesalePrice:0.##}, defaultMarketPrice={entry.Item.DefaultMarketPrice:0.##}, boxSize={entry.Item.boxSize}, productSalesRatio={entry.Item.productSalesRatio:0.###}, maxOrderAmountPerImporter={entry.Item.maxOrderAmountPerImporter}, canPlayerDoOrder={entry.Item.canPlayerDoOrder}, isADemandedProduct={entry.Item.isADemandedProduct}");
            }

            return new CatalogPageResult(
                clampedPageIndex,
                pageCount,
                builder.ToString(),
                clampedPageIndex > 0,
                clampedPageIndex < pageCount - 1);
        }

        internal static void LogInfo(string message)
        {
            try
            {
                _logger?.Info(message);
            }
            catch
            {
            }

            if (SharedWholesaleDeskDebugSettings.EnableFileLogging)
                SharedWholesaleDeskFileLogger.Log("INFO", message);

            Debug.Log($"SharedWholesaleDesk: {message}");
        }

        internal static void LogWarning(string message)
        {
            try
            {
                _logger?.Warn(message);
            }
            catch
            {
            }

            if (SharedWholesaleDeskDebugSettings.EnableFileLogging)
                SharedWholesaleDeskFileLogger.Log("WARN", message);

            Debug.LogWarning($"SharedWholesaleDesk: {message}");
        }

        private static bool TryOpenConfirmedVanillaFallback(PatchedServiceDeskRecord record)
        {
            try
            {
                LogInfo(
                    $"Using confirmed fallback constructor for wholesale desk at {record.AddressKey}: {typeof(WholesaleStoreManagerDialog).FullName}.");
                _ = new WholesaleStoreManagerDialog();
                return true;
            }
            catch (Exception exception)
            {
                LogWarning(
                    $"Confirmed fallback constructor failed for wholesale desk at {record.AddressKey}. {exception}");
                return false;
            }
        }

        private static IEnumerable<ProductEligibilityResult> DiscoverEligibleModdedProducts()
        {
            if (ItemsGetter.AllItems == null)
            {
                LogWarning("Product discovery skipped because ItemsGetter.AllItems is unavailable.");
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
                    LogInfo("Excluded non-ba item candidate with missing item ID: itemName was null or whitespace.");
                    continue;
                }

                if (itemName.StartsWith("ba:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var evaluation = EvaluateItemEligibility(item);
                LogInfo($"{(evaluation.IsEligible ? "Included" : "Excluded")} non-ba item '{itemName}': {evaluation.Reason}");
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
                LogWarning($"Failed to resolve building at {GetAddressKey(address)}. {exception}");
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

            public static PatchScanResult NotReady() => new(false, 0, 0);
            public static PatchScanResult CreateReady(int foundTargetCount, int patchedCount) => new(true, foundTargetCount, patchedCount);
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

            public static ProductEligibilityResult Included(Item item, string reason) => new(item, true, reason);
            public static ProductEligibilityResult Excluded(Item item, string reason) => new(item, false, reason);
        }

        internal readonly struct CatalogPageResult
        {
            internal CatalogPageResult(
                int pageIndex,
                int pageCount,
                string message,
                bool hasPreviousPage,
                bool hasNextPage)
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

    internal sealed class SharedWholesaleDeskDialog : Dialog
    {
        private int _catalogPageIndex;

        public SharedWholesaleDeskDialog()
        {
            var record = SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord();
            npcNameKey = "dialog_wholesale_store_npc_name";

            SharedWholesaleDeskRuntime.LogInfo(
                $"Opened custom shared wholesale dialog for {(record?.ServiceKind.ToString() ?? "unknown")} desk at {record?.AddressKey ?? "<no-address>"}.");

            DialogController.current.ShowEntry(BuildStartEntry(record));
        }

        private DialogEntry BuildStartEntry(SharedWholesaleDeskRuntime.PatchedServiceDeskRecord? record)
        {
            return new DialogEntry
            {
                headerKey = npcNameKey,
                messageData = "Shared wholesale access proof of concept<br><br>Select the original vanilla wholesale flow or open the debug modded-products catalog.",
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = "Original Wholesale Contract",
                SecondOptionTextOverride = "Modded Products",
                OnConfirm = () => OpenOriginalVanillaBranch(record),
                OnSecondOption = OpenDebugCatalog,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry? OpenOriginalVanillaBranch(SharedWholesaleDeskRuntime.PatchedServiceDeskRecord? record)
        {
            if (record == null)
            {
                SharedWholesaleDeskRuntime.LogWarning("Original vanilla branch could not resolve the current patched wholesale desk record.");
                return BuildErrorEntry("The original wholesale desk mapping could not be resolved.");
            }

            var opened = SharedWholesaleDeskRuntime.TryOpenOriginalVanillaDialog(record);
            return opened ? null : BuildErrorEntry("The original vanilla wholesale desk dialog failed to open.");
        }

        private DialogEntry OpenDebugCatalog()
        {
            _catalogPageIndex = 0;
            return BuildCatalogEntry();
        }

        private DialogEntry BuildCatalogEntry()
        {
            var page = SharedWholesaleDeskRuntime.BuildDebugCatalogPage(_catalogPageIndex);
            return new DialogEntry
            {
                headerKey = "Shared Wholesale Debug Catalog",
                messageData = page.Message,
                Template = DialogEntry.TemplateType.Text,
                ConfirmTextOverride = page.HasNextPage ? "Next Page" : "Back",
                SecondOptionTextOverride = page.HasPreviousPage ? "Previous Page" : null,
                OnConfirm = page.HasNextPage ? NextCatalogPage : ReturnToStart,
                OnSecondOption = page.HasPreviousPage ? PreviousCatalogPage : null,
                OnCancel = DialogController.current.CancelDialog
            };
        }

        private DialogEntry NextCatalogPage()
        {
            _catalogPageIndex++;
            return BuildCatalogEntry();
        }

        private DialogEntry PreviousCatalogPage()
        {
            _catalogPageIndex = Mathf.Max(0, _catalogPageIndex - 1);
            return BuildCatalogEntry();
        }

        private DialogEntry ReturnToStart()
        {
            return BuildStartEntry(SharedWholesaleDeskRuntime.TryGetCurrentDeskRecord());
        }

        private static DialogEntry BuildErrorEntry(string message)
        {
            return new DialogEntry
            {
                messageData = message,
                Template = DialogEntry.TemplateType.Text,
                OnCancel = DialogController.current.FinishDialog
            };
        }
    }

    internal static class SharedWholesaleDeskDebugSettings
    {
        internal const bool EnableFileLogging = true;
    }

    internal static class SharedWholesaleDeskFileLogger
    {
        private static readonly object Sync = new object();
        private static string? _logPath;

        internal static string LogPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_logPath))
                    return _logPath;

                try
                {
                    var directory = Path.Combine(Application.persistentDataPath, "SharedWholesaleDesk");
                    Directory.CreateDirectory(directory);
                    _logPath = Path.Combine(directory, "shared-wholesale-debug.log");
                }
                catch
                {
                    _logPath = Path.Combine(Path.GetTempPath(), "shared-wholesale-debug.log");
                }

                return _logPath;
            }
        }

        internal static void Log(string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
