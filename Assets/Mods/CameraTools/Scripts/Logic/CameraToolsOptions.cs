#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace CameraTools
{
    public sealed class CameraToolsOptions
    {
        private const string GameplayZoomKey = "camera_tools_gameplay_max_zoom";
        private const string VehicleZoomKey = "camera_tools_vehicle_max_zoom";
        private const string MapDistanceKey = "camera_tools_map_distance";

        private ModContext? context;

        public void Initialize(ModContext modContext, CameraToolsSettings settings)
        {
            context = modContext;

            var options =
                new ModOptions()
                    .AddHeader("cameratools_options_header")
                    .AddSlider(GameplayZoomKey, "cameratools_gameplay_zoom_label", 15, 80, settings.GameplayMaxZoom,
                        value => settings.GameplayMaxZoom = value, "cameratools_slider_value")
                    .AddSlider(MapDistanceKey, "cameratools_map_distance_label", 100, 700, settings.MapDistance,
                        value => settings.MapDistance = value, "cameratools_slider_value")
                    .AddSlider(VehicleZoomKey, "cameratools_vehicle_zoom_label", 20, 120, settings.VehicleMaxZoom,
                        value => settings.VehicleMaxZoom = value, "cameratools_slider_value");

            OptionsService.Register(modContext.ModId, options);
            modContext.Logger.Info("CameraTools: options registered.");
        }

        public void Shutdown()
        {
            if (context == null)
                return;

            OptionsService.RemoveModOptions(context.ModId);
            context.Logger.Info("CameraTools: options unregistered.");
            context = null;
        }
    }
}
