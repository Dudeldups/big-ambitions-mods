#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsTrafficService
    {
        private enum TrafficMode
        {
            Vanilla,
            Multiplied,
            Disabled
        }

        private const int MaximumTrafficMultiplier = 5;
        private static readonly FieldInfo? LastTrafficDensityField = typeof(TimeOfDayController).GetField(
            "_lastTrafficDensity",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ModContext context;
        private TrafficMode mode;
        private float multiplier = 1f;
        private int vanillaDensity = -1;
        private bool originalTrafficSpawning;
        private bool trafficChanged;
        private bool originalParkedCarsEnabled;
        private bool parkedCarsChanged;
        private VehiclePool? expandedPool;
        private readonly Dictionary<CarType, int> originalPoolCounts = new Dictionary<CarType, int>();

        public DeveloperToolsTrafficService(ModContext context) => this.context = context;

        public bool ParkedCarsEnabled => ParkingLaneGenerator.spawningActive;

        public void PrepareTrafficPoolCapacity()
        {
            if (TrafficManager.IsInitialized)
                return;

            var pool = TrafficComponent.Instance?.vehiclePool;
            if (pool == null || pool.trafficCars == null || ReferenceEquals(pool, expandedPool))
                return;

            RestoreTrafficPoolCounts();
            expandedPool = pool;
            foreach (var carType in pool.trafficCars)
            {
                if (carType == null || carType.nrOfVehicles <= 0)
                    continue;

                originalPoolCounts[carType] = carType.nrOfVehicles;
                carType.nrOfVehicles = checked(carType.nrOfVehicles * MaximumTrafficMultiplier);
            }

            if (originalPoolCounts.Count > 0)
            {
                context.Logger.Info(
                    $"DeveloperTools: expanded the pre-initialization NPC traffic pool to {pool.GetNumberOfVehicles()} vehicles for density testing.");
            }
        }

        public IEnumerator ReapplyTrafficAfterHourlyUpdate()
        {
            yield return null;
            if (mode != TrafficMode.Multiplied)
                yield break;

            var currentVanillaDensity = ReadVanillaDensity();
            if (currentVanillaDensity < 0)
                yield break;

            vanillaDensity = currentVanillaDensity;
            ApplyMultipliedDensity(false, out _);
        }

        public bool SetTrafficMultiplier(float requestedMultiplier, out string message)
        {
            if (!TryGetTrafficState(out var gameManager, out var capacity, out message))
                return false;

            CaptureOriginalState(gameManager);
            var currentVanillaDensity = ReadVanillaDensity();
            if (mode == TrafficMode.Vanilla && currentVanillaDensity >= 0)
                vanillaDensity = currentVanillaDensity;
            if (vanillaDensity < 0)
                vanillaDensity = Math.Min(40, capacity);

            multiplier = Mathf.Max(1f, requestedMultiplier);
            mode = multiplier <= 1f ? TrafficMode.Vanilla : TrafficMode.Multiplied;
            gameManager.spawnTraffic = true;

            if (mode == TrafficMode.Vanilla)
            {
                var target = Mathf.Clamp(vanillaDensity, 0, capacity);
                Manager.SetTrafficDensity(target);
                message = $"Restored vanilla AI vehicle traffic ({target} vehicles).";
            }
            else
            {
                ApplyMultipliedDensity(true, out message);
            }

            context.Logger.Info("DeveloperTools: " + message);
            return true;
        }

        public bool DisableTraffic(out string message)
        {
            if (!TryGetTrafficState(out var gameManager, out _, out message))
                return false;

            CaptureOriginalState(gameManager);
            var currentVanillaDensity = ReadVanillaDensity();
            if (mode == TrafficMode.Vanilla && currentVanillaDensity >= 0)
                vanillaDensity = currentVanillaDensity;

            mode = TrafficMode.Disabled;
            multiplier = 1f;
            gameManager.spawnTraffic = false;
            Manager.SetTrafficDensity(0);
            Manager.ClearTraffic();
            message = "Disabled AI vehicle traffic and cleared active traffic vehicles.";
            context.Logger.Info("DeveloperTools: " + message);
            return true;
        }

        public bool ToggleParkedCars(out string message)
        {
            if (!parkedCarsChanged)
                originalParkedCarsEnabled = ParkingLaneGenerator.spawningActive;

            var enable = !ParkingLaneGenerator.spawningActive;
            ApplyParkedCars(enable, out var laneCount);
            parkedCarsChanged = true;
            message = enable
                ? $"Enabled parked cars and refreshed {laneCount} parking lanes."
                : $"Disabled parked cars and cleared {laneCount} parking lanes.";
            context.Logger.Info("DeveloperTools: " + message);
            return true;
        }

        public void Shutdown()
        {
            if (trafficChanged && TryGetTrafficState(out var gameManager, out var capacity, out _))
            {
                gameManager.spawnTraffic = originalTrafficSpawning;
                if (originalTrafficSpawning)
                    Manager.SetTrafficDensity(Mathf.Clamp(vanillaDensity, 0, capacity));
                else
                {
                    Manager.SetTrafficDensity(0);
                    Manager.ClearTraffic();
                }
            }

            if (parkedCarsChanged && ParkingLaneGenerator.spawningActive != originalParkedCarsEnabled)
                ApplyParkedCars(originalParkedCarsEnabled, out _);

            RestoreTrafficPoolCounts();
        }

        private bool ApplyMultipliedDensity(bool describeCapacityLimit, out string message)
        {
            if (!TryGetTrafficState(out var gameManager, out var capacity, out message))
                return false;

            gameManager.spawnTraffic = true;
            var requested = Mathf.Max(0, Mathf.RoundToInt(vanillaDensity * multiplier));
            var target = Math.Min(requested, capacity);
            Manager.SetTrafficDensity(target);
            message = $"Set AI vehicle traffic to {multiplier:0.#}x ({target} vehicles).";
            if (describeCapacityLimit && target < requested)
                message += $" The traffic pool capped the requested {requested} at {capacity}.";
            return true;
        }

        private void CaptureOriginalState(GameManager gameManager)
        {
            if (trafficChanged)
                return;

            originalTrafficSpawning = gameManager.spawnTraffic;
            vanillaDensity = ReadVanillaDensity();
            trafficChanged = true;
        }

        private static int ReadVanillaDensity()
        {
            try
            {
                var timeOfDay = InstanceBehavior<GameManager>.Instance?.timeOfDayController;
                return timeOfDay != null && LastTrafficDensityField?.GetValue(timeOfDay) is int density
                    ? density
                    : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static bool TryGetTrafficState(out GameManager gameManager, out int capacity, out string message)
        {
            gameManager = InstanceBehavior<GameManager>.Instance;
            var cityManager = InstanceBehavior<CityManager>.Instance;
            var trafficComponent = cityManager?.trafficComponent;
            capacity = trafficComponent?.vehiclePool?.GetNumberOfVehicles() ?? 0;
            if (gameManager == null || trafficComponent == null || capacity <= 0 || !TrafficManager.IsInitialized)
            {
                message = "AI vehicle traffic is unavailable in the current scene.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static void ApplyParkedCars(bool enable, out int laneCount)
        {
            ParkingLaneGenerator.spawningActive = enable;
            var lanes = UnityEngine.Object.FindObjectsByType<ParkingLaneGenerator>(FindObjectsSortMode.None);
            laneCount = lanes.Length;
            foreach (var lane in lanes)
            {
                if (lane == null)
                    continue;
                if (enable)
                    lane.Init();
                else
                    lane.CleanupParkedVehicles(true);
            }
        }

        private void RestoreTrafficPoolCounts()
        {
            foreach (var entry in originalPoolCounts)
            {
                if (entry.Key != null)
                    entry.Key.nrOfVehicles = entry.Value;
            }

            originalPoolCounts.Clear();
            expandedPool = null;
        }
    }
}
