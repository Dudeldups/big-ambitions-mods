#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace ModdedVehiclesIntegration
{
    internal sealed class ModdedVehiclesIntegrationRuntime : MonoBehaviour
    {
        private readonly DealerCatalogIntegration catalogIntegration = new DealerCatalogIntegration();
        private readonly DealerDeskInteractionIntegration deskInteractionIntegration =
            new DealerDeskInteractionIntegration();
        private ModContext? context;
        private object? currentSave;
        private bool wasEnteringBuilding;

        internal static ModdedVehiclesIntegrationRuntime Initialize(ModContext context)
        {
            var existing = FindObjectOfType<ModdedVehiclesIntegrationRuntime>();
            if (existing != null)
            {
                existing.context = context;
                existing.deskInteractionIntegration.SetCatalogSynchronizer(
                    existing.SynchronizeCatalogForInteraction);
                existing.SubscribeGlobalEvents();
                return existing;
            }

            var runtimeObject = new GameObject(nameof(ModdedVehiclesIntegrationRuntime));
            DontDestroyOnLoad(runtimeObject);
            var runtime = runtimeObject.AddComponent<ModdedVehiclesIntegrationRuntime>();
            runtime.context = context;
            runtime.deskInteractionIntegration.SetCatalogSynchronizer(runtime.SynchronizeCatalogForInteraction);
            runtime.SubscribeGlobalEvents();
            return runtime;
        }

        internal void Shutdown()
        {
            UnsubscribeGlobalEvents();
            deskInteractionIntegration.Shutdown();
            catalogIntegration.RestoreExternalCatalogs();
            DealerServiceIntegration.Restore();
            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SubscribeGlobalEvents();
        }

        private void OnDisable()
        {
            UnsubscribeGlobalEvents();
        }

        private void Update()
        {
            RefreshLayoutCacheOnBuildingEntry();
            deskInteractionIntegration.Update(context);

            var save = SaveGameManager.Current;
            if (ReferenceEquals(currentSave, save))
                return;

            currentSave = save;
            catalogIntegration.ResetTracking();
            if (save != null)
                RefreshAll();
        }

        private void RefreshLayoutCacheOnBuildingEntry()
        {
            var enteringBuilding = InstanceBehavior<BuildingManager>.Instance?.enteringBuilding == true;
            if (enteringBuilding && !wasEnteringBuilding)
            {
                DealerLayoutIntegration.EnsureApplied(context);
                DealerServiceIntegration.EnsureApplied(context);
            }

            wasEnteringBuilding = enteringBuilding;
        }

        private void SubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onEnterVehicle += HandleVehicleEntered;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggled;
            GlobalEvents.onFullMenuToggle += HandleFullMenuToggled;
        }

        private void UnsubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggled;
        }

        private void HandleVehicleEntered(VehicleController vehicleController)
        {
            EnsureModdedVehicleSleepEnvironment(vehicleController, "enter-vehicle");
        }

        private void HandleFullMenuToggled(bool isOpen)
        {
            if (!isOpen)
                return;

            var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
            if (selectedVehicle != null)
                EnsureModdedVehicleSleepEnvironment(selectedVehicle, "full-menu");
        }

        private void EnsureModdedVehicleSleepEnvironment(
            VehicleController vehicleController,
            string source)
        {
            if (!IsModdedMotorVehicle(vehicleController))
                return;

            try
            {
                var environmentField = typeof(VehicleController).GetField(
                    "sleepEnvironment",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var environment = environmentField?.GetValue(vehicleController);
                if (environmentField == null || environment == null)
                {
                    context?.Logger.Warn(
                        $"Modded Vehicles Integration: vehicle '{GetVehicleTypeName(vehicleController)}' has no sleep environment source='{source}'.");
                    return;
                }

                var configField = FindField(environment.GetType(), "config");
                if (configField == null)
                {
                    context?.Logger.Warn(
                        $"Modded Vehicles Integration: vehicle '{GetVehicleTypeName(vehicleController)}' has an unsupported sleep environment source='{source}'.");
                    return;
                }

                if (configField.GetValue(environment) is UnityEngine.Object currentConfig &&
                    currentConfig != null &&
                    IsCarSleepConfig(currentConfig))
                {
                    return;
                }

                var carConfig = CreateFallbackCarSleepConfig(configField.FieldType);
                if (carConfig == null)
                {
                    context?.Logger.Warn(
                        $"Modded Vehicles Integration: could not create the native Car sleep configuration for '{GetVehicleTypeName(vehicleController)}' source='{source}'.");
                    return;
                }

                configField.SetValue(environment, carConfig);
                environmentField.SetValue(vehicleController, environment);
                context?.Logger.Info(
                    $"Modded Vehicles Integration: configured native Car sleep for '{GetVehicleTypeName(vehicleController)}' source='{source}'.");
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Modded Vehicles Integration: could not configure car sleep for " +
                    $"'{GetVehicleTypeName(vehicleController)}' source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        private static bool IsModdedMotorVehicle(VehicleController vehicleController)
        {
            var vehicleTypeName = GetVehicleTypeName(vehicleController);
            return !string.IsNullOrWhiteSpace(vehicleTypeName) &&
                   !vehicleTypeName.StartsWith("ba:", StringComparison.OrdinalIgnoreCase) &&
                   vehicleController.vehicleType != null &&
                   vehicleController.vehicleType.maxFuel > 0f;
        }

        private static string GetVehicleTypeName(VehicleController vehicleController)
        {
            return vehicleController.vehicleInstance?.vehicleTypeName ??
                   vehicleController.vehicleType?.vehicleTypeName ??
                   "unknown";
        }

        private static bool IsCarSleepConfig(UnityEngine.Object candidate)
        {
            var typeField = FindField(candidate.GetType(), "sleepEnvironmentType");
            var typeValue = typeField?.GetValue(candidate);
            return typeValue != null && Convert.ToInt32(typeValue) == 1;
        }

        private static UnityEngine.Object? CreateFallbackCarSleepConfig(Type configType)
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(configType))
                return null;

            var config = ScriptableObject.CreateInstance(configType);
            config.name = "Modded Vehicles Integration Runtime Car Sleep Config";
            config.hideFlags = HideFlags.HideAndDontSave;
            SetEnumField(config, "sleepEnvironmentType", 1);
            SetEnumField(config, "energyRegen", 3);

            var balanceConfigField = FindField(configType, "balanceConfig");
            if (balanceConfigField == null ||
                !typeof(ScriptableObject).IsAssignableFrom(balanceConfigField.FieldType))
            {
                Destroy(config);
                return null;
            }

            var balance = ScriptableObject.CreateInstance(balanceConfigField.FieldType);
            balance.name = "Modded Vehicles Integration Runtime Car Sleep Balance";
            balance.hideFlags = HideFlags.HideAndDontSave;
            SetStringField(balance, "displayName", "Car");
            SetEnumField(balance, "source", 0);
            SetIntField(balance, "defaultDurationMinutes", 480);
            SetIntField(balance, "minDurationMinutes", 60);
            SetIntField(balance, "maxDurationMinutes", 1440);
            balanceConfigField.SetValue(config, balance);

            var luxuryBalanceField = FindField(configType, "luxuryOverrideBalanceConfig");
            luxuryBalanceField?.SetValue(config, balance);
            return config;
        }

        private static FieldInfo? FindField(Type type, string fieldName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static void SetEnumField(object target, string fieldName, int value)
        {
            var field = FindField(target.GetType(), fieldName);
            if (field?.FieldType.IsEnum == true)
                field.SetValue(target, Enum.ToObject(field.FieldType, value));
        }

        private static void SetIntField(object target, string fieldName, int value)
        {
            var field = FindField(target.GetType(), fieldName);
            if (field?.FieldType == typeof(int))
                field.SetValue(target, value);
        }

        private static void SetStringField(object target, string fieldName, string value)
        {
            var field = FindField(target.GetType(), fieldName);
            if (field?.FieldType == typeof(string))
                field.SetValue(target, value);
        }

        private void SynchronizeCatalogForInteraction()
        {
            catalogIntegration.Synchronize();
        }

        private void RefreshAll()
        {
            var save = SaveGameManager.Current;
            if (save == null)
                return;

            DealerLayoutIntegration.EnsureApplied(context);
            DealerServiceIntegration.EnsureApplied(context);
            catalogIntegration.Synchronize();
        }
    }
}
