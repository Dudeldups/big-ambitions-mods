#nullable enable
using System.Collections.Generic;

namespace HQCentral.Model
{
    public sealed class HQCentralHrPlan
    {
        public object? VanillaPlan { get; set; }
        public string AssignedManagerName { get; set; } = "Unassigned";
        public int AssignedEmployeeCount { get; set; }
        public int MaxEmployeeCount { get; set; }
        public string Status { get; set; } = "Unknown";
    }

    public sealed class HQCentralHeadhunterPlan
    {
        public object? VanillaPlan { get; set; }
        public string AssignedHeadhunterName { get; set; } = "Unassigned";
        public int CandidateCount { get; set; }
        public bool AutomaticReplacementEnabled { get; set; }
        public string NextRecruitDayText { get; set; } = "Not recruiting";
        public string Status { get; set; } = "Unknown";
    }

    public sealed class HQCentralLogisticsPlan
    {
        public object? VanillaPlan { get; set; }
        public string HeadquartersName { get; set; } = "Unknown headquarters";
        public string HeadquartersAddress { get; set; } = "Unknown";
        public string OriginName { get; set; } = "Unassigned";
        public string OriginAddress { get; set; } = "Unassigned";
        public string AssignedManagerName { get; set; } = "Unassigned";
        public bool IsFactory { get; set; }
        public string Status { get; set; } = "Unknown";
        public List<HQCentralLogisticsDestination> Destinations { get; } = new List<HQCentralLogisticsDestination>();
    }

    public sealed class HQCentralLogisticsDestination
    {
        public object? VanillaDestination { get; set; }
        public string DestinationAddress { get; set; } = "Unassigned";
        public string BusinessName { get; set; } = "Unassigned";
        public string Status { get; set; } = "Unknown";
        public int MinBoxes { get; set; }
        public int MaxBoxes { get; set; }
        public int PlannedDeliveries { get; set; }
    }

    public sealed class HQCentralPurchasingPlan
    {
        public object? VanillaPlan { get; set; }
        public string AssignedPurchasingAgentName { get; set; } = "Unassigned";
        public int ProductCount { get; set; }
        public int PartnershipCount { get; set; }
        public string Status { get; set; } = "Unknown";
    }

    public sealed class HQCentralIssue
    {
        public string Severity { get; set; } = "Info";
        public string Category { get; set; } = "General";
        public string HeadquartersAddress { get; set; } = "Unknown";
        public string Message { get; set; } = string.Empty;
    }
}
