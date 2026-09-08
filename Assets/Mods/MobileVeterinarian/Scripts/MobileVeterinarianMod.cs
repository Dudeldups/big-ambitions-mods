#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(MobileVeterinarian.MobileVeterinarianMod))]

namespace MobileVeterinarian
{
    [ModEntryOnCityLoad]
    public sealed class MobileVeterinarianMod : IModBigAmbitions
    {
        private MobileVeterinarianRuntime? runtime;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _ = AnimalVehicleRegistry.GetRegistrations();
            runtime = MobileVeterinarianRuntime.Install(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown("game or city unload");
            runtime = null;
            return Task.CompletedTask;
        }
    }
}
