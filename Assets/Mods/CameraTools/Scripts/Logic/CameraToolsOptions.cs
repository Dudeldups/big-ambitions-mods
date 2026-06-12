#nullable enable
using BAModAPI;
using BigAmbitions.Mods;

namespace CameraTools
{
    public sealed class CameraToolsOptions
    {
        private const string GameplayToggleKey = "camera_tools_gameplay_enabled";
        private const string GameplayZoomKey = "camera_tools_gameplay_max_zoom";
        private const string GameplayDefaultPitchKey = "camera_tools_gameplay_default_pitch";
        private const string GameplayMinPitchKey = "camera_tools_gameplay_min_pitch";
        private const string GameplayMaxPitchKey = "camera_tools_gameplay_max_pitch";
        private const string MapToggleKey = "camera_tools_map_enabled";
        private const string MapDistanceKey = "camera_tools_map_distance";
        private const string MapPitchKey = "camera_tools_map_pitch";
        private const string MapOrthoSizeKey = "camera_tools_map_orthographic_size";

        private ModContext? context;

        public void Initialize(ModContext modContext, CameraToolsSettings settings)
        {
            context = modContext;

            var options =
                new ModOptions()
                    .AddHeader("cameratools_options_header")
                    .AddToggle(GameplayToggleKey, "cameratools_gameplay_toggle", settings.EnableGameplayTweaks,
                        value => settings.EnableGameplayTweaks = value)
                    .AddSlider(GameplayZoomKey, "cameratools_gameplay_zoom_label", 15, 80, settings.GameplayMaxZoom,
                        value => settings.GameplayMaxZoom = value, "cameratools_slider_value")
                    .AddSlider(GameplayDefaultPitchKey, "cameratools_gameplay_pitch_default_label", 10, 80,
                        settings.GameplayDefaultPitch, value => settings.GameplayDefaultPitch = value,
                        "cameratools_slider_value")
                    .AddSlider(GameplayMinPitchKey, "cameratools_gameplay_pitch_min_label", 0, 70,
                        settings.GameplayMinPitch, value => settings.GameplayMinPitch = value,
                        "cameratools_slider_value")
                    .AddSlider(GameplayMaxPitchKey, "cameratools_gameplay_pitch_max_label", 20, 89,
                        settings.GameplayMaxPitch, value => settings.GameplayMaxPitch = value,
                        "cameratools_slider_value")
                    .AddHeader("cameratools_gameplay_pitch_help")
                    .AddToggle(MapToggleKey, "cameratools_map_toggle", settings.EnableMapTopDown,
                        value => settings.EnableMapTopDown = value)
                    .AddSlider(MapDistanceKey, "cameratools_map_distance_label", 100, 2000, settings.MapDistance,
                        value => settings.MapDistance = value, "cameratools_slider_value")
                    .AddSlider(MapPitchKey, "cameratools_map_pitch_label", 75, 90, settings.MapPitch,
                        value => settings.MapPitch = value, "cameratools_slider_value")
                    .AddSlider(MapOrthoSizeKey, "cameratools_map_size_label", 20, 1000, settings.MapOrthographicSize,
                        value => settings.MapOrthographicSize = value, "cameratools_slider_value")
                    .AddHeader("cameratools_map_help");

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
