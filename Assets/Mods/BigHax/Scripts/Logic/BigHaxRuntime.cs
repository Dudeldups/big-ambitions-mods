#nullable enable
using BAModAPI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigHax
{
    public sealed class BigHaxRuntime : MonoBehaviour
    {
        private const float ActiveVehiclePollIntervalSeconds = 0.1f;

        private static BigHaxRuntime? instance;
        private readonly BigHaxItemCapacityService itemCapacityService = new BigHaxItemCapacityService();
        private readonly BigHaxVehicleCapacityService vehicleCapacityService = new BigHaxVehicleCapacityService();

        private bool applyRequested;
        private ModContext? context;
        private float nextActiveVehiclePollAt;
        private BigHaxSettings? settings;

        public static BigHaxRuntime Initialize(ModContext context, BigHaxSettings settings)
        {
            var runtime = FindObjectOfType<BigHaxRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(BigHaxRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<BigHaxRuntime>();
            }

            runtime.context = context;
            runtime.settings = settings;
            runtime.applyRequested = true;
            runtime.nextActiveVehiclePollAt = 0f;
            BigHaxLogger.SetDebugLoggingEnabled(settings.EnableDebugLogging);
            instance = runtime;
            BigHaxLogger.Info(context, $"BigHax: file log path = {BigHaxFileLogger.LogPath}");
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

            BigHaxLogger.SetDebugLoggingEnabled(settings.EnableDebugLogging);
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
