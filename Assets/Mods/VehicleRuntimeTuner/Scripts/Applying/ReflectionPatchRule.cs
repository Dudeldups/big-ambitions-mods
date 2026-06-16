#nullable enable
using System;
using VehicleRuntimeTuner.Profiles;

namespace VehicleRuntimeTuner.Applying
{
    public sealed class ReflectionPatchRule
    {
        public string displayName = string.Empty;
        public string componentTypeNameContains = string.Empty;
        public string memberName = string.Empty;
        public string wheelGroup = string.Empty;
        public Func<VehicleTuningProfile, float?>? readValue;
    }
}
