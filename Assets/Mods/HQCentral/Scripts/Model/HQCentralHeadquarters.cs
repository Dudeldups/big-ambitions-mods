#nullable enable
using System.Collections.Generic;

namespace HQCentral.Model
{
    public sealed class HQCentralHeadquarters
    {
        public object? BuildingRegistration { get; set; }
        public string Address { get; set; } = "Unknown";
        public string DisplayName { get; set; } = "Unknown headquarters";
        public string BusinessTypeName { get; set; } = "Unknown";

        public List<HQCentralEmployee> Employees { get; } = new List<HQCentralEmployee>();
        public List<HQCentralHrPlan> HrPlans { get; } = new List<HQCentralHrPlan>();
        public List<HQCentralHeadhunterPlan> HeadhunterPlans { get; } = new List<HQCentralHeadhunterPlan>();
        public List<HQCentralLogisticsPlan> LogisticsPlans { get; } = new List<HQCentralLogisticsPlan>();
        public List<HQCentralPurchasingPlan> PurchasingPlans { get; } = new List<HQCentralPurchasingPlan>();
    }
}
