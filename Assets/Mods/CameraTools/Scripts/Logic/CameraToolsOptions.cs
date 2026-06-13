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
        private const string ScenicViewHotkeyKey = "camera_tools_scenic_view_hotkey";
        private const string ScenicViewDebugLoggingKey = "camera_tools_scenic_view_debug_logging";
        private static readonly string[] ScenicViewHotkeyChoices =
        {
            "cameratools_hotkey_f6",
            "cameratools_hotkey_f7",
            "cameratools_hotkey_home",
            "cameratools_hotkey_insert",
            "cameratools_hotkey_delete"
        };

        private static readonly UnityEngine.KeyCode[] ScenicViewHotkeyValues =
        {
            UnityEngine.KeyCode.F6,
            UnityEngine.KeyCode.F7,
            UnityEngine.KeyCode.Home,
            UnityEngine.KeyCode.Insert,
            UnityEngine.KeyCode.Delete
        };

        private ModContext? context;
        private bool isRegistered;

        public void Initialize(ModContext modContext, CameraToolsSettings settings)
        {
            context = modContext;
            OptionsService.RemoveModOptions(modContext.ModId);

            var options =
                new ModOptions()
                    .AddHeader("cameratools_options_header")
                    .AddSlider(GameplayZoomKey, "cameratools_gameplay_zoom_label", 15, 90, settings.GameplayMaxZoom,
                        value => settings.GameplayMaxZoom = value, "cameratools_slider_value")
                    .AddSlider(MapDistanceKey, "cameratools_map_distance_label", 100, 800, settings.MapDistance,
                        value => settings.MapDistance = value, "cameratools_slider_value")
                    .AddSlider(VehicleZoomKey, "cameratools_vehicle_zoom_label", 20, 120, settings.VehicleMaxZoom,
                        value => settings.VehicleMaxZoom = value, "cameratools_slider_value")
                    .AddDropdown(ScenicViewHotkeyKey, "cameratools_scenic_view_hotkey_label", ScenicViewHotkeyChoices,
                        GetScenicViewHotkeyIndex(settings.ScenicViewHotkey),
                        value => settings.ScenicViewHotkey = ScenicViewHotkeyValues[value])
                    .AddToggle(ScenicViewDebugLoggingKey, "cameratools_scenic_view_debug_logging_label",
                        settings.EnableScenicViewDebugLogging,
                        value => settings.EnableScenicViewDebugLogging = value);

            OptionsService.Register(modContext.ModId, options);
            isRegistered = true;
            modContext.Logger.Info("CameraTools: options registered.");
        }

        public void Shutdown()
        {
            if (context == null)
                return;

            OptionsService.RemoveModOptions(context.ModId);
            isRegistered = false;
            context.Logger.Info("CameraTools: options unregistered.");
            context = null;
        }

        private static int GetScenicViewHotkeyIndex(UnityEngine.KeyCode keyCode)
        {
            for (var i = 0; i < ScenicViewHotkeyValues.Length; i++)
            {
                if (ScenicViewHotkeyValues[i] == keyCode)
                    return i;
            }

            return 1;
        }
    }
}
