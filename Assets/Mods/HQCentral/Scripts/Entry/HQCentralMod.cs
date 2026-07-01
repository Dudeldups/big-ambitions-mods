#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using HQCentral.Debugging;
using HQCentral.Runtime;

[assembly: RegisterModClass(typeof(HQCentral.Entry.HQCentralMod))]

namespace HQCentral.Entry
{
    [ModEntryOnInitializationLoad]
    public sealed class HQCentralMod : IModBigAmbitions
    {
        private static HQCentralRuntime? activeRuntime;
        private static string? activeModId;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            if (activeModId == context.ModId && activeRuntime != null)
                return Task.CompletedTask;

            activeRuntime?.Shutdown();
            activeRuntime = null;

            HQCentralFileLogger.StartSession();

            try
            {
                activeRuntime = HQCentralRuntime.Initialize(context);
                activeModId = context.ModId;
                context.Logger.Info(
                    $"HQCentral: runtime initialized. F10 opens the read-only overview; F4 writes a visible UI snapshot. " +
                    $"Data log: {HQCentralFileLogger.DataLogPath}");
                HQCentralFileLogger.Info("HQCentral runtime initialized. Hotkeys: F10 overview, F4 visible UI snapshot.");
            }
            catch (Exception exception)
            {
                HQCentralFileLogger.Error("Mod initialization failed.", exception);
                context.Logger.Error(exception);
                throw;
            }

            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            activeRuntime?.Shutdown();
            activeRuntime = null;
            activeModId = null;
            HQCentralFileLogger.Info("HQCentral runtime shut down.");
            return Task.CompletedTask;
        }
    }
}
