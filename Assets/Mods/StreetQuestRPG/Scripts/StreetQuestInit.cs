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
            StreetQuestShared.RefreshQuestInteractionAddress();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            StreetQuestShared.UnbindContactFromAddress(StreetQuestShared.HomelessAddress);
            return Task.CompletedTask;
        }
    }
}
