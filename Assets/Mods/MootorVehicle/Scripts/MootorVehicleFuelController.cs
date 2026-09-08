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

        private readonly HashSet<GasStationTrigger> nearbyRefuelStations = new();
        private VehicleController? vehicle;
        private ModContext? context;
        private readonly float configuredMaximumFuel = DefaultMaximumFuel;
        private ItemInstance? fedBoxAwaitingRefresh;
        private bool mounted;
        private bool stationFuelBlocked;

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
            TryFeedFromHands();
            FindOverlappingRefuelStations();
            if (nearbyRefuelStations.Count > 0)
                BlockStationFueling();
        }

        internal void NotifyDismounted()
        {
            mounted = false;
            RestoreStationFueling();
            if (fedBoxAwaitingRefresh != null)
                StartCoroutine(RefreshFedBoxAfterDismount(fedBoxAwaitingRefresh));
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
                // Keep the cargo mutation, then refresh once the normal exit path restores that UI.
                fedBoxAwaitingRefresh = heldItem;
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

        private IEnumerator RefreshFedBoxAfterDismount(ItemInstance fedBox)
        {
            yield return null;
            if (fedBoxAwaitingRefresh != fedBox)
                yield break;

            fedBoxAwaitingRefresh = null;
            if (PlayerHelper.ItemInstanceInHands != fedBox)
                yield break;

            try
            {
                PlayerHelper.OnItemInHandsCargoUpdated();
            }
            catch (System.Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle feed vehicle={vehicle?.GetInstanceID()} could not refresh " +
                    $"the fed box after dismount: {exception.GetBaseException().Message}");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var station = FindRefuelStation(other);
            if (station == null || !nearbyRefuelStations.Add(station) || !mounted)
                return;

            BlockStationFueling();
        }

        private void OnTriggerExit(Collider other)
        {
            var station = FindRefuelStation(other);
            if (station == null || !nearbyRefuelStations.Remove(station) || nearbyRefuelStations.Count != 0)
                return;

            RestoreStationFueling();
        }

        private static GasStationTrigger? FindRefuelStation(Collider other)
        {
            var station = other.GetComponent<GasStationTrigger>() ??
                          other.GetComponentInParent<GasStationTrigger>();
            return station != null && station.isRefuelStation ? station : null;
        }

        private void FindOverlappingRefuelStations()
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
                    nearbyRefuelStations.Add(station);
                }
        }

        private void BlockStationFueling()
        {
            if (vehicle?.vehicleType == null || stationFuelBlocked)
                return;

            vehicle.vehicleType.maxFuel = Mathf.Max(0f, vehicle.GetCurrentFuel());
            stationFuelBlocked = true;
            context?.Logger.Info(
                $"Moo-tor Vehicle gas-station fueling blocked vehicle={vehicle.GetInstanceID()} " +
                $"fuel={vehicle.vehicleType.maxFuel:F2}.");

            foreach (var station in nearbyRefuelStations)
                if (station != null)
                    StartCoroutine(RefreshGasStationOverlay(station));
        }

        private IEnumerator RefreshGasStationOverlay(GasStationTrigger station)
        {
            // Refresh once after the game's trigger callback has populated its overlay.
            yield return null;
            if (mounted && stationFuelBlocked && station != null && nearbyRefuelStations.Contains(station))
                GasStationOverlay.Show(station);
        }

        private void RestoreStationFueling()
        {
            if (vehicle?.vehicleType == null || !stationFuelBlocked)
                return;

            vehicle.vehicleType.maxFuel = configuredMaximumFuel;
            stationFuelBlocked = false;
        }

        private void OnDisable()
        {
            RestoreStationFueling();
            nearbyRefuelStations.Clear();
            mounted = false;
        }

        private void OnDestroy()
        {
            RestoreStationFueling();
        }
    }
}
