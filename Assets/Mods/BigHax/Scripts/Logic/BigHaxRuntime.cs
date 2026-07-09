#nullable enable
using BAModAPI;
using System.Collections;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigHax
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BigHaxRuntime : MonoBehaviour
    {
        private const float ActiveVehiclePollIntervalSeconds = 0.1f;
        private const float CasinoBetLimitPollIntervalSeconds = 0.5f;
        private const float CustomerTrafficPollIntervalSeconds = 5f;
        private const float EmployeeTrainingPollIntervalSeconds = 0.25f;
        private const float LoanLimitPollIntervalSeconds = 0.5f;

        private static BigHaxRuntime? instance;
        private readonly BigHaxBusinessCapacityService businessCapacityService = new BigHaxBusinessCapacityService();
        private readonly BigHaxCasinoBetLimitService casinoBetLimitService = new BigHaxCasinoBetLimitService();
        private readonly BigHaxBuildingCustomerCapacityService buildingCustomerCapacityService = new BigHaxBuildingCustomerCapacityService();
        private BigHaxCustomerTrafficService? customerTrafficService;
        private readonly BigHaxEmployeeTrainingService employeeTrainingService = new BigHaxEmployeeTrainingService();
        private readonly BigHaxIllegalParkingService illegalParkingService = new BigHaxIllegalParkingService();
        private readonly BigHaxInvestmentLimitService investmentLimitService = new BigHaxInvestmentLimitService();
        private readonly BigHaxItemCapacityService itemCapacityService = new BigHaxItemCapacityService();
        private readonly BigHaxLoanLimitService loanLimitService = new BigHaxLoanLimitService();
        private readonly BigHaxOverlayUi overlayUi = new BigHaxOverlayUi();
        private readonly BigHaxSleepRestDurationService sleepRestDurationService = new BigHaxSleepRestDurationService();
        private readonly BigHaxUpdateNoticeUi updateNoticeUi = new BigHaxUpdateNoticeUi();
        private readonly BigHaxVehicleCapacityService vehicleCapacityService = new BigHaxVehicleCapacityService();

        private bool applyRequested;
        private bool parkingVehicleEventsSubscribed;
        private Coroutine? parkingExitCleanupCoroutine;
        private FieldInfo? onEnterVehicleField;
        private FieldInfo? onExitVehicleField;
        private Delegate? onEnterVehicleHandler;
        private Delegate? onExitVehicleHandler;
        private float nextCasinoBetLimitPollAt;
        private ModContext? context;
        private float nextActiveVehiclePollAt;
        private float nextCustomerTrafficPollAt;
        private float nextEmployeeTrainingPollAt;
        private float nextLoanLimitPollAt;
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
            runtime.nextCasinoBetLimitPollAt = 0f;
            runtime.nextCustomerTrafficPollAt = 0f;
            runtime.nextEmployeeTrainingPollAt = 0f;
            runtime.nextLoanLimitPollAt = 0f;
            runtime.customerTrafficService = CreateCustomerTrafficService(context);
            runtime.updateNoticeUi.Initialize(context.ModId);
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
            if (parkingExitCleanupCoroutine != null)
            {
                StopCoroutine(parkingExitCleanupCoroutine);
                parkingExitCleanupCoroutine = null;
            }

            TryRestoreCustomerTraffic();
            casinoBetLimitService.RestoreOriginalLimit();
            buildingCustomerCapacityService.RestoreOriginalCapacities();
            businessCapacityService.RestoreOriginalCapacities();
            illegalParkingService.RestoreOriginalState();
            investmentLimitService.RestoreOriginalLimit();
            itemCapacityService.RestoreOriginalCapacities();
            loanLimitService.RestoreOriginalLimit();
            sleepRestDurationService.RestoreOriginalDurationsOnShutdown();
            vehicleCapacityService.RestoreOriginalCapacities();
            if (instance == this)
                instance = null;

            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            GlobalEvents.onNewHour += HandleNewHour;
            GlobalEvents.onNewDay += HandleNewDay;
            GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
            SubscribeParkingVehicleEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            GlobalEvents.onNewHour -= HandleNewHour;
            GlobalEvents.onNewDay -= HandleNewDay;
            GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
            UnsubscribeParkingVehicleEvents();
        }

        private void Update()
        {
            if (settings == null)
                return;

            BigHaxLogger.SetDebugLoggingEnabled(settings.EnableDebugLogging);
            ApplyIfRequested();
            PollUiToggleHotkey();
            PollActiveVehicleChanges();
            PollCasinoBetLimitChanges();
            PollCustomerTrafficChanges();
            PollEmployeeTrainingChanges();
            PollLoanLimitChanges();
            updateNoticeUi.ConsumeGameplayInputIfNeeded();
            overlayUi.ConsumeGameplayInputIfNeeded();
        }

        private void ApplyIfRequested()
        {
            if (!applyRequested || context == null || settings == null)
                return;

            try
            {
                SafeApply("casino bet limit", () => casinoBetLimitService.ApplyConfiguredLimit(context, settings));
                SafeApply("building customer capacities", () => buildingCustomerCapacityService.ApplyConfiguredCapacities(context, settings));
                SafeApply("business capacities", () => businessCapacityService.ApplyConfiguredCapacities(context, settings));
                SafeApply("illegal parking", () => illegalParkingService.ApplyConfiguredBehavior(context, settings));
                SafeApply("investment limit", () => investmentLimitService.ApplyConfiguredLimit(context, settings));
                SafeApply("customer traffic", () => TryApplyCustomerTraffic(context, settings, forceRefresh: true));
                SafeApply("item capacities", () => itemCapacityService.ApplyConfiguredCapacities(context, settings));
                SafeApply("loan limit", () => loanLimitService.ApplyConfiguredLimit(settings));
                SafeApply("bench rest durations", () => sleepRestDurationService.ApplyConfiguredDurations(context, settings));
                SafeApply("vehicle capacities", () => vehicleCapacityService.ApplyConfiguredCapacities(context, settings, forceRefresh: true));
            }
            finally
            {
                applyRequested = false;
            }
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

        private void PollCasinoBetLimitChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextCasinoBetLimitPollAt)
                return;

            nextCasinoBetLimitPollAt = Time.unscaledTime + CasinoBetLimitPollIntervalSeconds;
            casinoBetLimitService.ApplyConfiguredLimit(context, settings);
        }

        private void PollEmployeeTrainingChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextEmployeeTrainingPollAt)
                return;

            nextEmployeeTrainingPollAt = Time.unscaledTime + EmployeeTrainingPollIntervalSeconds;
            employeeTrainingService.Update(context, settings);
        }

        private void PollLoanLimitChanges()
        {
            if (context == null || settings == null || Time.unscaledTime < nextLoanLimitPollAt)
                return;

            nextLoanLimitPollAt = Time.unscaledTime + LoanLimitPollIntervalSeconds;
            loanLimitService.ApplyConfiguredLimit(settings);
        }

        private void PollUiToggleHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.UiHotkey))
                return;

            overlayUi.Toggle();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            overlayUi.Hide();
            updateNoticeUi.HandleSceneLoaded();
            UnsubscribeParkingVehicleEvents();
            SubscribeParkingVehicleEvents();
            TryInvalidateCustomerTrafficCache();
            casinoBetLimitService.InvalidateCache();
            employeeTrainingService.InvalidateCache();
            investmentLimitService.InvalidateCache();
            buildingCustomerCapacityService.InvalidateCache();
            businessCapacityService.InvalidateCache();
            illegalParkingService.InvalidateCache();
            itemCapacityService.InvalidateCache();
            loanLimitService.InvalidateCache();
            sleepRestDurationService.InvalidateCache();
            vehicleCapacityService.InvalidateCache();
            applyRequested = true;
        }

        private void OnGUI()
        {
            if (context == null || settings == null)
                return;

            overlayUi.OnGui(context, settings);
            updateNoticeUi.OnGui(context);
        }

        private void HandleNewHour()
        {
            if (context == null || settings == null)
                return;

            SafeApply("illegal parking onNewHour", () => illegalParkingService.HandleNewHour(context, settings));
        }

        private void HandleNewDay()
        {
            if (context == null || settings == null)
                return;

            SafeApply("illegal parking onNewDay", () => illegalParkingService.HandleNewDay(context, settings));
        }

        private void HandleVehicleVariablesChanged()
        {
            if (context == null || settings == null || !settings.DisableIllegalParkingPenalties)
                return;

            SafeApply("illegal parking onVehicleVariablesChanged", () => illegalParkingService.ApplyConfiguredBehavior(context, settings));
        }

        private void HandleVehicleEntered()
        {
            if (context == null || settings == null || !settings.DisableIllegalParkingPenalties)
                return;

            SafeApply("illegal parking onEnterVehicle", () => illegalParkingService.HandleVehicleEntered(context, settings));
        }

        private void HandleVehicleExited()
        {
            if (context == null || settings == null || !settings.DisableIllegalParkingPenalties)
                return;

            if (parkingExitCleanupCoroutine != null)
                StopCoroutine(parkingExitCleanupCoroutine);

            parkingExitCleanupCoroutine = StartCoroutine(HandleVehicleExitedDeferred());
        }

        private IEnumerator HandleVehicleExitedDeferred()
        {
            for (var frame = 0; frame < 90; frame++)
            {
                yield return null;

                if (context == null || settings == null || !settings.DisableIllegalParkingPenalties)
                    yield break;

                SafeApply("illegal parking onExitVehicle", () => illegalParkingService.HandleVehicleExited(context, settings));
            }

            parkingExitCleanupCoroutine = null;
        }

        private void SubscribeParkingVehicleEvents()
        {
            if (parkingVehicleEventsSubscribed)
                return;

            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var globalEventsType = typeof(GlobalEvents);
                onEnterVehicleField = globalEventsType.GetField("onEnterVehicle", flags);
                onExitVehicleField = globalEventsType.GetField("onExitVehicle", flags);
                onEnterVehicleHandler = SubscribeGlobalEvent(onEnterVehicleField, nameof(HandleVehicleEntered));
                onExitVehicleHandler = SubscribeGlobalEvent(onExitVehicleField, nameof(HandleVehicleExited));
                parkingVehicleEventsSubscribed = onEnterVehicleHandler != null || onExitVehicleHandler != null;
            }
            catch (Exception exception)
            {
                BigHaxFileLogger.Log("BigHax-runtime-errors.log", "BigHax-runtime-errors.log", $"[parking subscription] {exception}");
            }
        }

        private void UnsubscribeParkingVehicleEvents()
        {
            TryUnsubscribeGlobalEvent(onEnterVehicleField, onEnterVehicleHandler);
            TryUnsubscribeGlobalEvent(onExitVehicleField, onExitVehicleHandler);
            onEnterVehicleHandler = null;
            onExitVehicleHandler = null;
            onEnterVehicleField = null;
            onExitVehicleField = null;
            parkingVehicleEventsSubscribed = false;
        }

        private Delegate? SubscribeGlobalEvent(FieldInfo? eventField, string handlerMethodName)
        {
            if (eventField == null)
                return null;

            var eventType = eventField.FieldType;
            if (eventType == null)
                return null;

            var handler = CreateCompatibleDelegate(eventType, handlerMethodName);
            if (handler == null)
                return null;

            var currentValue = eventField.GetValue(null) as Delegate;
            eventField.SetValue(null, Delegate.Combine(currentValue, handler));
            return handler;
        }

        private Delegate? CreateCompatibleDelegate(Type eventType, string handlerMethodName)
        {
            var invokeMethod = eventType.GetMethod("Invoke");
            var targetMethod = GetType().GetMethod(handlerMethodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (invokeMethod == null || targetMethod == null)
                return null;

            var parameters = invokeMethod.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            var call = Expression.Call(Expression.Constant(this), targetMethod);
            Expression body = call;
            if (invokeMethod.ReturnType != typeof(void))
            {
                body = Expression.Block(call, Expression.Default(invokeMethod.ReturnType));
            }

            return Expression.Lambda(eventType, body, parameters).Compile();
        }

        private static void TryUnsubscribeGlobalEvent(FieldInfo? eventField, Delegate? handler)
        {
            if (eventField == null || handler == null)
                return;

            try
            {
                var currentValue = eventField.GetValue(null) as Delegate;
                eventField.SetValue(null, Delegate.Remove(currentValue, handler));
            }
            catch
            {
            }
        }

        private void SafeApply(string scope, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception exception)
            {
                context?.Logger.Error(exception);
                BigHaxFileLogger.Log("BigHax-runtime-errors.log", "BigHax-runtime-errors.log", $"[{scope}] {exception}");
            }
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
