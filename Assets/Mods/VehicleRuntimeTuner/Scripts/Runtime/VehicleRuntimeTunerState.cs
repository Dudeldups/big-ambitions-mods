#nullable enable
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.UI;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Runtime
{
    public sealed class VehicleRuntimeTunerState
    {
        public bool OverlayVisible { get; set; }
        public bool OverlayTextFieldFocused { get; set; }
        public ActiveVehicleInfo? ActiveVehicle { get; set; }
        public VehicleTuningProfile CurrentProfile { get; set; } = VehicleTuningProfile.CreateDefault();
        public VehicleRuntimeTunerFieldBuffer FieldBuffer { get; } = new VehicleRuntimeTunerFieldBuffer();
        public VehicleRuntimeTunerDefaultValues DefaultValues { get; } = new VehicleRuntimeTunerDefaultValues();
        public VehicleRuntimeTunerStatusMessage StatusMessage { get; } = new VehicleRuntimeTunerStatusMessage();
    }
}
