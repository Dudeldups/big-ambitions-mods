#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(StorageTools.StorageToolsMod))]

namespace StorageTools
{
    [ModEntryOnInitializationLoad]
    public sealed class StorageToolsMod : IModBigAmbitions
    {
        private static readonly StorageToolsSettings SharedSettings = new();
        private static readonly StorageToolsOptions SharedOptions = new();
        private static StorageToolsRuntime? activeRuntime;
        private static string? activeModId;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            if (activeModId == context.ModId && activeRuntime != null)
                return Task.CompletedTask;

            if (activeModId != null)
                SharedOptions.Shutdown();

            activeRuntime?.Shutdown();
            activeRuntime = null;

            activeRuntime = StorageToolsRuntime.Initialize(context, SharedSettings);
            SharedOptions.Initialize(context, SharedSettings);
            activeModId = context.ModId;
            StorageToolsLogger.Info(context, "StorageTools: runtime initialized.");
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
