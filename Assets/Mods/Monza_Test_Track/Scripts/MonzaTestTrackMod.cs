#nullable enable
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using UnityEngine;

[assembly: RegisterModClass(typeof(MonzaTestTrack.MonzaTestTrackMod))]

namespace MonzaTestTrack
{
    [ModEntryOnCityLoad]
    public sealed class MonzaTestTrackMod : IModBigAmbitions
    {
        public const string BundleKey = "AssetBundles/monzatesttrack.unity3d";
        public const string PrefabPath = "Assets/Mods/Monza_Test_Track/Prefabs/MonzaTestTrack.prefab";

        private GameObject? _runtimeObject;

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        public Task OnLoadAsync(ModContext context)
        {
            GameObject? bundledPrefab = null;
            try
            {
                var bundle = AssetService.GetBundle(context.ModId, BundleKey);
                if (bundle != null)
                {
                    bundledPrefab = bundle.LoadAsset<GameObject>(PrefabPath);
                    if (bundledPrefab == null)
                        context.Logger.Warn("[MonzaTestTrack] AssetBundle loaded but prefab is missing: " + PrefabPath);
                }
                else
                {
                    context.Logger.Warn(
                        "[MonzaTestTrack] Full visual AssetBundle is unavailable; using diagnostic surface fallback.");
                }
            }
            catch (System.Exception ex)
            {
                context.Logger.Warn(
                    "[MonzaTestTrack] Full visual AssetBundle could not be loaded; using diagnostic surface fallback. " +
                    ex.Message);
            }

            _runtimeObject = new GameObject("MonzaTestTrack.Runtime");
            var runtime = _runtimeObject.AddComponent<MonzaTestTrackRuntime>();
            runtime.Initialize(context.ModRootPath, context.Logger, bundledPrefab);
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
