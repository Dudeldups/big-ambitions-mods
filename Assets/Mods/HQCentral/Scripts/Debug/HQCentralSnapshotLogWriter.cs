#nullable enable
using System;
using System.Text;
using HQCentral.Discovery;
using HQCentral.Model;

namespace HQCentral.Debugging
{
    internal static class HQCentralSnapshotLogWriter
    {
        public static void Write(HQDiscoveryResult discovery, HQCentralSnapshot snapshot, string reason)
        {
            var builder = new StringBuilder(16 * 1024);
            builder.AppendLine("============================================================");
            builder.AppendLine($"HQCentral data snapshot: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            builder.AppendLine($"Reason: {reason}");
            builder.AppendLine($"Save loaded: {discovery.SaveLoaded}");
            builder.AppendLine($"IsHeadquarters property available: {discovery.IsHeadquartersPropertyAvailable}");
            builder.AppendLine(
                $"Vanilla counts: buildings={discovery.Buildings.Count}, employees={discovery.Employees.Count}, " +
                $"hrPlans={discovery.HrPlans.Count}, headhunterPlans={discovery.HeadhunterPlans.Count}, " +
                $"logisticsPlans={discovery.LogisticsPlans.Count}, purchasingPlans={discovery.PurchasingPlans.Count}");
            builder.AppendLine(
                $"Snapshot totals: headquarters={snapshot.TotalHeadquarters}, employees={snapshot.TotalEmployees}, " +
                $"hrManagers={snapshot.TotalHrManagers}, headhunters={snapshot.TotalHeadhunters}, " +
                $"logisticsManagers={snapshot.TotalLogisticsManagers}, purchasingAgents={snapshot.TotalPurchasingAgents}, " +
                $"issues={snapshot.Issues.Count}");

            builder.AppendLine("HQ candidates:");
            if (discovery.Candidates.Count == 0)
                builder.AppendLine("  <none>");
            foreach (var candidate in discovery.Candidates)
            {
                builder.AppendLine(
                    $"  name={candidate.DisplayName}; address={candidate.Address}; type={candidate.BusinessTypeName}; " +
                    $"isHeadquarters={candidate.IsHeadquarters}; source={candidate.DetectionSource}");
            }

            builder.AppendLine("Known HQ/BizMan UI types (metadata-confirmed, not runtime-scanned):");
            foreach (var typeName in discovery.KnownUiTypes)
                builder.AppendLine("  " + typeName);

            foreach (var headquarters in snapshot.Headquarters)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"HQ name={headquarters.DisplayName}; address={headquarters.Address}; type={headquarters.BusinessTypeName}; " +
                    $"employees={headquarters.Employees.Count}; hrPlans={headquarters.HrPlans.Count}; " +
                    $"headhunterPlans={headquarters.HeadhunterPlans.Count}; logisticsPlans={headquarters.LogisticsPlans.Count}; " +
                    $"purchasingPlans={headquarters.PurchasingPlans.Count}");

                foreach (var employee in headquarters.Employees)
                {
                    builder.AppendLine(
                        $"  Employee id={employee.Id}; name={employee.Name}; role={employee.Role}; skill={employee.Skill:0.#}; " +
                        $"wage={employee.Salary:0.##}; assignedBusiness={employee.AssignedBusiness}; " +
                        $"training={employee.TrainingState}; status={employee.Status}");
                }

                foreach (var plan in headquarters.HrPlans)
                {
                    builder.AppendLine(
                        $"  HR plan manager={plan.AssignedManagerName}; employees={plan.AssignedEmployeeCount}/{plan.MaxEmployeeCount}; " +
                        $"status={plan.Status}");
                }

                foreach (var plan in headquarters.HeadhunterPlans)
                {
                    builder.AppendLine(
                        $"  Headhunter plan manager={plan.AssignedHeadhunterName}; candidates={plan.CandidateCount}; " +
                        $"automaticReplacement={plan.AutomaticReplacementEnabled}; next={plan.NextRecruitDayText}; status={plan.Status}");
                }

                foreach (var plan in headquarters.LogisticsPlans)
                {
                    builder.AppendLine(
                        $"  Logistics plan manager={plan.AssignedManagerName}; originName={plan.OriginName}; origin={plan.OriginAddress}; " +
                        $"kind={(plan.IsFactory ? "Factory" : "Warehouse")}; destinations={plan.Destinations.Count}; status={plan.Status}");
                    foreach (var destination in plan.Destinations)
                    {
                        builder.AppendLine(
                            $"    Destination business={destination.BusinessName}; address={destination.DestinationAddress}; " +
                            $"productTargets={destination.PlannedDeliveries}; targetBoxes={destination.MaxBoxes}; status={destination.Status}");
                    }
                }

                foreach (var plan in headquarters.PurchasingPlans)
                {
                    builder.AppendLine(
                        $"  Purchasing plan manager={plan.AssignedPurchasingAgentName}; products={plan.ProductCount}; " +
                        $"partnerships={plan.PartnershipCount}; status={plan.Status}");
                }
            }

            if (snapshot.Issues.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Issues:");
                foreach (var issue in snapshot.Issues)
                {
                    builder.AppendLine(
                        $"  severity={issue.Severity}; category={issue.Category}; hq={issue.HeadquartersAddress}; message={issue.Message}");
                }
            }

            builder.AppendLine("END HQCentral data snapshot");
            HQCentralFileLogger.AppendDataSnapshot(builder);
        }
    }
}
