#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Buildings;
using Dialogs;
using JimmysUnityUtilities;
using Player.HUD.ItemInfoOverlays;
using Services;
using UI.Notification;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace ModdedVehiclesIntegration
{
    internal sealed class DealerDeskInteractionIntegration
    {
        private const string NoVehiclesNotificationKey = "modded-vehicles-integration:no_mod_vehicles";

        private static readonly HashSet<string> InteractiveDealerContactIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "City Cars",
            "Manhattan Luxury Cars",
            "The Hamptons Axis"
        };

        private Vector3 lastStandingPosition;
        private Quaternion lastStandingRotation;
        private bool hasStandingSnapshot;
        private bool dealerDialogWasOpen;
        private bool failureLogged;
        private string lastDiagnosedDealerSignature = string.Empty;
        private SpecialEmployeeController? directInteractionDesk;
        private Action? directInteractionAction;
        private Action? synchronizeCatalogBeforeDialog;

        internal void SetCatalogSynchronizer(Action synchronizer)
        {
            synchronizeCatalogBeforeDialog = synchronizer;
        }

        internal void Update(ModContext? context)
        {
            try
            {
                LogActiveDealerState(context);
                InstallDirectDeskInteraction(context);

                var character = InstanceBehavior<GameManager>.Instance?.playerController?.Character;
                if (character == null)
                    return;

                var dialog = DialogController.current;
                var contactId = dialog?.contact?.id;
                var isDealerVehicleDialog =
                    dialog?.dialogType == DialogType.Physical &&
                    dialog.dialog is VehicleStoreDialog &&
                    !string.IsNullOrEmpty(contactId) &&
                    InteractiveDealerContactIds.Contains(contactId!);

                if (!isDealerVehicleDialog)
                {
                    dealerDialogWasOpen = false;
                    if (!character.isSittingOn)
                    {
                        lastStandingPosition = character.transform.position;
                        lastStandingRotation = character.transform.rotation;
                        hasStandingSnapshot = true;
                    }

                    return;
                }

                if (dealerDialogWasOpen)
                    return;

                dealerDialogWasOpen = true;
                var wasForcedToSit = character.isSittingOn != null;
                var modVehicleCount = GetModVehicleCount(contactId!);

                if (modVehicleCount == 0)
                {
                    dialog!.FinishDialog();
                    RestoreStandingPosition(character);
                    Notifications.ShowError(NoVehiclesNotificationKey, NoVehiclesNotificationKey, true);
                    context?.Logger.Warn(
                        $"Modded Vehicles Integration: desk clicked at '{contactId}', but its mod-only catalogue is empty; " +
                        "closed the dialog to prevent the native vanilla-showroom fallback.");
                    dealerDialogWasOpen = false;
                    return;
                }

                character.Reset();
                var restoredPosition = RestoreStandingPosition(character);
                context?.Logger.Info(
                    $"Modded Vehicles Integration: desk interaction opened for '{contactId}': " +
                    $"modVehicles={modVehicleCount}, forcedSeat={wasForcedToSit}, " +
                    $"standingPositionRestored={restoredPosition}.");
            }
            catch (Exception exception)
            {
                if (failureLogged)
                    return;

                failureLogged = true;
                context?.Logger.Error(exception);
            }
        }

        internal void Shutdown()
        {
            if (directInteractionAction != null && ReferenceEquals(CtaManager.ctaAction, directInteractionAction))
                CtaManager.Clear();

            directInteractionDesk = null;
            directInteractionAction = null;
            synchronizeCatalogBeforeDialog = null;
        }

        private void InstallDirectDeskInteraction(ModContext? context)
        {
            var registration = InstanceBehavior<BuildingManager>.Instance?.buildingRegistration;
            var dealerContactId = registration?.BusinessName;
            var selectedDesk = MouseController.currentTargetEntity as SpecialEmployeeController;
            if (!BuildingManager.IsInsideBuilding ||
                string.IsNullOrEmpty(dealerContactId) ||
                !InteractiveDealerContactIds.Contains(dealerContactId!) ||
                selectedDesk == null ||
                selectedDesk.GetEmployeeType != SpecialEmployeeController.SpecialEmployeeType.VehicleStore)
            {
                if (directInteractionAction != null && ReferenceEquals(CtaManager.ctaAction, directInteractionAction))
                    CtaManager.Clear();

                directInteractionDesk = null;
                directInteractionAction = null;
                return;
            }

            if (!ReferenceEquals(directInteractionDesk, selectedDesk) || directInteractionAction == null)
            {
                directInteractionDesk = selectedDesk;
                directInteractionAction = () => InteractWithDeskDirectly(selectedDesk, dealerContactId!, context);
            }

            CtaManager.ctaAction = directInteractionAction;
        }

        private void InteractWithDeskDirectly(
            SpecialEmployeeController desk,
            string dealerContactId,
            ModContext? context)
        {
            var playerController = InstanceBehavior<GameManager>.Instance?.playerController;
            var character = playerController?.Character;
            if (playerController == null || character == null)
                return;

            var navTarget = desk.GetClosestNavMeshTargetPosition(character.transform.position);
            var hasRoute = navTarget != Vector3.zero && playerController.ExistsRoute(navTarget, false, desk);
            playerController.RemoveGoal();

            context?.Logger.Info(
                $"Modded Vehicles Integration: direct dealer desk click at '{dealerContactId}': " +
                $"deskPosition={desk.transform.position}, navTarget={navTarget}, hasRoute={hasRoute}; " +
                "opening the vehicle-store dialog without moving the player.");

            try
            {
                synchronizeCatalogBeforeDialog?.Invoke();
            }
            catch (Exception exception)
            {
                context?.Logger.Error(exception);
            }

            var interactionAccepted = desk.Interact();
            InstanceBehavior<OverlayManager>.Instance?.HideSimpleOverlayAndClearCta();
            context?.Logger.Info(
                $"Modded Vehicles Integration: direct dealer desk interaction returned {interactionAccepted} " +
                $"for '{dealerContactId}'.");
        }

        private void LogActiveDealerState(ModContext? context)
        {
            var buildingManager = InstanceBehavior<BuildingManager>.Instance;
            var registration = buildingManager?.buildingRegistration;
            var dealerContactId = registration?.BusinessName;
            if (!BuildingManager.IsInsideBuilding ||
                string.IsNullOrEmpty(dealerContactId) ||
                !InteractiveDealerContactIds.Contains(dealerContactId!))
            {
                lastDiagnosedDealerSignature = string.Empty;
                return;
            }

            var dealerSignature = $"{dealerContactId}|{registration!.Layout}";
            if (string.Equals(dealerSignature, lastDiagnosedDealerSignature, StringComparison.Ordinal))
                return;

            lastDiagnosedDealerSignature = dealerSignature;
            var specialEmployeeDesks = UnityEngine.Object.FindObjectsOfType<SpecialEmployeeController>();
            var vehicleStoreDesks = 0;
            foreach (var specialEmployeeDesk in specialEmployeeDesks)
            {
                if (specialEmployeeDesk != null &&
                    specialEmployeeDesk.GetEmployeeType == SpecialEmployeeController.SpecialEmployeeType.VehicleStore)
                {
                    vehicleStoreDesks++;
                }
            }

            var message =
                $"Modded Vehicles Integration: active dealer diagnostics: dealer='{dealerContactId}', " +
                $"layout='{registration.Layout}', specialEmployeeDesks={specialEmployeeDesks.Length}, " +
                $"vehicleStoreDesks={vehicleStoreDesks}.";
            if (vehicleStoreDesks == 0)
                context?.Logger.Warn(message);
            else
                context?.Logger.Info(message);
        }

        private bool RestoreStandingPosition(ThirdPersonCharacter character)
        {
            if (!hasStandingSnapshot)
                return false;

            character.WarpSafely(lastStandingPosition);
            character.ForceToRotation(lastStandingRotation);
            return true;
        }

        private static int GetModVehicleCount(string contactId)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> vehicleTypeNames) ||
                vehicleTypeNames == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var vehicleTypeName in vehicleTypeNames)
            {
                if (!string.IsNullOrEmpty(vehicleTypeName) && VehicleTypeHelper.IsModVehicleType(vehicleTypeName))
                    count++;
            }

            return count;
        }
    }
}
