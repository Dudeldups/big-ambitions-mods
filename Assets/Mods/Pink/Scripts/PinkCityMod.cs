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
        private const bool EnableDebugLogging = false;

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
        private const float FirstLoadingUiScanDelaySeconds = 0.05f;
        private const float LoadingUiScanIntervalSeconds = 0.35f;
        private const int MaxLoadingUiScanPasses = 4;
        private const float HudUiScanIntervalSeconds = 0.5f;
        private const int MaxHudUiScanPasses = 4;

        private float elapsedSeconds;
        private float nextScanAtSeconds;
        private int stableScans;
        private int scanPasses;
        private bool stopped;
        private bool loadingUiScanStopped;
        private bool hudUiScanStopped;
        private bool hudUiScanArmed;
        private int loadingUiScanPasses;
        private int hudUiScanPasses;
        private float nextLoadingUiScanAtSeconds;
        private float nextHudUiScanAtSeconds;

        internal void Initialize()
        {
            elapsedSeconds = 0f;
            nextScanAtSeconds = FirstScanDelaySeconds;
            stableScans = 0;
            scanPasses = 0;
            stopped = false;
            loadingUiScanStopped = false;
            hudUiScanStopped = false;
            hudUiScanArmed = false;
            loadingUiScanPasses = 0;
            hudUiScanPasses = 0;
            nextLoadingUiScanAtSeconds = FirstLoadingUiScanDelaySeconds;
            nextHudUiScanAtSeconds = 0f;

            PinkFileLogger.Info(
                $"Pink watcher initialized. firstDelay={FirstScanDelaySeconds:0.0}s interval={RetryIntervalSeconds:0.0}s maxPasses={MaxScanPasses} stableThreshold={StableScanThreshold}",
                alsoGameLog: true);
        }

        private void Update()
        {
            elapsedSeconds += Time.unscaledDeltaTime;

            if (!loadingUiScanStopped && elapsedSeconds >= nextLoadingUiScanAtSeconds)
            {
                loadingUiScanPasses++;
                PinkRuntime.ApplyLoadingUiTintPass();

                if (loadingUiScanPasses >= MaxLoadingUiScanPasses)
                    loadingUiScanStopped = true;
                else
                    nextLoadingUiScanAtSeconds = elapsedSeconds + LoadingUiScanIntervalSeconds;
            }

            if (hudUiScanArmed && !hudUiScanStopped && elapsedSeconds >= nextHudUiScanAtSeconds)
            {
                hudUiScanPasses++;
                PinkRuntime.ApplyMainHudUiTintPass();

                if (hudUiScanPasses >= MaxHudUiScanPasses)
                    hudUiScanStopped = true;
                else
                    nextHudUiScanAtSeconds = elapsedSeconds + HudUiScanIntervalSeconds;
            }

            if (stopped)
                return;

            if (elapsedSeconds < nextScanAtSeconds)
                return;

            scanPasses++;
            var result = PinkRuntime.ApplyPinkPass(scanPasses);
            nextScanAtSeconds = elapsedSeconds + RetryIntervalSeconds;

            if (!hudUiScanArmed)
            {
                hudUiScanArmed = true;
                hudUiScanPasses = 0;
                nextHudUiScanAtSeconds = elapsedSeconds;
            }

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
