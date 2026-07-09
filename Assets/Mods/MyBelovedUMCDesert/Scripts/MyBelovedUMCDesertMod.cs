#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BAModAPI;
using Blueprints;
using BusinessLayoutSets;
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
        internal const float RestoredPrice = 40500f;
        internal const float RestoredEnginePower = 150f;
        internal const float RestoredBrakeForce = 6000f;

        internal static readonly bool EnableShowroomDebugLogging = false;

        private static GameObject? registrarObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            MyBelovedUMCDesertFileLogger.Initialize(
                context.ModId,
                context.Logger,
                EnableShowroomDebugLogging);
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
            MyBelovedUMCDesertFileLogger.Shutdown();
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

            RemoveLegacyContact(context);
        }

        internal static void ApplyShowroomReplacement()
        {
            GeneralUSTrucksShowroomReplacementService.ApplyLayoutPatch();
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

    internal static class MyBelovedUMCDesertFileLogger
    {
        private const string PreferredLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

        private static readonly object Gate = new object();

        private static string? filePath;
        private static IModLogger? gameLogger;
        private static bool enabled;
        private static bool initialized;

        internal static bool Enabled => enabled && initialized;
        internal static string? FilePath => filePath;

        internal static void Initialize(string modId, IModLogger? logger, bool enableFileLogging)
        {
            enabled = enableFileLogging;
            gameLogger = logger;

            if (!enabled)
            {
                initialized = false;
                filePath = null;
                return;
            }

            try
            {
                var directory = ResolveLogDirectory();
                Directory.CreateDirectory(directory);

                filePath = Path.Combine(directory, "MyBelovedUMCDesert-showroom-debug.log");
                File.WriteAllText(
                    filePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] My Beloved UMC Desert showroom debug started. modId={modId}, logDirectory={directory}{Environment.NewLine}");

                initialized = true;
                gameLogger?.Info($"My Beloved UMC Desert debug log: {filePath}");
            }
            catch (Exception exception)
            {
                initialized = false;
                filePath = null;
                gameLogger?.Warn($"My Beloved UMC Desert file logger failed: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static string ResolveLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(PreferredLogDirectory);
                return PreferredLogDirectory;
            }
            catch
            {
                var fallback = Path.Combine(Path.GetTempPath(), "MyBelovedUMCDesert", "Logs");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        internal static void Shutdown()
        {
            Info("Debug log closed.");
            initialized = false;
            enabled = false;
            filePath = null;
            gameLogger = null;
        }

        internal static void Info(string message)
        {
            Write("INFO", message);
        }

        internal static void Warn(string message)
        {
            Write("WARN", message);
        }

        private static void Write(string level, string message)
        {
            if (!Enabled || string.IsNullOrEmpty(filePath))
                return;

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(
                        filePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Avoid recursive logging if the file system is unavailable.
            }
        }
    }

    internal static class GeneralUSTrucksShowroomReplacementService
    {
        private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
        private const string TargetBuildingSize = "ba:buildingsize_m";
        private const int TargetBuildingVersion = 1;
        private const string TargetLayoutName = "IndustryCityCarDealershipTrucks";
        private const string TargetOriginalItemName = "ba:itemname_deliverytruckshowcase";
        private const float PositionTolerance = 0.35f;

        private static readonly Vector3 TargetPosition = new Vector3(1035f, 0f, -152f);

        private static bool hasPatchedLayout;
        private static bool hasLoggedPatch;
        private static int patchAttemptCount;

        internal static void ApplyLayoutPatch()
        {
            patchAttemptCount++;
            MyBelovedUMCDesertFileLogger.Info(
                $"ApplyLayoutPatch attempt={patchAttemptCount}, hasPatchedLayout={hasPatchedLayout}, targetPosition={FormatVector(TargetPosition)}, targetOriginalItem={TargetOriginalItemName}, replacementItem={MyBelovedUMCDesertMod.ShowcaseItemName}.");

            if (hasPatchedLayout)
            {
                MyBelovedUMCDesertFileLogger.Info("ApplyLayoutPatch skipped because layout was already patched.");
                return;
            }

            if (TryPatchKnownTruckDealerLayout())
                return;

            var registration = FindGeneralUSTrucksRegistration();
            if (registration == null ||
                registration.BuildingCached == null ||
                string.IsNullOrEmpty(registration.businessTypeName) ||
                string.IsNullOrEmpty(registration.Layout))
            {
                MyBelovedUMCDesertFileLogger.Warn("General US Trucks registration was not ready; layout patch not applied on this attempt.");
                return;
            }

            MyBelovedUMCDesertFileLogger.Info(
                $"Registration fallback found businessName={registration.BusinessName}, businessType={registration.businessTypeName}, layout={registration.Layout}, buildingSize={registration.BuildingCached.BuildingSize}, buildingVersion={registration.BuildingCached.BuildingVersion}.");

            var layoutSet = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                registration.businessTypeName,
                new BuildingSizeInfo(registration.BuildingCached),
                registration.Layout.ToLowerInvariant(),
                false);
            if (layoutSet?.Items == null)
            {
                MyBelovedUMCDesertFileLogger.Warn("Registration fallback layout set was null or had no item list.");
                return;
            }

            TryPatchLayoutSet(layoutSet, "registration");
        }

        private static bool TryPatchKnownTruckDealerLayout()
        {
            var layoutSet = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                TargetBusinessTypeName,
                new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                TargetLayoutName.ToLowerInvariant(),
                false);
            if (layoutSet?.Items == null)
            {
                MyBelovedUMCDesertFileLogger.Warn(
                    $"Known truck dealer layout was not available businessType={TargetBusinessTypeName}, buildingSize={TargetBuildingSize}, buildingVersion={TargetBuildingVersion}, layout={TargetLayoutName}.");
                return false;
            }

            return TryPatchLayoutSet(layoutSet, "known-layout");
        }

        private static bool TryPatchLayoutSet(BusinessLayoutSet layoutSet, string source)
        {
            if (layoutSet?.Items == null)
                return false;

            var itemCount = 0;
            foreach (var item in layoutSet.Items)
            {
                itemCount++;
                if (item == null || !IsAtTargetPosition(item.position))
                    continue;

                MyBelovedUMCDesertFileLogger.Info(
                    $"Exact target position candidate source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, id={item.id}, item={item.itemName}, purchaserItem={item.playerItemPurchaserSettings?.itemName}, position={FormatVector(item.position)}.");

                if (!string.Equals(item.itemName, TargetOriginalItemName, StringComparison.Ordinal) &&
                    !string.Equals(item.itemName, MyBelovedUMCDesertMod.ShowcaseItemName, StringComparison.Ordinal))
                {
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Not patching exact-position item because itemName '{item.itemName}' is not '{TargetOriginalItemName}'.");
                    continue;
                }

                MyBelovedUMCDesertFileLogger.Info(
                    $"Patching exact showroom layout item source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, id={item.id}, from={item.itemName}, to={MyBelovedUMCDesertMod.ShowcaseItemName}.");

                item.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;
                if (item.playerItemPurchaserSettings != null)
                    item.playerItemPurchaserSettings.itemName = MyBelovedUMCDesertMod.ShowcaseItemName;

                hasPatchedLayout = true;
                if (!hasLoggedPatch)
                {
                    hasLoggedPatch = true;
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Patched exact showroom layout item source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, id={item.id}, position={FormatVector(item.position)}, item={MyBelovedUMCDesertMod.ShowcaseItemName}.");
                }

                return true;
            }

            MyBelovedUMCDesertFileLogger.Info(
                $"Scanned layout without target match source={source}, layout={layoutSet.LayoutName}, businessType={layoutSet.BusinessType}, itemCount={itemCount}.");
            return false;
        }

        private static BuildingRegistration? FindGeneralUSTrucksRegistration()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null)
                return null;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null)
                    continue;

                if (string.Equals(
                        registration.BusinessName,
                        MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                        StringComparison.Ordinal))
                {
                    MyBelovedUMCDesertFileLogger.Info(
                        $"Found General US Trucks registration address={registration.Address}, layout={registration.Layout}, businessType={registration.businessTypeName}.");
                    return registration;
                }
            }

            MyBelovedUMCDesertFileLogger.Warn("General US Trucks registration was not found in SaveGameManager.Current.BuildingRegistrations.");
            return null;
        }

        private static bool IsAtTargetPosition(Vector3 position)
        {
            return Vector3.SqrMagnitude(position - TargetPosition) <= PositionTolerance * PositionTolerance;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
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

            if (Math.Abs(vehicleType.price - MyBelovedUMCDesertMod.RestoredPrice) > 0.01f)
            {
                vehicleType.price = MyBelovedUMCDesertMod.RestoredPrice;
                changed = true;
            }

            if ((changed || !hasLoggedStats) && context != null)
            {
                hasLoggedStats = true;
                context.Logger.Info(
                    "My Beloved UMC Desert: restored vehicle stats " +
                    $"maxSpeed={vehicleType.maxSpeed}, " +
                    $"enginePower={vehicleType.enginePower}, " +
                    $"brakeForce={vehicleType.brakeForce}, " +
                    $"price={vehicleType.price}.");
            }
        }
    }

    internal sealed class MyBelovedUMCDesertContactRegistrar : MonoBehaviour
    {
        private ModContext? context;
        private bool initialized;

        public void Initialize(ModContext context)
        {
            this.context = context;
            if (initialized)
                return;

            initialized = true;
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);
            RegisterGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedCallback(OnGameLoaded);
        }

        private void OnDestroy()
        {
            UnregisterGlobalEvents();
        }

        private void RegisterGlobalEvents()
        {
            GlobalEvents.onEnterBuilding = (Action<Address>)Delegate.Remove(
                GlobalEvents.onEnterBuilding,
                new Action<Address>(OnEnterBuilding));
            GlobalEvents.onEnterBuilding = (Action<Address>)Delegate.Combine(
                GlobalEvents.onEnterBuilding,
                new Action<Address>(OnEnterBuilding));
        }

        private void UnregisterGlobalEvents()
        {
            GlobalEvents.onEnterBuilding = (Action<Address>)Delegate.Remove(
                GlobalEvents.onEnterBuilding,
                new Action<Address>(OnEnterBuilding));
        }

        private void OnGameLoaded()
        {
            MyBelovedUMCDesertFileLogger.Info("OnGameLoaded: refreshing dealer stock and attempting showroom layout patch.");
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);
            MyBelovedUMCDesertMod.ApplyShowroomReplacement();
        }

        private void OnEnterBuilding(Address address)
        {
            if (!IsGeneralUSTrucks(address))
            {
                MyBelovedUMCDesertFileLogger.Info($"OnEnterBuilding ignored address={address}.");
                return;
            }

            MyBelovedUMCDesertFileLogger.Info($"OnEnterBuilding General US Trucks address={address}: refreshing dealer stock and attempting showroom layout patch.");
            MyBelovedUMCDesertMod.RegisterDealerPhoneStock(context);
            MyBelovedUMCDesertMod.ApplyShowroomReplacement();
        }

        private static bool IsGeneralUSTrucks(Address address)
        {
            if (address == null)
                return false;

            var registration = BuildingHelper.GetBuildingRegistration(address);
            return string.Equals(
                registration?.BusinessName,
                MyBelovedUMCDesertMod.GeneralUSTrucksContactId,
                StringComparison.Ordinal);
        }
    }
}
