#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(MonzaTestTrack.MonzaTestTrackMod))]

namespace MonzaTestTrack
{
    [ModEntryOnCityLoad]
    public sealed class MonzaTestTrackMod : IModBigAmbitions
    {
        private GameObject? _runtimeObject;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _runtimeObject = new GameObject("MonzaTestTrack.Runtime");
            var runtime = _runtimeObject.AddComponent<MonzaTestTrackRuntime>();
            runtime.Initialize(context.ModRootPath, context.Logger);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_runtimeObject != null)
            {
                var runtime = _runtimeObject.GetComponent<MonzaTestTrackRuntime>();
                runtime?.Shutdown();
                UnityEngine.Object.Destroy(_runtimeObject);
                _runtimeObject = null;
            }

            return Task.CompletedTask;
        }
    }
}
