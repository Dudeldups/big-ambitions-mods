#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(MonzaTestTrack.MonzaTestTrackMod))]

namespace MonzaTestTrack
{
    [ModEntryOnInitializationLoad]
    public sealed class MonzaTestTrackMod : IModBigAmbitions
    {
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            return Task.CompletedTask;
        }
    }
}
