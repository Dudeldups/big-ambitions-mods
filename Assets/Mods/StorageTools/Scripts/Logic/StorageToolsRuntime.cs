#nullable enable
using BAModAPI;
using UnityEngine;

namespace StorageTools
{
    public sealed class StorageToolsRuntime : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.5f;

        private static StorageToolsRuntime? instance;
        private readonly StorageToolsItemCapacityService itemCapacityService = new StorageToolsItemCapacityService();
        private readonly StorageToolsVehicleCapacityService vehicleCapacityService = new StorageToolsVehicleCapacityService();

        private ModContext? context;
        private StorageToolsSettings? settings;
        private float nextRefreshAt;

        public static StorageToolsRuntime Initialize(ModContext context, StorageToolsSettings settings)
        {
            var runtime = FindObjectOfType<StorageToolsRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(StorageToolsRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<StorageToolsRuntime>();
            }

            runtime.context = context;
            runtime.settings = settings;
            runtime.nextRefreshAt = 0f;
            instance = runtime;
            runtime.ApplyNow();
            return runtime;
        }

        public static void RequestImmediateApply()
        {
            if (instance != null)
                instance.nextRefreshAt = 0f;
        }

        public void Shutdown()
        {
            itemCapacityService.RestoreOriginalCapacities();
            vehicleCapacityService.RestoreOriginalCapacities();
            if (instance == this)
                instance = null;

            Destroy(gameObject);
        }

        private void Update()
        {
            if (settings == null || Time.unscaledTime < nextRefreshAt)
                return;

            ApplyNow();
        }

        private void ApplyNow()
        {
            if (context == null || settings == null)
                return;

            itemCapacityService.ApplyConfiguredCapacities(context, settings);
            vehicleCapacityService.ApplyConfiguredCapacities(context, settings);
            nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        }
    }
}
