#nullable enable
using BAModAPI;
using UnityEngine;

namespace ModdedVehiclesIntegration
{
    internal sealed class ModdedVehiclesIntegrationRuntime : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 2f;

        private readonly DealerCatalogIntegration catalogIntegration = new DealerCatalogIntegration();
        private ModContext? context;
        private object? currentSave;
        private float nextRefreshAt;

        internal static ModdedVehiclesIntegrationRuntime Initialize(ModContext context)
        {
            var existing = FindObjectOfType<ModdedVehiclesIntegrationRuntime>();
            if (existing != null)
            {
                existing.context = context;
                return existing;
            }

            var runtimeObject = new GameObject(nameof(ModdedVehiclesIntegrationRuntime));
            DontDestroyOnLoad(runtimeObject);
            var runtime = runtimeObject.AddComponent<ModdedVehiclesIntegrationRuntime>();
            runtime.context = context;
            return runtime;
        }

        internal void Shutdown()
        {
            catalogIntegration.RestoreExternalCatalogs(context);
            Destroy(gameObject);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshAt)
                return;

            nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
            Refresh();
        }

        private void Refresh()
        {
            var save = SaveGameManager.Current;
            if (save == null)
                return;

            if (!ReferenceEquals(currentSave, save))
            {
                currentSave = save;
                catalogIntegration.ResetTracking();
            }

            DealerLayoutIntegration.EnsureApplied(context);
            catalogIntegration.Synchronize(context);
        }
    }
}
