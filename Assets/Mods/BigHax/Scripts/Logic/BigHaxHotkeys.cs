#nullable enable
namespace BigHax
{
    internal static class BigHaxHotkeys
    {
        public static readonly string[] ChoiceKeys =
        {
            "bighax_hotkey_f5",
            "bighax_hotkey_f6",
            "bighax_hotkey_f7",
            "bighax_hotkey_f8",
            "bighax_hotkey_home",
            "bighax_hotkey_insert",
            "bighax_hotkey_delete"
        };

        public static readonly UnityEngine.KeyCode[] Values =
        {
            UnityEngine.KeyCode.F5,
            UnityEngine.KeyCode.F6,
            UnityEngine.KeyCode.F7,
            UnityEngine.KeyCode.F8,
            UnityEngine.KeyCode.Home,
            UnityEngine.KeyCode.Insert,
            UnityEngine.KeyCode.Delete
        };

        public static UnityEngine.KeyCode GetKeyCode(int index)
        {
            if (index < 0 || index >= Values.Length)
                return Values[BigHaxSettings.DefaultUiHotkeyIndex];

            return Values[index];
        }

        public static int ClampIndex(int index)
        {
            if (index < 0 || index >= Values.Length)
                return BigHaxSettings.DefaultUiHotkeyIndex;

            return index;
        }
    }
}
