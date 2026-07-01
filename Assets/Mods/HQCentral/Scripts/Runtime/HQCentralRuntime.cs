#nullable enable
using System;
using System.Diagnostics;
using BAModAPI;
using HQCentral.Debugging;
using HQCentral.Discovery;
using HQCentral.Model;
using HQCentral.Snapshot;
using HQCentral.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HQCentral.Runtime
{
    [DefaultExecutionOrder(-10000)]
    public sealed class HQCentralRuntime : MonoBehaviour
    {
        private static HQCentralRuntime? instance;
        private readonly HQDiscoveryService discoveryService = new HQDiscoveryService();
        private readonly HQCentralSnapshotBuilder snapshotBuilder = new HQCentralSnapshotBuilder();
        private readonly HQCentralWindow window = new HQCentralWindow();
        private ModContext? context;
        private HQCentralSnapshot? currentSnapshot;
        private bool isShuttingDown;
        private bool cursorStateCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLockMode;

        public static HQCentralRuntime Initialize(ModContext context)
        {
            var runtime = FindObjectOfType<HQCentralRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(HQCentralRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<HQCentralRuntime>();
            }

            runtime.context = context;
            runtime.isShuttingDown = false;
            instance = runtime;
            return runtime;
        }

        public void Shutdown()
        {
            if (isShuttingDown)
                return;

            isShuttingDown = true;
            if (instance == this)
                instance = null;

            CloseOverview();
            window.Dispose();
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
            if (Input.GetKeyDown(KeyCode.F10))
            {
                if (window.IsVisible)
                    CloseOverview();
                else
                    OpenOverview();
            }

            if (Input.GetKeyDown(KeyCode.F4))
                WriteVisibleUiSnapshot();

            if (window.IsVisible)
                Input.ResetInputAxes();
        }

        private void OnGUI()
        {
            if (!window.IsVisible)
                return;

            try
            {
                window.OnGui(
                    () => RefreshOverview("manual refresh"),
                    CloseOverview,
                    LogLogisticsPlanSelection);
            }
            catch (Exception exception)
            {
                HQCentralFileLogger.Error("HQ Central overview rendering failed; closing the window.", exception);
                context?.Logger.Error(exception);
                CloseOverview();
            }
        }

        private void OpenOverview()
        {
            if (!RefreshOverview("overview opened"))
                return;

            CaptureCursorState();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            window.Show(currentSnapshot!);
            HQCentralFileLogger.Info("Read-only HQ Central overview opened.");
        }

        private bool RefreshOverview(string reason)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var discovery = discoveryService.Discover();
                var discoveryMilliseconds = stopwatch.ElapsedMilliseconds;
                currentSnapshot = snapshotBuilder.Build(discovery);
                var buildMilliseconds = stopwatch.ElapsedMilliseconds - discoveryMilliseconds;
                HQCentralSnapshotLogWriter.Write(discovery, currentSnapshot, reason);
                var loggingMilliseconds = stopwatch.ElapsedMilliseconds - discoveryMilliseconds - buildMilliseconds;
                if (window.IsVisible)
                    window.SetSnapshot(currentSnapshot);

                var message =
                    $"HQ Central snapshot refreshed: headquarters={currentSnapshot.TotalHeadquarters}, " +
                    $"employees={currentSnapshot.TotalEmployees}, issues={currentSnapshot.Issues.Count}. " +
                    $"Timing: discovery={discoveryMilliseconds}ms, build={buildMilliseconds}ms, " +
                    $"log={loggingMilliseconds}ms, total={stopwatch.ElapsedMilliseconds}ms. " +
                    $"Data log: {HQCentralFileLogger.DataLogPath}";
                HQCentralFileLogger.Info(message);
                context?.Logger.Info(message);
                return true;
            }
            catch (Exception exception)
            {
                HQCentralFileLogger.Error("HQ Central snapshot refresh failed.", exception);
                context?.Logger.Error(exception);
                return false;
            }
        }

        private void CloseOverview()
        {
            var wasVisible = window.IsVisible;
            window.Hide();
            RestoreCursorState();
            if (wasVisible)
                HQCentralFileLogger.Info("Read-only HQ Central overview closed.");
        }

        private static void LogLogisticsPlanSelection(HQCentralLogisticsPlan plan)
        {
            HQCentralFileLogger.Info(
                $"Logistics plan selected: manager={plan.AssignedManagerName}, hq={plan.HeadquartersName}, " +
                $"kind={(plan.IsFactory ? "Factory" : "Warehouse")}, origin={plan.OriginName} ({plan.OriginAddress}), " +
                $"destinations={plan.Destinations.Count}, status={plan.Status}.");
        }

        private void CaptureCursorState()
        {
            if (cursorStateCaptured)
                return;

            previousCursorVisible = Cursor.visible;
            previousCursorLockMode = Cursor.lockState;
            cursorStateCaptured = true;
        }

        private void RestoreCursorState()
        {
            if (!cursorStateCaptured)
                return;

            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockMode;
            cursorStateCaptured = false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CloseOverview();
            currentSnapshot = null;
            HQCentralFileLogger.Info($"Scene changed to {scene.name}; cached HQ snapshot cleared.");
        }

        private void WriteVisibleUiSnapshot()
        {
            try
            {
                var result = HQCentralUiSnapshotWriter.WriteVisibleUiSnapshot();
                context?.Logger.Info(
                    $"HQCentral: visible UI snapshot written ({result.CanvasCount} canvases, " +
                    $"{result.ElementCount} elements): {result.LogPath}");
            }
            catch (Exception exception)
            {
                HQCentralFileLogger.Error("Visible UI snapshot failed.", exception);
                context?.Logger.Error(exception);
            }
        }
    }
}
