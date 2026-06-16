#nullable enable
using UnityEngine;

namespace VehicleRuntimeTuner.Runtime
{
    public sealed class VehicleRuntimeTunerHotkeys
    {
        public bool ToggleOverlayPressed()
        {
            return Input.GetKeyDown(KeyCode.F10);
        }

        public bool ApplyPressed()
        {
            return Input.GetKeyDown(KeyCode.F9);
        }

        public bool DumpPressed()
        {
            return Input.GetKeyDown(KeyCode.F8);
        }

        public bool SavePressed()
        {
            return Input.GetKeyDown(KeyCode.F7);
        }

        public bool LoadPressed()
        {
            return Input.GetKeyDown(KeyCode.F6);
        }
    }
}
