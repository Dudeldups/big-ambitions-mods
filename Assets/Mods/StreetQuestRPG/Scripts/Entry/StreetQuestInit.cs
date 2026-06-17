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
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            StreetQuestShared.InitializeDebugLogging(context, nameof(StreetQuestInit));
            StreetQuestCharacterCatalog.Initialize(context?.ModRootPath, context?.Logger);
            StreetQuestQuestCatalog.Initialize(context?.ModRootPath, context?.Logger);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            StreetQuestShared.RestorePatchedDialogs();
            return Task.CompletedTask;
        }
    }
}
