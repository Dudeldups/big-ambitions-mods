#nullable enable
using System;
using System.Collections;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MootorVehicle
{
    public sealed class MootorVehicleRuntime : MonoBehaviour
    {
        private const int InitializationRetryCount = 24;
        private const int RequiredStablePasses = 5;
        private const float InitializationRetryDelay = 0.25f;
        private const string VeterinarianAssemblyName = "MobileVeterinarian";
        private const string MissingVeterinarianNotificationKey =
            "mootorvehicle:missing_mobile_veterinarian";
        private const string MissingVeterinarianNotificationId =
            "MootorVehicleMissingMobileVeterinarian";

        private Coroutine? initializationCoroutine;
        private ModContext? context;
        private string vehicleTypeName = string.Empty;
        private bool missingVeterinarianNoticeShown;

        public static MootorVehicleRuntime Initialize(ModContext context, string vehicleTypeName)
        {
            var runtime = FindObjectOfType<MootorVehicleRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(MootorVehicleRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<MootorVehicleRuntime>();
            }

            runtime.context = context;
            runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
            runtime.SubscribeGlobalEvents();
            GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
            runtime.ScheduleInitialization();
            return runtime;
        }

        public void Shutdown()
        {
            if (initializationCoroutine != null)
            {
                StopCoroutine(initializationCoroutine);
                initializationCoroutine = null;
            }

            foreach (var rider in FindObjectsOfType<MootorVehicleRiderController>(true))
                if (rider != null)
                    Destroy(rider);

            foreach (var fuelController in FindObjectsOfType<MootorVehicleFuelController>(true))
                if (fuelController != null)
                    Destroy(fuelController);

            foreach (var materialController in FindObjectsOfType<MootorVehicleMaterialController>(true))
                if (materialController != null)
                    Destroy(materialController);

            foreach (var ambientController in FindObjectsOfType<MootorVehicleAmbientMooController>(true))
                if (ambientController != null)
                    Destroy(ambientController);

            foreach (var impactController in FindObjectsOfType<MootorVehicleImpactController>(true))
                if (impactController != null)
                    Destroy(impactController);

            Destroy(gameObject);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeGlobalEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeGlobalEvents();
        }

        private void SubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onEnterVehicle += HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            GlobalEvents.onExitVehicle += HandleVehicleExited;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        }

        private void UnsubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeGlobalEvents();
            ScheduleInitialization();
        }

        private void HandleGameLoadedLate()
        {
            SubscribeGlobalEvents();
            ScheduleInitialization();
        }

        private void HandleGameUnloaded()
        {
            if (initializationCoroutine == null)
                return;

            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }

        private void HandleVehicleEntered(VehicleController vehicleController)
        {
            TryConfigureVehicle(vehicleController, "vehicle-entered");
        }

        private void HandleVehicleExited(VehicleController vehicleController)
        {
            if (!IsMootorVehicle(vehicleController))
                return;

            ApplyFreeParking(vehicleController);
        }

        private void HandleFullMenuToggle(bool isOpen)
        {
            if (!isOpen)
                return;

            if (!MootorVehicleDealerStock.EnsureVehicleAvailable(vehicleTypeName, context))
                context?.Logger.Warn("Moo-tor Vehicle: dealer catalog was not ready when the full menu opened.");
        }

        private void ScheduleInitialization()
        {
            if (initializationCoroutine != null)
                StopCoroutine(initializationCoroutine);

            initializationCoroutine = StartCoroutine(InitializeForLifecycle());
        }

        private IEnumerator InitializeForLifecycle()
        {
            var previousMatchedCount = -1;
            var stablePasses = 0;
            var dealerReady = false;
            for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
            {
                dealerReady |= MootorVehicleDealerStock.EnsureVehicleAvailable(vehicleTypeName, context);
                EnsureVehiclesConfigured(out var matchedCount, out _);

                if (dealerReady && matchedCount == previousMatchedCount)
                    stablePasses++;
                else
                    stablePasses = 0;

                previousMatchedCount = matchedCount;
                if (dealerReady && stablePasses >= RequiredStablePasses)
                    break;

                if (attempt < InitializationRetryCount)
                    yield return new WaitForSecondsRealtime(InitializationRetryDelay);
            }

            initializationCoroutine = null;
            TryShowMissingVeterinarianNotice();
        }

        private void TryShowMissingVeterinarianNotice()
        {
            if (missingVeterinarianNoticeShown || SaveGameManager.Current == null)
                return;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(
                        assembly.GetName().Name,
                        VeterinarianAssemblyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

            try
            {
                Notifications.Show(
                    NotificationType.Info,
                    MissingVeterinarianNotificationKey,
                    null,
                    7f,
                    MissingVeterinarianNotificationId,
                    null,
                    true,
                    false);
                missingVeterinarianNoticeShown = true;
                MootorVehicleDiagnostics.Info(
                    context,
                    "Moo-tor Vehicle: Mobile Veterinarian was not loaded; displayed dependency notice.");
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    "Moo-tor Vehicle: could not display the missing Mobile Veterinarian notice: " +
                    exception.GetBaseException().Message);
            }
        }

        private void EnsureVehiclesConfigured(out int matchedCount, out int configuredCount)
        {
            matchedCount = 0;
            configuredCount = 0;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance == null ||
                    !string.Equals(
                        vehicleController.vehicleInstance.vehicleTypeName,
                        vehicleTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                matchedCount++;
                if (TryConfigureVehicle(vehicleController, "lifecycle-scan"))
                    configuredCount++;
            }
        }

        private bool TryConfigureVehicle(VehicleController? vehicleController, string source)
        {
            if (!IsMootorVehicle(vehicleController))
                return false;

            var configuredVehicle = vehicleController!;
            ApplyFreeParking(configuredVehicle);

            var riderController = configuredVehicle.GetComponent<MootorVehicleRiderController>();
            var added = riderController == null;
            if (added)
                riderController = configuredVehicle.gameObject.AddComponent<MootorVehicleRiderController>();

            var fuelController = configuredVehicle.GetComponent<MootorVehicleFuelController>();
            if (fuelController == null)
                fuelController = configuredVehicle.gameObject.AddComponent<MootorVehicleFuelController>();

            var materialController = configuredVehicle.GetComponent<MootorVehicleMaterialController>();
            if (materialController == null)
                materialController = configuredVehicle.gameObject.AddComponent<MootorVehicleMaterialController>();

            var ambientController = configuredVehicle.GetComponent<MootorVehicleAmbientMooController>();
            if (ambientController == null)
                ambientController = configuredVehicle.gameObject.AddComponent<MootorVehicleAmbientMooController>();

            var impactController = configuredVehicle.GetComponent<MootorVehicleImpactController>();
            if (impactController == null)
                impactController = configuredVehicle.gameObject.AddComponent<MootorVehicleImpactController>();

            materialController.Initialize(configuredVehicle, context);
            fuelController.Initialize(configuredVehicle, context);
            ambientController.Initialize(configuredVehicle, context);
            impactController.Initialize(configuredVehicle, context);
            riderController!.Initialize(configuredVehicle, context);
            if (source == "vehicle-entered")
                riderController.NotifyMounted();
            return added;
        }

        private bool IsMootorVehicle(VehicleController? vehicleController)
        {
            return vehicleController?.vehicleInstance != null &&
                   string.Equals(
                       vehicleController.vehicleInstance.vehicleTypeName,
                       vehicleTypeName,
                       StringComparison.Ordinal);
        }

        private void ApplyFreeParking(VehicleController vehicleController)
        {
            var instance = vehicleController.vehicleInstance;
            if (instance == null)
                return;

            var changed = instance.parkingState != ParkingState.NotAvailable ||
                          !string.IsNullOrEmpty(instance.parkingNeighbourhood) ||
                          instance.unpaidParkingAmount > 0f;
            if (!changed)
                return;

            // Native scooters never run CarController.UpdateParkingZone(), so their saved parking
            // state does not become Illegal or accrue meter fees. Apply that same exemption at the
            // standard vehicle-exit event while keeping the cow on the proven car controller.
            instance.parkingState = ParkingState.NotAvailable;
            instance.parkingNeighbourhood = string.Empty;
            instance.unpaidParkingAmount = 0f;
            SaveGameManager.MarkChange();
            GlobalEvents.onVehicleVariablesChanged?.Invoke();
            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle: applied free sidewalk parking to vehicle={vehicleController.GetInstanceID()}.");
        }
    }
}
