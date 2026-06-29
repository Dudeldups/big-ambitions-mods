#nullable enable
using BAModAPI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigHax
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BigHaxRuntime : MonoBehaviour
    {
        private const float ActiveVehiclePollIntervalSeconds = 0.1f;
        private const float CustomerTrafficPollIntervalSeconds = 5f;
        private const float EmployeeTrainingPollIntervalSeconds = 0.25f;

        private static BigHaxRuntime? instance;
        private readonly BigHaxBusinessCapacityService businessCapacityService = new BigHaxBusinessCapacityService();
        private readonly BigHaxBuildingCustomerCapacityService buildingCustomerCapacityService = new BigHaxBuildingCustomerCapacityService();
        private BigHaxCustomerTrafficService? customerTrafficService;
        private readonly BigHaxEmployeeTrainingService employeeTrainingService = new BigHaxEmployeeTrainingService();
        private readonly BigHaxInvestmentLimitService investmentLimitService = new BigHaxInvestmentLimitService();
        private readonly BigHaxItemCapacityService itemCapacityService = new BigHaxItemCapacityService();
        private readonly BigHaxOverlayUi overlayUi = new BigHaxOverlayUi();
        private readonly BigHaxVehicleCapacityService vehicleCapacityService = new BigHaxVehicleCapacityService();

        private bool applyRequested;
        private ModContext? context;
        private float nextActiveVehiclePollAt;
        private float nextCustomerTrafficPollAt;
        private float nextEmployeeTrainingPollAt;
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
            runtime.nextCustomerTrafficPollAt = 0f;
            runtime.nextEmployeeTrainingPollAt = 0f;
            runtime.customerTrafficService = CreateCustomerTrafficService(context);
            BigHaxLogger.SetDebugLoggingEnabled(settings.EnableDebugLogging);
            instance = runtime;
            BigHaxFileLogger.Log(
                "bighax-investment-debug.log",
                "bighax-investment-debug.log",
                $"[Runtime] Initialize. ModRootPath={context.ModRootPath}, MainLogPath={BigHaxFileLogger.LogPath}");
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
            TryRestoreCustomerTraffic();
            buildingCustomerCapacityService.RestoreOriginalCapacities();
            businessCapacityService.RestoreOriginalCapacities();
            investmentLimitService.RestoreOriginalLimit();
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
            PollUiToggleHotkey();
            PollActiveVehicleChanges();
            PollCustomerTrafficChanges();
            PollEmployeeTrainingChanges();
            overlayUi.ConsumeGameplayInputIfNeeded();
        }

        private void ApplyIfRequested()
        {
            if (!applyRequested || context == null || settings == null)
                return;

            buildingCustomerCapacityService.ApplyConfiguredCapacities(context, settings);
            businessCapacityService.ApplyConfiguredCapacities(context, settings);
            investmentLimitService.ApplyConfiguredLimit(context, settings);
            TryApplyCustomerTraffic(context, settings, forceRefresh: true);
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

        private void PollCustomerTrafficChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextCustomerTrafficPollAt)
                return;

            nextCustomerTrafficPollAt = Time.unscaledTime + CustomerTrafficPollIntervalSeconds;
            TryApplyCustomerTraffic(context, settings, forceRefresh: false);
        }

        private void PollEmployeeTrainingChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextEmployeeTrainingPollAt)
                return;

            nextEmployeeTrainingPollAt = Time.unscaledTime + EmployeeTrainingPollIntervalSeconds;
            employeeTrainingService.Update(context, settings);
        }

        private void PollUiToggleHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.UiHotkey))
                return;

            overlayUi.Toggle();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryInvalidateCustomerTrafficCache();
            employeeTrainingService.InvalidateCache();
            investmentLimitService.InvalidateCache();
            buildingCustomerCapacityService.InvalidateCache();
            businessCapacityService.InvalidateCache();
            itemCapacityService.InvalidateCache();
            vehicleCapacityService.InvalidateCache();
            applyRequested = true;
        }

        private void OnGUI()
        {
            if (context == null || settings == null)
                return;

            overlayUi.OnGui(context, settings);
        }

        private static BigHaxCustomerTrafficService? CreateCustomerTrafficService(ModContext context)
        {
            try
            {
                return new BigHaxCustomerTrafficService();
            }
            catch (System.Exception exception)
            {
                context.Logger.Error(exception);
                return null;
            }
        }

        private void TryApplyCustomerTraffic(ModContext context, BigHaxSettings settings, bool forceRefresh)
        {
            if (customerTrafficService == null)
                return;

            try
            {
                customerTrafficService.ApplyConfiguredTraffic(context, settings, forceRefresh);
            }
            catch (System.Exception exception)
            {
                context.Logger.Error(exception);
                customerTrafficService = null;
            }
        }

        private void TryRestoreCustomerTraffic()
        {
            if (customerTrafficService == null)
                return;

            try
            {
                customerTrafficService.RestoreOriginalState(context);
            }
            catch (System.Exception exception)
            {
                context?.Logger.Error(exception);
            }
        }

        private void TryInvalidateCustomerTrafficCache()
        {
            if (customerTrafficService == null)
                return;

            try
            {
                customerTrafficService.InvalidateCache();
            }
            catch (System.Exception exception)
            {
                context?.Logger.Error(exception);
                customerTrafficService = null;
            }
        }
    }
}
