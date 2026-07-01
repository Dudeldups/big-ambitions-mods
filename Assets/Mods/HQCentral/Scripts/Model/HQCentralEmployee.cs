#nullable enable

namespace HQCentral.Model
{
    public sealed class HQCentralEmployee
    {
        public object? VanillaEmployee { get; set; }
        public string Id { get; set; } = "Unknown";
        public string Name { get; set; } = "Unknown";
        public string Role { get; set; } = "Unknown";
        public string AssignedBusiness { get; set; } = "Unknown";
        public string AssignedHeadquarters { get; set; } = "Unknown";
        public float Skill { get; set; }
        public float Salary { get; set; }
        public string TrainingState { get; set; } = "None";
        public string Status { get; set; } = "Unknown";
    }
}
