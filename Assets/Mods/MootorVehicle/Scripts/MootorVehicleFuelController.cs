#nullable enable
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.Items;
using Helpers;
using NWH.VehiclePhysics2.Modules.Fuel;
using NWH.VehiclePhysics2.Modules.SpeedLimiter;
using UI.Overlays;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

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
        private const float EnergyDrinkSpeedMultiplier = 2f;

        private readonly HashSet<int> suppressedRefuelStations = new();
        private VehicleController? vehicle;
        private ModContext? context;
        private readonly float configuredMaximumFuel = DefaultMaximumFuel;
        private ItemInstance? heldItemAwaitingRestore;
        private PhysicsVehicle? physicsVehicle;
        private FuelModuleWrapper? fuelModuleWrapper;
        private SpeedLimiterModuleWrapper? speedLimiterWrapper;
        private float regularSpeedLimit;
        private float regularEnginePower;
        private string energyBoostPreferenceKey = string.Empty;
        private bool performanceConfigured;
        private bool energyBoostActive;
        private bool performanceConfigurationWarningLogged;
        private bool mounted;

        internal void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
            context = modContext;
            ConfigureEnergyDrinkPerformance();

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

            ActivateEnergyDrinkBoost();

            context?.Logger.Info(
                $"Moo-tor Vehicle feed vehicle={vehicle.GetInstanceID()} item='{EnergyDrinkItemName}' " +
                $"source={(boxedEnergyDrink != null ? "box" : "hands")} " +
                $"fuelBefore={fuelBefore:F2} fuelAfter={fuelAfter:F2} " +
                $"speedMultiplier={EnergyDrinkSpeedMultiplier:F1}; energy drink consumed.");
        }

        private void ConfigureEnergyDrinkPerformance()
        {
            if (performanceConfigured || vehicle == null)
                return;

            try
            {
                physicsVehicle = vehicle.GetComponent<PhysicsVehicle>();
                fuelModuleWrapper = vehicle.GetComponent<FuelModuleWrapper>();
                speedLimiterWrapper = vehicle.GetComponent<SpeedLimiterModuleWrapper>();
                var fuelModule = fuelModuleWrapper?.module;
                var speedLimiter = speedLimiterWrapper?.module;
                var engine = physicsVehicle?.powertrain?.engine;
                if (fuelModule == null || speedLimiter == null || engine == null)
                {
                    WarnPerformanceConfigurationOnce(
                        $"could not bind fuelModule={fuelModule != null} " +
                        $"speedLimiter={speedLimiter != null} engine={engine != null}");
                    return;
                }

                regularSpeedLimit = speedLimiter.speedLimit;
                regularEnginePower = engine.maxPower;
                energyBoostPreferenceKey = BuildEnergyBoostPreferenceKey(context?.ModId, vehicle);
                fuelModule.onOutOfFuel.RemoveListener(HandleOutOfFuel);
                fuelModule.onOutOfFuel.AddListener(HandleOutOfFuel);
                performanceConfigured = true;

                var persistedBoost = !string.IsNullOrEmpty(energyBoostPreferenceKey) &&
                                     UnityEngine.PlayerPrefs.GetInt(energyBoostPreferenceKey, 0) != 0;
                if (persistedBoost && vehicle.GetCurrentFuel() > 0.01f)
                    SetEnergyDrinkBoost(true, false);
                else if (persistedBoost)
                    PersistEnergyDrinkBoost(false);

                context?.Logger.Info(
                    $"Moo-tor Vehicle energy boost configured vehicle={vehicle.GetInstanceID()} " +
                    $"regularSpeed={regularSpeedLimit:F1} regularPower={regularEnginePower:F1} " +
                    $"restored={energyBoostActive}.");
            }
            catch (System.Exception exception)
            {
                WarnPerformanceConfigurationOnce(
                    $"configuration failed: {exception.GetBaseException().Message}");
            }
        }

        private void WarnPerformanceConfigurationOnce(string reason)
        {
            if (performanceConfigurationWarningLogged)
                return;

            performanceConfigurationWarningLogged = true;
            context?.Logger.Warn(
                $"Moo-tor Vehicle energy boost vehicle={vehicle?.GetInstanceID()} {reason}.");
        }

        private void ActivateEnergyDrinkBoost()
        {
            ConfigureEnergyDrinkPerformance();
            if (!performanceConfigured)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle feed vehicle={vehicle?.GetInstanceID()} refueled, but its " +
                    "energy-drink speed boost could not be applied.");
                return;
            }

            try
            {
                SetEnergyDrinkBoost(true, true);
            }
            catch (System.Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle feed vehicle={vehicle?.GetInstanceID()} refueled, but applying " +
                    $"the energy-drink speed boost failed: {exception.GetBaseException().Message}");
            }
        }

        private void HandleOutOfFuel()
        {
            if (!energyBoostActive)
                return;

            SetEnergyDrinkBoost(false, true);
            context?.Logger.Info(
                $"Moo-tor Vehicle energy boost vehicle={vehicle?.GetInstanceID()} ended: cow is out of fuel.");
        }

        private void SetEnergyDrinkBoost(bool active, bool persist)
        {
            var speedLimiter = speedLimiterWrapper?.module;
            var engine = physicsVehicle?.powertrain?.engine;
            if (!performanceConfigured || speedLimiter == null || engine == null)
                return;

            speedLimiter.speedLimit = regularSpeedLimit * (active ? EnergyDrinkSpeedMultiplier : 1f);
            engine.maxPower = regularEnginePower * (active ? EnergyDrinkSpeedMultiplier : 1f);
            energyBoostActive = active;

            if (persist)
                PersistEnergyDrinkBoost(active);

            context?.Logger.Info(
                $"Moo-tor Vehicle energy boost vehicle={vehicle?.GetInstanceID()} active={active} " +
                $"speedLimit={speedLimiter.speedLimit:F1} enginePower={engine.maxPower:F1}.");
        }

        private void PersistEnergyDrinkBoost(bool active)
        {
            if (string.IsNullOrEmpty(energyBoostPreferenceKey))
                return;

            if (active)
                UnityEngine.PlayerPrefs.SetInt(energyBoostPreferenceKey, 1);
            else
                UnityEngine.PlayerPrefs.DeleteKey(energyBoostPreferenceKey);
            UnityEngine.PlayerPrefs.Save();
        }

        private static string BuildEnergyBoostPreferenceKey(string? modId, VehicleController controller)
        {
            var vehicleId = controller.vehicleInstance?.id;
            return string.IsNullOrWhiteSpace(vehicleId)
                ? string.Empty
                : $"m:{(string.IsNullOrWhiteSpace(modId) ? "MootorVehicle" : modId)}:energy-boost:{vehicleId}";
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

        private void OnDestroy()
        {
            fuelModuleWrapper?.module?.onOutOfFuel.RemoveListener(HandleOutOfFuel);

            if (!performanceConfigured)
                return;

            if (speedLimiterWrapper?.module != null)
                speedLimiterWrapper.module.speedLimit = regularSpeedLimit;
            if (physicsVehicle?.powertrain?.engine != null)
                physicsVehicle.powertrain.engine.maxPower = regularEnginePower;
        }
    }
}
