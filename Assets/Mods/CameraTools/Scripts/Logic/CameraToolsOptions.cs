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
        private const string HideUiHotkeyKey = "camera_tools_hide_ui_hotkey";
        private const string HideMapMarkersKey = "camera_tools_hide_map_markers";
        private static readonly string[] HotkeyChoices =
        {
            "cameratools_hotkey_f5",
            "cameratools_hotkey_f6",
            "cameratools_hotkey_f7",
            "cameratools_hotkey_f8",
            "cameratools_hotkey_home",
            "cameratools_hotkey_insert",
            "cameratools_hotkey_delete"
        };

        private static readonly UnityEngine.KeyCode[] HotkeyValues =
        {
            UnityEngine.KeyCode.F5,
            UnityEngine.KeyCode.F6,
            UnityEngine.KeyCode.F7,
            UnityEngine.KeyCode.F8,
            UnityEngine.KeyCode.Home,
            UnityEngine.KeyCode.Insert,
            UnityEngine.KeyCode.Delete
        };

        private ModContext? context;
        private string? registeredModId;

        public void Initialize(ModContext modContext, CameraToolsSettings settings)
        {
            context = modContext;
            settings.HideMapMarkersWithUi = LoadSavedHideMapMarkersValue(modContext.ModId, settings.HideMapMarkersWithUi);
            if (!string.IsNullOrEmpty(registeredModId))
            {
                LogOptionsDebug(modContext, $"CameraTools: unregistering previous options for modId={registeredModId}.");
                OptionsService.RemoveModOptions(registeredModId);
            }

            LogOptionsDebug(modContext, $"CameraTools: removing stale options for current modId={modContext.ModId} before registration.");
            OptionsService.RemoveModOptions(modContext.ModId);

            try
            {
                LogOptionsDebug(modContext, $"CameraTools: building options for modId={modContext.ModId}.");
                var options =
                    new ModOptions()
                        .AddHeader("cameratools_options_header")
                        .AddSlider(GameplayZoomKey, "cameratools_gameplay_zoom_label", 15, 43, settings.GameplayMaxZoom,
                            value => settings.GameplayMaxZoom = value, "cameratools_slider_value")
                        .AddSlider(MapDistanceKey, "cameratools_map_distance_label", 100, 800, settings.MapDistance,
                            value => settings.MapDistance = value, "cameratools_slider_value")
                        .AddSlider(VehicleZoomKey, "cameratools_vehicle_zoom_label", 20, 120, settings.VehicleMaxZoom,
                            value => settings.VehicleMaxZoom = value, "cameratools_slider_value")
                        .AddDropdown(ScenicViewHotkeyKey, "cameratools_scenic_view_hotkey_label", HotkeyChoices,
                            GetHotkeyIndex(settings.ScenicViewHotkey),
                            value => settings.ScenicViewHotkey = HotkeyValues[value])
                        .AddDropdown(HideUiHotkeyKey, "cameratools_hide_ui_hotkey_label", HotkeyChoices,
                            GetHotkeyIndex(settings.HideUiHotkey),
                            value => settings.HideUiHotkey = HotkeyValues[value])
                        .AddToggle(HideMapMarkersKey, "cameratools_hide_map_markers_label",
                            settings.HideMapMarkersWithUi,
                            value =>
                            {
                                settings.HideMapMarkersWithUi = value;
                                SaveHideMapMarkersValue(modContext.ModId, value);
                            });

                LogOptionsDebug(modContext, $"CameraTools: built options count = {options.Options.Count} for modId={modContext.ModId}.");

                LogOptionsDebug(modContext, $"CameraTools: registering options for modId={modContext.ModId}.");
                OptionsService.Register(modContext.ModId, options);
                registeredModId = modContext.ModId;
                LogOptionsDebug(modContext, $"CameraTools: options registered successfully for modId={modContext.ModId}.");
            }
            catch (System.Exception exception)
            {
                LogOptionsDebug(modContext, $"CameraTools: failed to build/register options. {exception}");
                throw;
            }
        }

        public void Shutdown()
        {
            if (context == null)
                return;

            if (!string.IsNullOrEmpty(registeredModId))
            {
                LogOptionsDebug(context, $"CameraTools: unregistering options on shutdown for modId={registeredModId}.");
                OptionsService.RemoveModOptions(registeredModId);
            }

            registeredModId = null;
            LogOptionsDebug(context, "CameraTools: options unregistered.");
            context = null;
        }

        private static int GetHotkeyIndex(UnityEngine.KeyCode keyCode)
        {
            for (var i = 0; i < HotkeyValues.Length; i++)
            {
                if (HotkeyValues[i] == keyCode)
                    return i;
            }

            return 1;
        }

        private static void LogOptionsDebug(ModContext modContext, string message)
        {
            modContext.Logger.Info(message);
        }

        private static string GetSavedHideMapMarkersKey(string modId)
        {
            return modId + "." + HideMapMarkersKey;
        }

        private static bool LoadSavedHideMapMarkersValue(string modId, bool fallbackValue)
        {
            var key = GetSavedHideMapMarkersKey(modId);
            return UnityEngine.PlayerPrefs.HasKey(key)
                ? UnityEngine.PlayerPrefs.GetInt(key, fallbackValue ? 1 : 0) != 0
                : fallbackValue;
        }

        private static void SaveHideMapMarkersValue(string modId, bool value)
        {
            var key = GetSavedHideMapMarkersKey(modId);
            UnityEngine.PlayerPrefs.SetInt(key, value ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }
    }
}
