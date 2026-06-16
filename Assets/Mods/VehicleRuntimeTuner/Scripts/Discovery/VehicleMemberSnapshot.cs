#nullable enable

namespace VehicleRuntimeTuner.Discovery
{
    public sealed class VehicleMemberSnapshot
    {
        public string ComponentPath { get; set; } = string.Empty;
        public string ComponentTypeName { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string ValueText { get; set; } = string.Empty;
    }
}
