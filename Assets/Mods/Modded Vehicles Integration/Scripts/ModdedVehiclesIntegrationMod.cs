#nullable enable
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(ModdedVehiclesIntegration.ModdedVehiclesIntegrationMod))]

namespace ModdedVehiclesIntegration
{
    [ModEntryOnInitializationLoad]
    public sealed class ModdedVehiclesIntegrationMod : IModBigAmbitions
    {
        private ModdedVehiclesIntegrationRuntime? runtime;

        public string[] RelativeAssetBundlePaths => System.Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            DealerLayoutIntegration.EnsureApplied(context);
            runtime = ModdedVehiclesIntegrationRuntime.Initialize(context);
            context.Logger.Info("Modded Vehicles Integration: dealer desks and vehicle catalog integration initialized.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;
            DealerLayoutIntegration.Restore();
            return Task.CompletedTask;
        }
    }
}
