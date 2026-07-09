#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BAModAPI;
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
        internal const string GeneralUSTrucksContactId = "General US Trucks";
        internal const int RestoredMaxSpeed = 80;
        internal const float RestoredEnginePower = 150f;
        internal const float RestoredBrakeForce = 6000f;
        internal const bool DebugLoggingEnabled = true;

        private static GameObject? registrarObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            MyBelovedUMCDesertLogger.SetDebugLoggingEnabled(DebugLoggingEnabled);
            MyBelovedUMCDesertLogger.Info(context, $"My Beloved UMC Desert: file log path = {MyBelovedUMCDesertFileLogger.LogPath}");

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
            GeneralUSTrucksStockService.ApplyPhoneStock(context);
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
            MyBelovedUMCDesertLogger.Info(context, $"My Beloved UMC Desert: removed legacy standalone contact count={removedCount}.");
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
                MyBelovedUMCDesertLogger.Warn(null, $"My Beloved UMC Desert: contacts UI refresh failed: {exception.Message}");
            }
        }
    }

    internal static class GeneralUSTrucksStockService
    {
        private static readonly string[] VanillaGeneralUSTrucksVehicles =
        {
            "ba:vehicletype_freighttruckt1",
            "ba:vehicletype_deliverytruck",
            "ba:vehicletype_mersaididash",
            "ba:vehicletype_umcnunavut",
            "ba:vehicletype_vordv150"
        };

        private static List<string>? previousPhoneStock;
        private static bool previousPhoneStockCaptured;
        private static string? lastAppliedStockKey;

        internal static void ApplyPhoneStock(ModContext? context)
        {
            CapturePreviousPhoneStock(context);

            var stock = CreateMergedPhoneStock();
            var stockKey = string.Join("|", stock);
            ContractItemsForSaleService.SetVehiclesForContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId, stock);

            if (!string.Equals(lastAppliedStockKey, stockKey, StringComparison.Ordinal))
            {
                lastAppliedStockKey = stockKey;
                MyBelovedUMCDesertLogger.Info(
                    context,
                    $"My Beloved UMC Desert: registered phone stock for '{MyBelovedUMCDesertMod.GeneralUSTrucksContactId}': {string.Join(", ", stock)}");
            }
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

        private static void CapturePreviousPhoneStock(ModContext? context)
        {
            if (previousPhoneStockCaptured)
                return;

            previousPhoneStockCaptured = true;
            if (ContractItemsForSaleService.TryGetVehiclesForContact(MyBelovedUMCDesertMod.GeneralUSTrucksContactId, out List<string> existingStock))
            {
                previousPhoneStock = existingStock;
                MyBelovedUMCDesertLogger.Info(
                    context,
                    $"My Beloved UMC Desert: captured existing modded phone stock for '{MyBelovedUMCDesertMod.GeneralUSTrucksContactId}': {string.Join(", ", existingStock)}");
            }
            else
            {
                previousPhoneStock = null;
                MyBelovedUMCDesertLogger.Info(
                    context,
                    $"My Beloved UMC Desert: no existing modded phone stock for '{MyBelovedUMCDesertMod.GeneralUSTrucksContactId}'.");
            }
        }

        private static List<string> CreateMergedPhoneStock()
        {
            var stock = new List<string>();
            AddUnique(stock, VanillaGeneralUSTrucksVehicles);
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

    internal static class MyBelovedUMCDesertLogger
    {
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>();
        private static bool debugLoggingEnabled;

        internal static void SetDebugLoggingEnabled(bool enabled)
        {
            debugLoggingEnabled = enabled;
        }

        internal static void Info(ModContext? context, string message)
        {
            if (!debugLoggingEnabled)
                return;

            context?.Logger.Info(message);
            MyBelovedUMCDesertFileLogger.Log(message);
        }

        internal static void Warn(ModContext? context, string message)
        {
            if (!debugLoggingEnabled)
                return;

            context?.Logger.Warn(message);
            MyBelovedUMCDesertFileLogger.Log("WARN: " + message);
        }

        internal static void WarnOnce(ModContext? context, string key, string message)
        {
            if (!WarnedKeys.Add(key))
                return;

            Warn(context, message);
        }
    }

    internal static class MyBelovedUMCDesertFileLogger
    {
        private static readonly object Sync = new object();
        private static readonly string PreferredWorkspaceLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";
        private static string? logPath;

        internal static string LogPath
        {
            get
            {
                lock (Sync)
                {
                    if (!string.IsNullOrEmpty(logPath))
                        return logPath;

                    try
                    {
                        Directory.CreateDirectory(PreferredWorkspaceLogDirectory);
                        logPath = Path.Combine(PreferredWorkspaceLogDirectory, "MyBelovedUMCDesert.log");
                    }
                    catch
                    {
                        logPath = Path.Combine(Path.GetTempPath(), "MyBelovedUMCDesert.log");
                    }

                    return logPath;
                }
            }
        }

        internal static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
