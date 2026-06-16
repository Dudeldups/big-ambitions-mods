#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;
using VehicleRuntimeTuner.Utils;

[assembly: RegisterModClass(typeof(VehicleRuntimeTuner.Entry.VehicleRuntimeTunerMod))]

namespace VehicleRuntimeTuner.Entry
{
    [ModEntryOnInitializationLoad]
    public sealed class VehicleRuntimeTunerMod : IModBigAmbitions
    {
        private static GameObject? runtimeObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            try
            {
                if (runtimeObject != null)
                    UnityEngine.Object.Destroy(runtimeObject);

                runtimeObject = new GameObject("VehicleRuntimeTunerRuntime");
                UnityEngine.Object.DontDestroyOnLoad(runtimeObject);

                var runtime = runtimeObject.AddComponent<Runtime.VehicleRuntimeTunerRuntime>();
                runtime.Initialize(context);

                if (VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                {
                    context.Logger.Info("VehicleRuntimeTuner: loaded.");
                    VehicleRuntimeTunerFileLogger.Log("INFO", "VehicleRuntimeTuner loaded.");
                }
            }
            catch (Exception ex)
            {
                if (VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                    VehicleRuntimeTunerFileLogger.Log("ERROR", "OnLoadAsync failed: " + ex);
                throw;
            }

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (runtimeObject != null)
                UnityEngine.Object.Destroy(runtimeObject);

            runtimeObject = null;
            return Task.CompletedTask;
        }
    }
}
