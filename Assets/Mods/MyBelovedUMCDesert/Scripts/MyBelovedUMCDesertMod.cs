#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using JimmysUnityUtilities;
using Localizor;
using Services;
using UI;
using UI.Notification;
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
        internal const int RestoredMaxSpeed = 80;
        internal const float RestoredEnginePower = 150f;
        internal const float RestoredBrakeForce = 6000f;

        private static readonly string[] VehiclesForSale = { VehicleTypeName };
        private static GameObject? registrarObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(DialogTypeKey);
            CallDialogFactory.RegisterDialog(dialogType, () => new MyBelovedUMCDesertDialog());

            EnsureRegistrar(context, dialogType);
            context.Logger.Info($"My Beloved UMC Desert: registered seller for '{VehicleTypeName}'.");
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
            return Task.CompletedTask;
        }

        private static void EnsureRegistrar(ModContext context, CallDialogType dialogType)
        {
            if (registrarObject == null)
            {
                registrarObject = new GameObject("MyBelovedUMCDesert.ContactRegistrar");
                UnityEngine.Object.DontDestroyOnLoad(registrarObject);
            }

            var registrar = registrarObject.GetComponent<MyBelovedUMCDesertContactRegistrar>();
            if (registrar == null)
                registrar = registrarObject.AddComponent<MyBelovedUMCDesertContactRegistrar>();

            registrar.Initialize(context, dialogType);
        }

        internal static void RegisterContactAndStock(CallDialogType dialogType, ModContext? context)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.Contacts == null)
                return;

            UMCDesertStatsService.ApplyRestoredStats(context);
            ContractItemsForSaleService.SetVehiclesForContact(ContactId, VehiclesForSale);

            var existedBefore = false;
            foreach (var existingContact in saveGame.Contacts)
            {
                if (existingContact != null &&
                    string.Equals(existingContact.id, ContactId, StringComparison.OrdinalIgnoreCase))
                {
                    existedBefore = true;
                    break;
                }
            }

            var contact = Contact.GetContact(
                ContactId,
                ContactCategoryName.FurnitureAndEquipment,
                ContactDescription);
            if (contact == null)
                return;

            var changed = !existedBefore || contact.callDialogTypeOverride != dialogType;
            contact.callDialogTypeOverride = dialogType;

            if (contact.messagesQueue == null || contact.messagesQueue.Count == 0)
            {
                contact.SendMessage(new TextMessage("mybelovedumcdesert:welcome"));
                changed = true;
            }

            if (changed)
            {
                saveGame.hasEverUsedMods = true;
                SaveGameManager.MarkChange();
                RefreshContactsUi(contact);
                context?.Logger.Info($"My Beloved UMC Desert: contact ready. existedBefore={existedBefore}");
            }
        }

        private static void RefreshContactsUi(Contact contact)
        {
            try
            {
                var contactsApp = InstanceBehavior<UIs>.Instance?.fullMenu?.contactsApp;
                if (contactsApp == null)
                    return;

                if (contactsApp.selectedContact == contact)
                    contactsApp.RefreshHeader();

                ContactsApp.onContactAdded?.Invoke();
            }
            catch (Exception)
            {
                // UI may not be initialized yet; the registrar will try again after the save is ready.
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

        private CallDialogType dialogType;
        private ModContext? context;
        private float nextAttemptAt;

        public void Initialize(ModContext context, CallDialogType dialogType)
        {
            this.context = context;
            this.dialogType = dialogType;
            nextAttemptAt = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAttemptAt)
                return;

            nextAttemptAt = Time.unscaledTime + RetryIntervalSeconds;
            MyBelovedUMCDesertMod.RegisterContactAndStock(dialogType, context);
        }
    }

    internal sealed class MyBelovedUMCDesertDialog : Dialog
    {
        private VehicleContractSettings? vehicleContractSettings;

        public MyBelovedUMCDesertDialog()
        {
            npcNameKey = MyBelovedUMCDesertMod.ContactId;
            DialogController.current.ShowEntry(Start());
        }

        private DialogEntry Start()
        {
            if (VehicleTypeHelper.GetVehicleType(MyBelovedUMCDesertMod.VehicleTypeName) == null)
                return Message("mybelovedumcdesert:dialog_missing_vehicle", DialogController.current.FinishDialog);

            DialogController.current.contact.SendMessage(
                new TextMessage("mybelovedumcdesert:dialog_start", null, true, true));

            return new DialogEntry
            {
                messageData = "mybelovedumcdesert:dialog_start".Localize(),
                Template = DialogEntry.TemplateType.Text,
                headerKey = npcNameKey,
                OnVisible = () =>
                {
                    VehicleContractSettings.disableDeliveryOnNextInit = true;
                    VehicleContractSettingsDialog().ShowEntry();
                }
            };
        }

        private static DialogEntry VehicleContractSettingsDialog()
        {
            return new DialogEntry
            {
                headerKey = "dialog_vehicle_store_contract_header",
                Template = DialogEntry.TemplateType.Input,
                InputTemplate = DialogEntry.InputTemplateName.VehicleContractSettings,
                OnConfirm = OnVehicleSettingsSet,
                OnCancel = DialogController.current.CancelDialog,
                onCancelMessage = new TextMessage(LegacyRef.MessageType.ContactsMessagePlayerCancelCall)
            };
        }

        private static DialogEntry? OnVehicleSettingsSet()
        {
            var dialog = DialogController.current.dialog as MyBelovedUMCDesertDialog;
            return dialog?.PurchaseSelectedVehicle();
        }

        private DialogEntry? PurchaseSelectedVehicle()
        {
            vehicleContractSettings = DialogController.current.GetInputTransform<VehicleContractSettings>(null);
            if (vehicleContractSettings == null || vehicleContractSettings.selectedVehicleForSale == null)
            {
                Notifications.ShowError("common_notification_select_vehicle");
                return null;
            }

            if (!UMCDesertPurchaseService.TryPurchase(
                    vehicleContractSettings.selectedVehicleForSale,
                    out var failureMessageKey))
            {
                if (!string.IsNullOrEmpty(failureMessageKey))
                    return Message(failureMessageKey, DialogController.current.FinishDialog);

                return null;
            }

            var messageData = new Dictionary<string, string>
            {
                { "vehicleTypeName", MyBelovedUMCDesertMod.VehicleTypeName.GetLocalization() }
            };
            DialogController.current.contact.ReceivePlayerMessage(
                new TextMessage("mybelovedumcdesert:dialog_purchased_player", null, true));
            DialogController.current.contact.SendMessage(
                new TextMessage(LegacyRef.MessageType.DialogVehicleStoreVehiclePurchasedManager, messageData, true));

            return Message("mybelovedumcdesert:dialog_purchased_manager", DialogController.current.FinishDialog);
        }

        private static DialogEntry Message(string key, Action? onVisible = null)
        {
            return new DialogEntry
            {
                messageData = key.Localize(),
                Template = DialogEntry.TemplateType.Text,
                headerKey = MyBelovedUMCDesertMod.ContactId,
                OnVisible = onVisible
            };
        }
    }

    internal static class UMCDesertPurchaseService
    {
        private const string ParkingGarageRootPath = "BuildingBlocks/BuildingBlock(5,1)/Parking01Exterior";

        public static bool TryPurchase(ContractVehicleForSale vehicleForSale, out string? failureMessageKey)
        {
            failureMessageKey = null;

            if (!string.Equals(vehicleForSale.VehicleName, MyBelovedUMCDesertMod.VehicleTypeName, StringComparison.Ordinal))
                return false;

            UMCDesertStatsService.ApplyRestoredStats(null);
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleForSale.VehicleName);
            if (vehicleType == null)
            {
                failureMessageKey = "mybelovedumcdesert:dialog_missing_vehicle";
                return false;
            }

            if (!TryGetSpawnPoint(out var spawnPosition, out var spawnRotation))
            {
                failureMessageKey = "mybelovedumcdesert:dialog_no_spawn";
                return false;
            }

            var transactionData = new Dictionary<string, string> { { "vehicleName", vehicleForSale.VehicleName } };
            if (vehicleType.taxDeductible)
                transactionData["taxDeductibleName"] = vehicleForSale.VehicleName;

            var transactionInfo = new TransactionInfo(
                LegacyRef.Transaction.VehicleBought,
                transactionData,
                vehicleType.taxDeductible);

            if (!GameManager.ChangeMoneySafe(
                    -vehicleForSale.GetPurchasePrice(),
                    transactionInfo,
                    showNotification: true))
                return false;

            var vehicleInstance = new VehicleInstance(vehicleForSale.VehicleName)
            {
                id = CreateVehicleId(),
                vehicleColorName = vehicleForSale.GetInitialColor(),
                fuel = vehicleType.maxFuel * UnityEngine.Random.Range(0.97f, 0.98f)
            };

            VehicleHelper.CreateAndSpawnVehicle(vehicleInstance, spawnPosition, spawnRotation);
            return true;
        }

        private static bool TryGetSpawnPoint(out Vector3 spawnPosition, out Quaternion spawnRotation)
        {
            spawnPosition = default;
            spawnRotation = default;

            if (VehicleParkingHelper.TryGetRandomParkingGarageSpot(
                    ParkingGarageRootPath,
                    out spawnPosition,
                    out spawnRotation))
                return true;

            var cityBuildingController = BuildingManager.Instance?.cityBuildingController;
            var customPositions = cityBuildingController?.customPositions;
            if (customPositions is { Count: > 0 })
            {
                spawnPosition = customPositions[0].position;
                spawnRotation = customPositions[0].rotation;
                return true;
            }

            var playerController = GameManager.Instance?.playerController;
            if (playerController == null)
                return false;

            spawnPosition = playerController.transform.position + playerController.transform.forward * 4f;
            spawnRotation = playerController.transform.rotation;
            return true;
        }

        private static string CreateVehicleId()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
    }
}
