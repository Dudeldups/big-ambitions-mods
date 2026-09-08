#nullable enable
using BAModAPI;
using BigAmbitions.Mods;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsOptions
    {
        private const string UiKey = "developer_tools_ui_hotkey";
        private const string MoneyKey = "developer_tools_money_hotkey";
        private const string NeedsKey = "developer_tools_needs_hotkey";
        private ModContext? context;
        private string? registeredModId;

        public void Initialize(ModContext modContext, DeveloperToolsSettings settings)
        {
            context = modContext;
            settings.UiHotkeyIndex = Load(modContext.ModId, UiKey, settings.UiHotkeyIndex);
            settings.MoneyHotkeyIndex = Load(modContext.ModId, MoneyKey, settings.MoneyHotkeyIndex);
            settings.NeedsHotkeyIndex = Load(modContext.ModId, NeedsKey, settings.NeedsHotkeyIndex);

            OptionsService.RemoveModOptions(modContext.ModId);
            var options = new ModOptions()
                .AddHeader("developer_tools_options_header")
                .AddDropdown(UiKey, "developer_tools_ui_hotkey_label", DeveloperToolsHotkeys.Choices,
                    settings.UiHotkeyIndex, value => Save(modContext.ModId, UiKey, settings.UiHotkeyIndex = DeveloperToolsHotkeys.Clamp(value)))
                .AddDropdown(MoneyKey, "developer_tools_money_hotkey_label", DeveloperToolsHotkeys.Choices,
                    settings.MoneyHotkeyIndex, value => Save(modContext.ModId, MoneyKey, settings.MoneyHotkeyIndex = DeveloperToolsHotkeys.Clamp(value)))
                .AddDropdown(NeedsKey, "developer_tools_needs_hotkey_label", DeveloperToolsHotkeys.Choices,
                    settings.NeedsHotkeyIndex, value => Save(modContext.ModId, NeedsKey, settings.NeedsHotkeyIndex = DeveloperToolsHotkeys.Clamp(value)));

            OptionsService.Register(modContext.ModId, options);
            registeredModId = modContext.ModId;
            modContext.Logger.Info("DeveloperTools: configurable hotkeys registered in mod options.");
        }

        public void Shutdown()
        {
            if (context != null && !string.IsNullOrEmpty(registeredModId))
                OptionsService.RemoveModOptions(registeredModId);
            registeredModId = null;
            context = null;
        }

        private static string PreferenceKey(string modId, string key) => modId + "." + key;

        private static int Load(string modId, string key, int fallback)
        {
            var preferenceKey = PreferenceKey(modId, key);
            return DeveloperToolsHotkeys.Clamp(UnityEngine.PlayerPrefs.HasKey(preferenceKey)
                ? UnityEngine.PlayerPrefs.GetInt(preferenceKey, fallback)
                : fallback);
        }

        private static void Save(string modId, string key, int value)
        {
            UnityEngine.PlayerPrefs.SetInt(PreferenceKey(modId, key), value);
            UnityEngine.PlayerPrefs.Save();
        }
    }
}
