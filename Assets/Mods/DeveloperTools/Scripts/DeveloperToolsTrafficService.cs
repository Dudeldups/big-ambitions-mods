#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using Localizor;
using UnityEngine;
using Vehicles.VehicleTypes;

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
        private static readonly FieldInfo? TrafficVehiclesField = typeof(TrafficManager).GetField(
            "trafficVehicles",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? GetVehicleListMethod = TrafficVehiclesField?.FieldType.GetMethod(
            "GetVehicleList",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
        private readonly List<CatalogEntry> moddedAiModels = new List<CatalogEntry>();
        private readonly HashSet<string> moddedAiPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<VehicleComponent, VehicleGroupType> originalVehicleGroups =
            new Dictionary<VehicleComponent, VehicleGroupType>();
        private string favoredPrefabName = string.Empty;
        private bool moddedOnly;

        public DeveloperToolsTrafficService(ModContext context) => this.context = context;

        public bool ParkedCarsEnabled => ParkingLaneGenerator.spawningActive;
        public bool ModdedOnly => moddedOnly;
        public string FavoredPrefabName => favoredPrefabName;
        public IReadOnlyList<CatalogEntry> ModdedAiModels => moddedAiModels;

        internal static string Text(string key, string fallback)
        {
            try
            {
                var localized = key.GetLocalization()?.ToString();
                return string.IsNullOrWhiteSpace(localized) ? fallback : localized!;
            }
            catch
            {
                return fallback;
            }
        }

        public void RefreshAvailableModels()
        {
            moddedAiModels.Clear();
            moddedAiPrefabNames.Clear();
            var pool = TrafficComponent.Instance?.vehiclePool;
            if (pool?.trafficCars == null)
                return;

            var moddedNames = VehicleTypeHelper.GetVehicleTypeNames()
                .Where(id => VehicleTypeHelper.IsModVehicleType(id) ||
                             (id.Contains(':') && !id.StartsWith("ba:", StringComparison.OrdinalIgnoreCase)))
                .Select(id => NormalizeName(id.Substring(id.LastIndexOf(':') + 1)
                    .Replace("vehicletype_", string.Empty)))
                .Where(name => name.Length > 0)
                .ToArray();
            foreach (var carType in pool.trafficCars)
            {
                if (carType?.vehiclePrefab == null || !carType.canBeAiDriven)
                    continue;
                var prefabName = carType.vehiclePrefab.name;
                var normalized = NormalizeName(prefabName);
                if (!moddedNames.Any(name => normalized.StartsWith(name, StringComparison.Ordinal)) ||
                    !moddedAiPrefabNames.Add(prefabName))
                    continue;
                moddedAiModels.Add(new CatalogEntry(prefabName, carType.name));
            }
            moddedAiModels.Sort((left, right) =>
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        public bool SetModdedOnly(bool enabled, out string message)
        {
            if (enabled && moddedAiModels.Count == 0)
                RefreshAvailableModels();
            if (enabled && moddedAiModels.Count == 0)
            {
                message = Text("developer_tools_ai_no_models", "No modded AI-capable vehicles are registered in the traffic pool.");
                return false;
            }
            var previousModdedOnly = moddedOnly;
            var previousFavored = favoredPrefabName;
            moddedOnly = enabled;
            if (!enabled)
                favoredPrefabName = string.Empty;
            if (ApplyVehicleFilter(out message))
                return true;
            moddedOnly = previousModdedOnly;
            favoredPrefabName = previousFavored;
            return false;
        }

        public bool FavorModel(string prefabName, out string message)
        {
            if (moddedAiModels.All(entry => entry.Id != prefabName))
            {
                message = Text("developer_tools_ai_model_unavailable", "That modded AI vehicle is no longer available in the traffic pool.");
                return false;
            }
            var previousFavored = favoredPrefabName;
            var previousModdedOnly = moddedOnly;
            favoredPrefabName = prefabName;
            moddedOnly = true;
            if (ApplyVehicleFilter(out message))
                return true;
            favoredPrefabName = previousFavored;
            moddedOnly = previousModdedOnly;
            return false;
        }

        public bool StopFavoringModel(out string message)
        {
            var previousFavored = favoredPrefabName;
            favoredPrefabName = string.Empty;
            if (ApplyVehicleFilter(out message))
                return true;
            favoredPrefabName = previousFavored;
            return false;
        }

        private bool ApplyVehicleFilter(out string message)
        {
            if (!TrafficManager.IsInitialized)
            {
                message = Text("developer_tools_ai_unavailable", "AI vehicle traffic is unavailable in the current scene.");
                return false;
            }
            List<VehicleComponent>? vehicles;
            try
            {
                var pooledVehicles = TrafficVehiclesField?.GetValue(TrafficManager.Instance);
                vehicles = pooledVehicles == null
                    ? null
                    : GetVehicleListMethod?.Invoke(pooledVehicles, null) as List<VehicleComponent>;
            }
            catch (Exception exception)
            {
                message = Text("developer_tools_ai_lookup_failed", "Could not inspect the AI vehicle pool.");
                context.Logger.Warn("DeveloperTools: AI traffic pool lookup failed: " +
                                    exception.GetBaseException().Message);
                return false;
            }
            if (vehicles == null)
            {
                message = Text("developer_tools_ai_not_ready", "The AI vehicle pool is not ready.");
                return false;
            }
            var allowed = 0;
            foreach (var vehicle in vehicles)
            {
                if (vehicle == null)
                    continue;
                if (!originalVehicleGroups.ContainsKey(vehicle))
                    originalVehicleGroups[vehicle] = vehicle.vehicleGroupType;
                var prefabName = vehicle.prefab != null ? vehicle.prefab.name : string.Empty;
                var selected = !moddedOnly ||
                               (moddedAiPrefabNames.Contains(prefabName) &&
                                (favoredPrefabName.Length == 0 ||
                                 string.Equals(prefabName, favoredPrefabName, StringComparison.OrdinalIgnoreCase)));
                vehicle.vehicleGroupType = selected && moddedOnly
                    ? VehicleGroupType.Personal
                    : selected ? originalVehicleGroups[vehicle] : (VehicleGroupType)(-1);
                if (selected)
                    allowed++;
            }
            if (allowed == 0)
            {
                RestoreVehicleGroups();
                message = Text("developer_tools_ai_no_match", "No matching AI vehicles were found in the initialized traffic pool.");
                context.Logger.Warn("DeveloperTools: AI traffic filter was not applied; no pooled vehicles matched.");
                return false;
            }
            // The game's traffic manager owns the active vehicles. Recycle them
            // through its API so the new filter takes effect on the next spawn.
            try
            {
                Manager.ClearTraffic();
            }
            catch (Exception exception)
            {
                RestoreVehicleGroups();
                message = Text("developer_tools_ai_recycle_failed", "Could not recycle current AI traffic.");
                context.Logger.Warn("DeveloperTools: AI traffic recycle failed: " +
                                    exception.GetBaseException().Message);
                return false;
            }
            message = favoredPrefabName.Length > 0
                ? string.Format(Text("developer_tools_ai_favored", "Prioritizing {0} in AI traffic ({1} pooled vehicles)."), favoredPrefabName, allowed)
                : moddedOnly
                    ? string.Format(Text("developer_tools_ai_modded_only", "Only modded AI cars will spawn ({0} pooled vehicles)."), allowed)
                    : Text("developer_tools_ai_restored", "Restored all AI vehicle types.");
            if (DeveloperToolsDiagnostics.Traffic)
                context.Logger.Info("DeveloperTools: " + message);
            return true;
        }

        private void RestoreVehicleGroups()
        {
            foreach (var entry in originalVehicleGroups)
                if (entry.Key != null)
                    entry.Key.vehicleGroupType = entry.Value;
            originalVehicleGroups.Clear();
        }

        private static string NormalizeName(string value) =>
            new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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

            if (originalPoolCounts.Count > 0 && DeveloperToolsDiagnostics.Enabled)
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

            if (DeveloperToolsDiagnostics.Enabled)
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
            if (DeveloperToolsDiagnostics.Enabled)
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
            if (DeveloperToolsDiagnostics.Enabled)
                context.Logger.Info("DeveloperTools: " + message);
            return true;
        }

        public void Shutdown()
        {
            RestoreVehicleGroups();
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

        public void HandleSceneChanged()
        {
            originalVehicleGroups.Clear();
            moddedAiModels.Clear();
            moddedAiPrefabNames.Clear();
            moddedOnly = false;
            favoredPrefabName = string.Empty;
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
