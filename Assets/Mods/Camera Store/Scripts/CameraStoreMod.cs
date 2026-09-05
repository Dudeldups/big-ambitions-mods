#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;

[assembly: RegisterModClass(typeof(CameraStore.CameraStoreMod))]
[assembly: RegisterModClass(typeof(CameraStore.CameraStoreCityMod))]

namespace CameraStore
{
    [ModEntryOnInitializationLoad]
    public sealed class CameraStoreMod : IModBigAmbitions
    {
        private readonly CameraStoreItemRegistration itemRegistration = new();
        private readonly CameraStoreBusinessRegistration businessRegistration = new();

        public string[] RelativeAssetBundlePaths => new[] { CameraStoreIds.BundleKey };

        public Task OnLoadAsync(ModContext context)
        {
            try
            {
                var bundle = AssetService.GetBundle(context.ModId, CameraStoreIds.BundleKey);
                if (bundle == null)
                    throw new InvalidOperationException($"Camera Store bundle not found: {CameraStoreIds.BundleKey}");

                itemRegistration.LoadAndRegister(bundle);
                businessRegistration.LoadAndRegister(bundle);
                context.Logger.Info("Camera Store registered 8 products, 2 fixtures, and its business type.");
            }
            catch (Exception exception)
            {
                businessRegistration.Unregister();
                itemRegistration.Unregister();
                context.Logger.Error(exception);
                throw;
            }

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            businessRegistration.Unregister();
            itemRegistration.Unregister();
            return Task.CompletedTask;
        }
    }

    [ModEntryOnCityLoad]
    public sealed class CameraStoreCityMod : IModBigAmbitions
    {
        private readonly CameraStoreImporterIntegration importerIntegration = new();
        private readonly CameraStoreShelfIntegration shelfIntegration = new();

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public async Task OnLoadAsync(ModContext context)
        {
            try
            {
                // City services and item caches can settle over several initialization continuations.
                // Each integration is idempotent, so a short bounded retry handles both new and loaded saves.
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    shelfIntegration.Apply();
                    importerIntegration.Apply();
                    if (attempt < 5)
                        await Task.Yield();
                }

                context.Logger.Info("Camera Store products added to BlueStone Imports and retail fixtures.");
            }
            catch (Exception exception)
            {
                importerIntegration.Restore();
                shelfIntegration.Restore();
                context.Logger.Error(exception);
                throw;
            }
        }

        public Task OnUnloadAsync()
        {
            importerIntegration.Restore();
            shelfIntegration.Restore();
            return Task.CompletedTask;
        }
    }
}
