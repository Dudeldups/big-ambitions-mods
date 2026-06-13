#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using Dialogs;
using UnityEngine;

[assembly: RegisterModClass(typeof(SharedWholesaleDesk.SharedWholesaleDeskCityMod))]

namespace SharedWholesaleDesk
{
    [ModEntryOnCityLoad]
    public sealed class SharedWholesaleDeskCityMod : IModBigAmbitions
    {
        private static GameObject? _watcherObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            SharedWholesaleDeskLog.SetLogger(context.Logger);
            SharedWholesaleDeskRuntime.Initialize();

            var dialogType = (CallDialogType)ModEnumHash.GetSafeHash(SharedWholesaleDeskRuntime.ModdedDialogTypeKey);
            SharedWholesaleDeskRuntime.SetModdedDialogType(dialogType);
            CallDialogFactory.RegisterDialog(dialogType, () => new SharedWholesaleDeskDialog());

            SharedWholesaleDeskLog.Info(
                $"Registered shared wholesale dialog type '{SharedWholesaleDeskRuntime.ModdedDialogTypeKey}' = {(int)dialogType}.");
            SharedWholesaleDeskLog.Info(
                $"File logging enabled={SharedWholesaleDeskDebugSettings.EnableFileLogging}. LogPath={SharedWholesaleDeskFileLogger.LogPath}");

            EnsureWatcher();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_watcherObject != null)
            {
                UnityEngine.Object.Destroy(_watcherObject);
                _watcherObject = null;
            }

            SharedWholesaleDeskRuntime.RestorePatchedServiceDesks();
            SharedWholesaleDeskRuntime.Reset();
            SharedWholesaleDeskLog.SetLogger(null);
            return Task.CompletedTask;
        }

        private static void EnsureWatcher()
        {
            if (_watcherObject == null)
            {
                _watcherObject = new GameObject("SharedWholesaleDesk.Watcher");
                UnityEngine.Object.DontDestroyOnLoad(_watcherObject);
            }

            var watcher = _watcherObject.GetComponent<SharedWholesaleDeskWatcher>();
            if (watcher == null)
                watcher = _watcherObject.AddComponent<SharedWholesaleDeskWatcher>();

            watcher.Initialize();
        }
    }

    internal sealed class SharedWholesaleDeskWatcher : MonoBehaviour
    {
        private const float RetryIntervalSeconds = 2f;
        private const int StableScanThreshold = 3;

        private float _elapsedSeconds;
        private float _nextScanAtSeconds;
        private int _stableScans;
        private bool _stopped;

        internal void Initialize()
        {
            _elapsedSeconds = 0f;
            _nextScanAtSeconds = 0f;
            _stableScans = 0;
            _stopped = false;
        }

        private void Update()
        {
            if (_stopped)
                return;

            _elapsedSeconds += Time.unscaledDeltaTime;
            if (_elapsedSeconds < _nextScanAtSeconds)
                return;

            var result = SharedWholesaleDeskRuntime.TryPatchServiceDesks();
            _nextScanAtSeconds = _elapsedSeconds + RetryIntervalSeconds;

            if (!result.Ready)
                return;

            if (result.FoundTargetCount > 0 && result.PatchedCount == 0)
                _stableScans++;
            else
                _stableScans = 0;

            if (_stableScans < StableScanThreshold)
                return;

            _stopped = true;
            SharedWholesaleDeskLog.Info(
                $"Stopping wholesale desk scan after {_stableScans} stable passes. Targets={result.FoundTargetCount}, patched={SharedWholesaleDeskRuntime.PatchedDeskCount}.");
        }
    }
}
