#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(CameraTools.CameraToolsMod))]

namespace CameraTools
{
    [ModEntryOnInitializationLoad]
    public sealed class CameraToolsMod : IModBigAmbitions
    {
        private static readonly CameraToolsSettings SharedSettings = new();
        private static readonly CameraToolsOptions SharedOptions = new();
        private static CameraToolsRuntime? activeRuntime;
        private static string? activeModId;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            if (activeModId == context.ModId && activeRuntime != null)
            {
                context.Logger.Info($"CameraTools: skipping duplicate load for modId={context.ModId}.");
                return Task.CompletedTask;
            }

            if (activeModId != null)
            {
                context.Logger.Info($"CameraTools: tearing down previous load for modId={activeModId} before reinitializing modId={context.ModId}.");
                SharedOptions.Shutdown();
            }

            activeRuntime?.Shutdown();
            activeRuntime = null;

            SharedOptions.Initialize(context, SharedSettings);
            activeRuntime = CameraToolsRuntime.Initialize(context, SharedSettings);
            activeModId = context.ModId;
            context.Logger.Info("CameraTools: runtime initialized.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            activeRuntime?.Shutdown();
            activeRuntime = null;
            SharedOptions.Shutdown();
            activeModId = null;
            return Task.CompletedTask;
        }
    }
}
