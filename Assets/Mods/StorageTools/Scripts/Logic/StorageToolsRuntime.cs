#nullable enable
using BAModAPI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StorageTools
{
    public sealed class StorageToolsRuntime : MonoBehaviour
    {
        private const float ActiveVehiclePollIntervalSeconds = 0.1f;

        private static StorageToolsRuntime? instance;
        private readonly StorageToolsItemCapacityService itemCapacityService = new StorageToolsItemCapacityService();
        private readonly StorageToolsVehicleCapacityService vehicleCapacityService = new StorageToolsVehicleCapacityService();

        private bool applyRequested;
        private ModContext? context;
        private float nextActiveVehiclePollAt;
        private StorageToolsSettings? settings;

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
            runtime.applyRequested = true;
            runtime.nextActiveVehiclePollAt = 0f;
            StorageToolsLogger.SetDebugLoggingEnabled(settings.EnableDebugLogging);
            instance = runtime;
            StorageToolsLogger.Info(context, $"StorageTools: file log path = {StorageToolsFileLogger.LogPath}");
            runtime.ApplyIfRequested();
            return runtime;
        }

        public static void RequestImmediateApply()
        {
            if (instance != null)
                instance.applyRequested = true;
        }

        public void Shutdown()
        {
            itemCapacityService.RestoreOriginalCapacities();
            vehicleCapacityService.RestoreOriginalCapacities();
            if (instance == this)
                instance = null;

            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Update()
        {
            if (settings == null)
                return;

            StorageToolsLogger.SetDebugLoggingEnabled(settings.EnableDebugLogging);
            ApplyIfRequested();
            PollActiveVehicleChanges();
        }

        private void ApplyIfRequested()
        {
            if (!applyRequested || context == null || settings == null)
                return;

            itemCapacityService.ApplyConfiguredCapacities(context, settings);
            vehicleCapacityService.ApplyConfiguredCapacities(context, settings, forceRefresh: true);
            applyRequested = false;
        }

        private void PollActiveVehicleChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextActiveVehiclePollAt)
                return;

            nextActiveVehiclePollAt = Time.unscaledTime + ActiveVehiclePollIntervalSeconds;
            vehicleCapacityService.ApplyConfiguredCapacities(context, settings, forceRefresh: false);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            itemCapacityService.InvalidateCache();
            vehicleCapacityService.InvalidateCache();
            applyRequested = true;
        }
    }
}
