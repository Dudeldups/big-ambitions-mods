using System;
using System.Threading.Tasks;
using BAModAPI;
namespace StreetQuestRPG
{
    [ModEntryOnCityLoad]
    public sealed class StreetQuestCity : IModBigAmbitions
    {
        public string[] RelativeAssetBundlePaths => new[] { StreetQuestAssetBundleService.BundleKey };

        public Task OnLoadAsync(ModContext context)
        {
            StreetQuestRuntimeBootstrap.Configure(context, nameof(StreetQuestCity));
            StreetQuestRuntimeBootstrap.EnsureCityRuntimeReady();
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
