#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(FreelancePhotographer.FreelancePhotographerMod))]

namespace FreelancePhotographer
{
    [ModEntryOnCityLoad]
    public sealed class FreelancePhotographerMod : IModBigAmbitions
    {
        private static FreelancePhotographerRuntime? activeRuntime;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            activeRuntime?.Shutdown();
            activeRuntime = FreelancePhotographerRuntime.Initialize(context);
            context.Logger.Info("Freelance Photographer: V1 runtime initialized. Press F9 for Photo Jobs.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            activeRuntime?.Shutdown();
            activeRuntime = null;
            PhotographySaveService.ResetCache();
            return Task.CompletedTask;
        }
    }
}
