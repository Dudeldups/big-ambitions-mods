#nullable enable
using System;
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

        internal static ModdedVehiclesIntegrationRuntime Initialize(ModContext context)
        {
            var existing = FindObjectOfType<ModdedVehiclesIntegrationRuntime>();
            if (existing != null)
            {
                existing.context = context;
                existing.deskInteractionIntegration.SetCatalogSynchronizer(
                    existing.SynchronizeCatalogForInteraction);
                existing.SubscribeToEvents();
                return existing;
            }

            var runtimeObject = new GameObject(nameof(ModdedVehiclesIntegrationRuntime));
            DontDestroyOnLoad(runtimeObject);
            var runtime = runtimeObject.AddComponent<ModdedVehiclesIntegrationRuntime>();
            runtime.context = context;
            runtime.deskInteractionIntegration.SetCatalogSynchronizer(runtime.SynchronizeCatalogForInteraction);
            runtime.SubscribeToEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
            return runtime;
        }

        internal void Shutdown()
        {
            UnsubscribeFromEvents();
            deskInteractionIntegration.Shutdown();
            catalogIntegration.RestoreExternalCatalogs(context);
            DealerServiceIntegration.Restore(context);
            Destroy(gameObject);
        }

        private void Update()
        {
            deskInteractionIntegration.Update(context);

            var save = SaveGameManager.Current;
            if (ReferenceEquals(currentSave, save))
                return;

            currentSave = save;
            catalogIntegration.ResetTracking();
            if (save != null)
                RefreshAll("save became available");
        }

        private void SubscribeToEvents()
        {
            GlobalEvents.onEnterBuildingDelayed -= HandleEnterBuildingDelayed;
            GlobalEvents.onEnterBuildingDelayed += HandleEnterBuildingDelayed;
        }

        private void UnsubscribeFromEvents()
        {
            GlobalEvents.onEnterBuildingDelayed -= HandleEnterBuildingDelayed;
        }

        private void HandleGameLoadedLate()
        {
            var save = SaveGameManager.Current;
            if (!ReferenceEquals(currentSave, save))
            {
                currentSave = save;
                catalogIntegration.ResetTracking();
            }

            RefreshAll("game loaded");
        }

        private void HandleEnterBuildingDelayed(Address _)
        {
            var dealerContactId = InstanceBehavior<BuildingManager>.Instance?.buildingRegistration?.BusinessName;
            if (DealerDeskInteractionIntegration.IsInteractiveDealerContactId(dealerContactId))
                catalogIntegration.Synchronize(context);
        }

        private void SynchronizeCatalogForInteraction()
        {
            catalogIntegration.Synchronize(context);
        }

        private void RefreshAll(string source)
        {
            var save = SaveGameManager.Current;
            if (save == null)
                return;

            DealerLayoutIntegration.EnsureApplied(context);
            DealerServiceIntegration.EnsureApplied(context);
            catalogIntegration.Synchronize(context);
            context?.Logger.Info(
                $"Modded Vehicles Integration: event-driven integration refresh completed ({source}).");
        }
    }
}
