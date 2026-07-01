#nullable enable
using System.Collections.Generic;
using Buildings.Office.Headquarters;
using Entities;

namespace HQCentral.Discovery
{
    internal sealed class HQDiscoveryResult
    {
        public bool SaveLoaded { get; set; }
        public bool IsHeadquartersPropertyAvailable { get; set; }
        public List<BuildingRegistration> Buildings { get; } = new List<BuildingRegistration>();
        public List<BuildingRegistration> Headquarters { get; } = new List<BuildingRegistration>();
        public List<HQCandidateDiagnostic> Candidates { get; } = new List<HQCandidateDiagnostic>();
        public List<EmployeeInstance> Employees { get; } = new List<EmployeeInstance>();
        public List<HrManagerPlan> HrPlans { get; } = new List<HrManagerPlan>();
        public List<HeadhunterPlan> HeadhunterPlans { get; } = new List<HeadhunterPlan>();
        public List<LogisticsManagerPlan> LogisticsPlans { get; } = new List<LogisticsManagerPlan>();
        public List<ImportPartnership> PurchasingPlans { get; } = new List<ImportPartnership>();
        public List<string> KnownUiTypes { get; } = new List<string>();
    }

    internal sealed class HQCandidateDiagnostic
    {
        public string Address { get; set; } = "Unknown";
        public string DisplayName { get; set; } = "Unknown";
        public string BusinessTypeName { get; set; } = "Unknown";
        public bool IsHeadquarters { get; set; }
        public string DetectionSource { get; set; } = "None";
    }
}
