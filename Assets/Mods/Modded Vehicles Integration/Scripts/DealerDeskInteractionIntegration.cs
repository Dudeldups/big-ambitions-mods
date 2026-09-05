#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Buildings;
using Dialogs;
using JimmysUnityUtilities;
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

        internal void Update(ModContext? context)
        {
            try
            {
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
