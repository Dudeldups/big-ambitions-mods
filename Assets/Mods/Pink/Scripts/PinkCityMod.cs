#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(Pink.PinkCityMod))]

namespace Pink
{
    [ModEntryOnCityLoad]
    public sealed class PinkCityMod : IModBigAmbitions
    {
        // Set this to false for the release build. The logger file/class can stay in the mod.
        private const bool EnableDebugLogging = true;

        // Keep false by default. Turn on only if you need every renderer/material decision in the log.
        private const bool EnableVerbosePatchLogging = false;

        private static GameObject? watcherObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            PinkRuntime.Initialize(
                context.ModId,
                context.Logger,
                EnableDebugLogging,
                EnableVerbosePatchLogging);

            EnsureWatcher();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (watcherObject != null)
            {
                UnityEngine.Object.Destroy(watcherObject);
                watcherObject = null;
            }

            PinkRuntime.Restore();
            PinkRuntime.Reset();
            return Task.CompletedTask;
        }

        private static void EnsureWatcher()
        {
            if (watcherObject == null)
            {
                watcherObject = new GameObject("Pink.CityWatcher");
                UnityEngine.Object.DontDestroyOnLoad(watcherObject);
            }

            var watcher = watcherObject.GetComponent<PinkWatcher>();
            if (watcher == null)
                watcher = watcherObject.AddComponent<PinkWatcher>();

            watcher.Initialize();
        }
    }

    internal sealed class PinkWatcher : MonoBehaviour
    {
        private const float FirstScanDelaySeconds = 8f;
        private const float RetryIntervalSeconds = 6f;
        private const int StableScanThreshold = 1;
        private const int MaxScanPasses = 1;

        private float elapsedSeconds;
        private float nextScanAtSeconds;
        private int stableScans;
        private int scanPasses;
        private bool stopped;

        internal void Initialize()
        {
            elapsedSeconds = 0f;
            nextScanAtSeconds = FirstScanDelaySeconds;
            stableScans = 0;
            scanPasses = 0;
            stopped = false;

            PinkFileLogger.Info(
                $"Pink watcher initialized. firstDelay={FirstScanDelaySeconds:0.0}s interval={RetryIntervalSeconds:0.0}s maxPasses={MaxScanPasses} stableThreshold={StableScanThreshold}",
                alsoGameLog: true);
        }

        private void Update()
        {
            if (stopped)
                return;

            elapsedSeconds += Time.unscaledDeltaTime;
            if (elapsedSeconds < nextScanAtSeconds)
                return;

            scanPasses++;
            var result = PinkRuntime.ApplyPinkPass(scanPasses);
            nextScanAtSeconds = elapsedSeconds + RetryIntervalSeconds;

            if (!result.Ready)
                return;

            if (result.NewCandidateCount == 0 && result.NewPatchCount == 0)
                stableScans++;
            else
                stableScans = 0;

            if (stableScans < StableScanThreshold && scanPasses < MaxScanPasses)
                return;

            stopped = true;
            PinkFileLogger.Info(
                $"Stopping pink scan. reason={(stableScans >= StableScanThreshold ? "stable" : "maxPasses")} passes={scanPasses} stable={stableScans} " +
                $"candidatesSeen={PinkRuntime.CandidateRootCount} processedVehicles={PinkRuntime.ProcessedVehicleRootCount} processedNpcs={PinkRuntime.ProcessedNpcRootCount} " +
                $"patchedMaterials={PinkRuntime.PatchedMaterialCount} patchedRendererSlots={PinkRuntime.PatchedRendererSlotCount} processedVehicleRenderers={PinkRuntime.ProcessedVehicleRendererCount} processedNpcRenderers={PinkRuntime.ProcessedNpcRendererCount}.",
                alsoGameLog: true);
        }
    }
}
