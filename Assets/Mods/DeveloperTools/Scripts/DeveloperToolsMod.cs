#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(DeveloperTools.DeveloperToolsMod))]

namespace DeveloperTools
{
    [ModEntryOnInitializationLoad]
    public sealed class DeveloperToolsMod : IModBigAmbitions
    {
        private readonly DeveloperToolsSettings settings = new DeveloperToolsSettings();
        private readonly DeveloperToolsOptions options = new DeveloperToolsOptions();
        private DeveloperToolsRuntime? runtime;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            options.Initialize(context, settings);
            runtime = DeveloperToolsRuntime.Initialize(context, settings);
            context.Logger.Info("DeveloperTools: loaded. Open the testing UI with " + settings.UiHotkey + ".");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;
            options.Shutdown();
            return Task.CompletedTask;
        }
    }
}
