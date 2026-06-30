#nullable enable
using System;
using BAModAPI;
using HQCentral.Debugging;
using UnityEngine;

namespace HQCentral.Runtime
{
    [DefaultExecutionOrder(-10000)]
    public sealed class HQCentralRuntime : MonoBehaviour
    {
        private static HQCentralRuntime? instance;
        private ModContext? context;
        private bool isShuttingDown;

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

            Destroy(gameObject);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F4))
                return;

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
