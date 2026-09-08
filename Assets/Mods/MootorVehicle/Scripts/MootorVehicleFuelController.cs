#nullable enable
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.Items;
using Helpers;
using UI.Overlays;
using UnityEngine;

namespace MootorVehicle
{
    /// <summary>
    /// Handles cow-specific fuel interactions. All work is driven by mount and trigger events;
    /// this component deliberately has no Update loop.
    /// </summary>
    internal sealed class MootorVehicleFuelController : MonoBehaviour
    {
        private const string EnergyDrinkItemName = "ba:itemname_energydrink";
        private const float DefaultMaximumFuel = 100f;

        private readonly HashSet<int> suppressedRefuelStations = new();
        private VehicleController? vehicle;
        private ModContext? context;
        private readonly float configuredMaximumFuel = DefaultMaximumFuel;
        private ItemInstance? heldItemAwaitingRestore;
        private bool mounted;

        internal void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
            context = modContext;

            if (controller.controlledByPlayer)
                NotifyMounted();
        }

        internal void NotifyMounted()
        {
            if (vehicle == null || mounted)
                return;

            mounted = true;
            heldItemAwaitingRestore = PlayerHelper.ItemInstanceInHands;
            TryFeedFromHands();
            SuppressOverlappingRefuelStations();
        }

        internal void NotifyDismounted()
        {
            mounted = false;
            suppressedRefuelStations.Clear();
            if (heldItemAwaitingRestore != null)
                StartCoroutine(RestoreHeldItemAfterDismount(heldItemAwaitingRestore));
        }

        private void TryFeedFromHands()
        {
            var heldItem = PlayerHelper.ItemInstanceInHands;
            if (vehicle == null || heldItem == null)
                return;

            CargoInstance? boxedEnergyDrink = null;
            if (heldItem.itemName != EnergyDrinkItemName)
            {
                foreach (var cargo in heldItem.GetCargoInstances())
                    if (cargo != null && cargo.itemName == EnergyDrinkItemName && cargo.amount > 0)
                    {
                        boxedEnergyDrink = cargo;
                        break;
                    }

                if (boxedEnergyDrink == null)
                    return;
            }

            var fuelBefore = vehicle.GetCurrentFuel();
            vehicle.SetFuel(configuredMaximumFuel);
            var fuelAfter = vehicle.GetCurrentFuel();
            if (fuelAfter + 0.01f < configuredMaximumFuel)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle feed vehicle={vehicle.GetInstanceID()} failed " +
                    $"fuelBefore={fuelBefore:F2} fuelAfter={fuelAfter:F2} target={configuredMaximumFuel:F2}; " +
                    "energy drink was retained.");
                return;
            }

            if (boxedEnergyDrink != null)
            {
                heldItem.ReduceFromCargo(boxedEnergyDrink, 1);
                // VehicleController.EnterVehicle is still executing here. Refreshing the held-item
                // HUD now dereferences the walking item panel after it has switched to vehicle mode,
                // throwing out of the entry callback and leaving the cow only partially mounted.
                // Keep the cargo mutation; re-adding the same held instance after the normal exit path
                // restores both the updated cargo display and the walking item interactions.
            }
            else
            {
                // The PlayerHelper setter performs the normal held-item cleanup and HUD refresh.
                PlayerHelper.ItemInstanceInHands = null;
            }

            context?.Logger.Info(
                $"Moo-tor Vehicle feed vehicle={vehicle.GetInstanceID()} item='{EnergyDrinkItemName}' " +
                $"source={(boxedEnergyDrink != null ? "box" : "hands")} " +
                $"fuelBefore={fuelBefore:F2} fuelAfter={fuelAfter:F2}; energy drink consumed.");
        }

        private IEnumerator RestoreHeldItemAfterDismount(ItemInstance heldItem)
        {
            yield return null;
            if (heldItemAwaitingRestore != heldItem)
                yield break;

            heldItemAwaitingRestore = null;
            if (PlayerHelper.ItemInstanceInHands != heldItem)
                yield break;

            try
            {
                // Reassigning uses PlayerHelper's standard remove/add lifecycle. The ItemInstance is
                // unchanged, so this recreates the hand object and item panel without moving inventory.
                PlayerHelper.ItemInstanceInHands = heldItem;
                context?.Logger.Info(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()}: restored held item " +
                    $"'{heldItem.itemName}' after dismount.");
            }
            catch (System.Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle rider vehicle={vehicle?.GetInstanceID()} could not restore " +
                    $"held item '{heldItem.itemName}' after dismount: " +
                    exception.GetBaseException().Message);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var station = FindRefuelStation(other);
            if (station == null || !mounted)
                return;

            SuppressRefuelStation(station, "trigger-enter");
        }

        private static GasStationTrigger? FindRefuelStation(Collider other)
        {
            var station = other.GetComponent<GasStationTrigger>() ??
                          other.GetComponentInParent<GasStationTrigger>();
            return station != null && station.isRefuelStation ? station : null;
        }

        private void SuppressOverlappingRefuelStations()
        {
            if (vehicle == null)
                return;

            var vehicleCollider = vehicle.vehicleCollider ?? vehicle.GetComponentInChildren<Collider>();
            if (vehicleCollider == null)
                return;

            foreach (var station in FindObjectsOfType<GasStationTrigger>(true))
                if (station != null && station.isRefuelStation && station.stationCollider != null &&
                    station.stationCollider.bounds.Intersects(vehicleCollider.bounds))
                {
                    SuppressRefuelStation(station, "mount-overlap");
                }
        }

        private void SuppressRefuelStation(GasStationTrigger station, string source)
        {
            if (!mounted)
                return;

            // GasStationController can show its overlay from its vehicle-enter callback without
            // applying GasStationTrigger's normal vehicle eligibility check. Hide it immediately,
            // then once more after all callbacks from the current frame have completed.
            GasStationOverlay.Hide();
            var stationId = station.GetInstanceID();
            if (suppressedRefuelStations.Add(stationId))
                StartCoroutine(HideGasStationOverlayAfterCallbacks(stationId, source));
        }

        private IEnumerator HideGasStationOverlayAfterCallbacks(int stationId, string source)
        {
            yield return null;
            if (!mounted)
                yield break;

            GasStationOverlay.Hide();
            context?.Logger.Info(
                $"Moo-tor Vehicle gas-station overlay suppressed vehicle={vehicle?.GetInstanceID()} " +
                $"station={stationId} source='{source}'.");
        }

        private void OnDisable()
        {
            suppressedRefuelStations.Clear();
            mounted = false;
        }
    }
}
