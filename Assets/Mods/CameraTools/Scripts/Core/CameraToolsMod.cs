#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;

[assembly: RegisterModClass(typeof(CameraTools.CameraToolsMod))]

namespace CameraTools;

[ModEntryOnInitializationLoad]
public sealed class CameraToolsMod : IModBigAmbitions
{
    private readonly CameraToolsSettings settings = new();
    private readonly CameraToolsOptions options = new();
    private CameraToolsRuntime? runtime;

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        options.Initialize(context, settings);
        runtime = CameraToolsRuntime.Initialize(context, settings);
        context.Logger.Info("CameraTools: runtime initialized.");
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
