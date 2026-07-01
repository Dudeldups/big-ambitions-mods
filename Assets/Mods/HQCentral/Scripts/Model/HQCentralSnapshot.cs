#nullable enable
using System.Collections.Generic;

namespace HQCentral.Model
{
    public sealed class HQCentralSnapshot
    {
        public List<HQCentralHeadquarters> Headquarters { get; } = new List<HQCentralHeadquarters>();
        public List<HQCentralIssue> Issues { get; } = new List<HQCentralIssue>();

        public int TotalHeadquarters { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalHrManagers { get; set; }
        public int TotalHeadhunters { get; set; }
        public int TotalLogisticsManagers { get; set; }
        public int TotalPurchasingAgents { get; set; }
    }
}
