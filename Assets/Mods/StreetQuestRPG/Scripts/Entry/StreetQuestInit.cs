using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(StreetQuestRPG.StreetQuestInit))]
[assembly: RegisterModClass(typeof(StreetQuestRPG.StreetQuestCity))]

namespace StreetQuestRPG
{
    [ModEntryOnInitializationLoad]
    public sealed class StreetQuestInit : IModBigAmbitions
    {
        public string[] RelativeAssetBundlePaths => new[] { StreetQuestAssetBundleService.BundleKey };

        public Task OnLoadAsync(ModContext context)
        {
            StreetQuestRuntimeBootstrap.Configure(context, nameof(StreetQuestInit));
            StreetQuestRuntimeBootstrap.EnsureWatcher();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            StreetQuestRuntimeBootstrap.Shutdown();
            return Task.CompletedTask;
        }
    }
}
