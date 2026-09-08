#nullable enable
using UnityEngine;

namespace DeveloperTools
{
    public sealed class DeveloperToolsSettings
    {
        public int UiHotkeyIndex { get; set; } = 7;
        public int MoneyHotkeyIndex { get; set; } = 0;
        public int NeedsHotkeyIndex { get; set; } = 0;

        public KeyCode UiHotkey => DeveloperToolsHotkeys.GetKeyCode(UiHotkeyIndex);
        public KeyCode MoneyHotkey => DeveloperToolsHotkeys.GetKeyCode(MoneyHotkeyIndex);
        public KeyCode NeedsHotkey => DeveloperToolsHotkeys.GetKeyCode(NeedsHotkeyIndex);
    }

    internal static class DeveloperToolsHotkeys
    {
        public static readonly string[] Choices =
        {
            "Disabled", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Home", "Insert", "Delete"
        };

        private static readonly KeyCode[] Values =
        {
            KeyCode.None, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9,
            KeyCode.F10, KeyCode.F11, KeyCode.F12, KeyCode.Home, KeyCode.Insert, KeyCode.Delete
        };

        public static int Clamp(int index) => index >= 0 && index < Values.Length ? index : 0;
        public static KeyCode GetKeyCode(int index) => Values[Clamp(index)];
    }
}
