#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MootorVehicle
{
    public sealed class MootorVehicleRuntime : MonoBehaviour
    {
        private const int InitializationRetryCount = 24;
        private const int RequiredStablePasses = 5;
        private const float InitializationRetryDelay = 0.25f;
        private const string RiderSeatName = "MootorVehicle_RiderSeat";

        private readonly HashSet<int> loggedVehicleIds = new();
        private Coroutine? initializationCoroutine;
        private ModContext? context;
        private string vehicleTypeName = string.Empty;

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
            runtime.ScheduleInitialization("mod-load");
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
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
            GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        }

        private void UnsubscribeGlobalEvents()
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
            GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeGlobalEvents();
            ScheduleInitialization($"scene-loaded:{scene.name}");
        }

        private void HandleGameLoadedLate()
        {
            SubscribeGlobalEvents();
            ScheduleInitialization("game-loaded-late");
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

        private void HandleFullMenuToggle(bool isOpen)
        {
            if (!isOpen)
                return;

            if (!MootorVehicleDealerStock.EnsureVehicleAvailable(vehicleTypeName, context))
                context?.Logger.Warn("Moo-tor Vehicle: dealer catalog was not ready when the full menu opened.");
        }

        private void ScheduleInitialization(string source)
        {
            if (initializationCoroutine != null)
                StopCoroutine(initializationCoroutine);

            initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
        }

        private IEnumerator InitializeForLifecycle(string source)
        {
            var previousMatchedCount = -1;
            var stablePasses = 0;
            var dealerReady = false;
            var maximumMatchedCount = 0;
            var configuredCount = 0;
            var attempts = 0;

            for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
            {
                attempts = attempt;
                dealerReady |= MootorVehicleDealerStock.EnsureVehicleAvailable(vehicleTypeName, context);
                EnsureVehiclesConfigured(out var matchedCount, out var configuredThisPass);
                maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);
                configuredCount += configuredThisPass;

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
            context?.Logger.Info(
                $"Moo-tor Vehicle: initialization source='{source}' attempts={attempts} " +
                $"dealerReady={dealerReady} matched={maximumMatchedCount} configured={configuredCount}.");
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
            if (vehicleController?.vehicleInstance == null ||
                !string.Equals(
                    vehicleController.vehicleInstance.vehicleTypeName,
                    vehicleTypeName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var riderController = vehicleController.GetComponent<MootorVehicleRiderController>();
            var added = riderController == null;
            if (added)
                riderController = vehicleController.gameObject.AddComponent<MootorVehicleRiderController>();

            var fuelController = vehicleController.GetComponent<MootorVehicleFuelController>();
            if (fuelController == null)
                fuelController = vehicleController.gameObject.AddComponent<MootorVehicleFuelController>();

            fuelController.Initialize(vehicleController, context);
            riderController!.Initialize(vehicleController, context);
            if (source == "vehicle-entered")
                riderController.NotifyMounted();
            LogVehicleConfiguration(vehicleController, source);
            return added;
        }

        private void LogVehicleConfiguration(VehicleController vehicleController, string source)
        {
            var instanceId = vehicleController.GetInstanceID();
            if (!loggedVehicleIds.Add(instanceId))
                return;

            var rigidbody = vehicleController.GetComponent<Rigidbody>();
            var seatFound = false;
            var visibleRenderers = 0;
            var wheelControllers = 0;

            foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
                seatFound |= string.Equals(child.name, RiderSeatName, StringComparison.Ordinal);

            foreach (var renderer in vehicleController.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    visibleRenderers++;

            foreach (var component in vehicleController.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null &&
                    string.Equals(
                        component.GetType().FullName,
                        "NWH.WheelController3D.WheelController",
                        StringComparison.Ordinal))
                {
                    wheelControllers++;
                }

            context?.Logger.Info(
                $"Moo-tor Vehicle: configured vehicle={instanceId} source='{source}' " +
                $"seat={seatFound} wheels={wheelControllers} colliders=" +
                $"{vehicleController.GetComponentsInChildren<Collider>(true).Length} " +
                $"visibleRenderers={visibleRenderers} mass={(rigidbody != null ? rigidbody.mass : 0f):F0}.");
        }
    }
}
