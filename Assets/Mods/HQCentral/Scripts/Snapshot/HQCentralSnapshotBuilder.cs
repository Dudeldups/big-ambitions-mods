#nullable enable
using System;
using System.Collections.Generic;
using Buildings.Office.Headquarters;
using Entities;
using Extensions;
using HQCentral.Discovery;
using HQCentral.Model;
using Streets;

namespace HQCentral.Snapshot
{
    internal sealed class HQCentralSnapshotBuilder
    {
        public HQCentralSnapshot Build(HQDiscoveryResult discovery)
        {
            var snapshot = new HQCentralSnapshot();
            if (!discovery.SaveLoaded)
            {
                snapshot.Issues.Add(CreateIssue("Warning", "Save", "Unknown", "No active save game was found."));
                return snapshot;
            }

            var headquartersByAddress = new Dictionary<string, HQCentralHeadquarters>(StringComparer.Ordinal);
            var employeesById = IndexEmployees(discovery.Employees);
            var buildingsByAddress = IndexBuildings(discovery.Buildings);

            foreach (var building in discovery.Headquarters)
            {
                var address = FormatAddress(building.Address);
                var headquarters = new HQCentralHeadquarters
                {
                    BuildingRegistration = building,
                    Address = address,
                    DisplayName = ValueOrUnknown(building.BusinessName, "Unnamed headquarters"),
                    BusinessTypeName = ValueOrUnknown(building.businessTypeName)
                };

                snapshot.Headquarters.Add(headquarters);
                headquartersByAddress[AddressKey(building.Address)] = headquarters;
            }

            foreach (var employee in discovery.Employees)
            {
                if (employee == null || !headquartersByAddress.TryGetValue(AddressKey(employee.assignedAddress), out var headquarters))
                    continue;

                var employeeSnapshot = BuildEmployee(employee, headquarters, buildingsByAddress);
                headquarters.Employees.Add(employeeSnapshot);
                CountManagerRole(snapshot, employeeSnapshot.Role);
            }

            foreach (var plan in discovery.HrPlans)
                AddHrPlan(snapshot, headquartersByAddress, employeesById, plan);

            foreach (var plan in discovery.HeadhunterPlans)
                AddHeadhunterPlan(snapshot, headquartersByAddress, employeesById, plan);

            foreach (var plan in discovery.LogisticsPlans)
                AddLogisticsPlan(snapshot, headquartersByAddress, employeesById, buildingsByAddress, plan);

            foreach (var plan in discovery.PurchasingPlans)
                AddPurchasingPlan(snapshot, headquartersByAddress, employeesById, plan);

            snapshot.TotalHeadquarters = snapshot.Headquarters.Count;
            foreach (var headquarters in snapshot.Headquarters)
            {
                snapshot.TotalEmployees += headquarters.Employees.Count;
                if (headquarters.Employees.Count == 0)
                {
                    snapshot.Issues.Add(CreateIssue(
                        "Info",
                        "Employees",
                        headquarters.Address,
                        "This headquarters has no directly assigned employees."));
                }
            }

            if (snapshot.TotalHeadquarters == 0)
                snapshot.Issues.Add(CreateIssue("Info", "Headquarters", "Unknown", "No player headquarters were detected."));

            return snapshot;
        }

        private static HQCentralEmployee BuildEmployee(
            EmployeeInstance employee,
            HQCentralHeadquarters headquarters,
            Dictionary<string, BuildingRegistration> buildingsByAddress)
        {
            var primarySkill = employee.characterData?.skills != null && employee.characterData.skills.Count > 0
                ? employee.characterData.skills[0]
                : null;
            var assignedBusiness = buildingsByAddress.TryGetValue(AddressKey(employee.assignedAddress), out var building)
                ? ValueOrUnknown(building.BusinessName)
                : "Unknown";

            return new HQCentralEmployee
            {
                VanillaEmployee = employee,
                Id = ValueOrUnknown(employee.id),
                Name = ValueOrUnknown(employee.characterData?.name),
                Role = FormatRole(primarySkill?.name),
                AssignedBusiness = assignedBusiness,
                AssignedHeadquarters = headquarters.DisplayName,
                Skill = primarySkill?.value ?? 0f,
                Salary = employee.hourlyWage,
                TrainingState = employee.trainingSession == null
                    ? "None"
                    : "Training " + FormatRole(employee.trainingSession.skill),
                Status = GetEmployeeStatus(employee)
            };
        }

        private static void AddHrPlan(
            HQCentralSnapshot snapshot,
            Dictionary<string, HQCentralHeadquarters> headquartersByAddress,
            Dictionary<string, EmployeeInstance> employeesById,
            HrManagerPlan plan)
        {
            if (plan == null || !TryGetHeadquarters(snapshot, headquartersByAddress, plan.headquartersAddress, "HR plan", out var headquarters))
                return;

            var managerName = GetEmployeeName(employeesById, plan.assignedEmployeeId);
            var assignedCount = plan.assignedEmployees?.Count ?? 0;
            var maxCount = plan.MaxEmployees;
            var status = string.IsNullOrEmpty(plan.assignedEmployeeId)
                ? "Unassigned"
                : assignedCount > maxCount ? "Over capacity" : "Active";

            headquarters.HrPlans.Add(new HQCentralHrPlan
            {
                VanillaPlan = plan,
                AssignedManagerName = managerName,
                AssignedEmployeeCount = assignedCount,
                MaxEmployeeCount = maxCount,
                Status = status
            });

            if (assignedCount > maxCount)
                snapshot.Issues.Add(CreateIssue("Warning", "HR capacity", headquarters.Address, managerName + " is over capacity."));
        }

        private static void AddHeadhunterPlan(
            HQCentralSnapshot snapshot,
            Dictionary<string, HQCentralHeadquarters> headquartersByAddress,
            Dictionary<string, EmployeeInstance> employeesById,
            HeadhunterPlan plan)
        {
            if (plan == null || !TryGetHeadquarters(snapshot, headquartersByAddress, plan.headquartersAddress, "headhunter plan", out var headquarters))
                return;

            var candidatesFound = plan.amountOfCandidatesToRecruitPreference >= 0 && plan.remainingCandidatesToRecruit >= 0
                ? Math.Max(0, plan.amountOfCandidatesToRecruitPreference - plan.remainingCandidatesToRecruit)
                : 0;

            headquarters.HeadhunterPlans.Add(new HQCentralHeadhunterPlan
            {
                VanillaPlan = plan,
                AssignedHeadhunterName = GetEmployeeName(employeesById, plan.assignedEmployeeId),
                CandidateCount = candidatesFound,
                AutomaticReplacementEnabled = plan.automaticallyReplaceOnResign || plan.automaticallyReplaceOnRetire,
                NextRecruitDayText = plan.isRecruiting
                    ? $"Day {plan.nextRecruit.Day}, {plan.nextRecruit.Hour:00}:00"
                    : "Not recruiting",
                Status = string.IsNullOrEmpty(plan.assignedEmployeeId)
                    ? "Unassigned"
                    : plan.isRecruiting ? "Recruiting" : "Idle"
            });
        }

        private static void AddLogisticsPlan(
            HQCentralSnapshot snapshot,
            Dictionary<string, HQCentralHeadquarters> headquartersByAddress,
            Dictionary<string, EmployeeInstance> employeesById,
            Dictionary<string, BuildingRegistration> buildingsByAddress,
            LogisticsManagerPlan plan)
        {
            if (plan == null || !TryGetHeadquarters(snapshot, headquartersByAddress, plan.headquartersAddress, "logistics plan", out var headquarters))
                return;

            var logisticsPlan = new HQCentralLogisticsPlan
            {
                VanillaPlan = plan,
                HeadquartersName = headquarters.DisplayName,
                HeadquartersAddress = headquarters.Address,
                OriginName = buildingsByAddress.TryGetValue(AddressKey(plan.targetAddress), out var originBuilding)
                    ? ValueOrUnknown(originBuilding.BusinessName)
                    : "Unassigned",
                OriginAddress = FormatAddress(plan.targetAddress),
                AssignedManagerName = GetEmployeeName(employeesById, plan.assignedEmployeeId),
                IsFactory = plan.isFactory,
                Status = string.IsNullOrEmpty(plan.assignedEmployeeId)
                    ? "Unassigned manager"
                    : plan.targetAddress == null ? "Unassigned origin" : "Active"
            };

            foreach (var destination in plan.destinations)
            {
                if (destination == null)
                    continue;

                var targetAmount = 0;
                foreach (var stockTarget in destination.stockTargets)
                {
                    if (stockTarget != null && stockTarget.targetAmount > 0)
                        targetAmount += stockTarget.targetAmount;
                }

                var destinationName = buildingsByAddress.TryGetValue(AddressKey(destination.deliveryTargetAddress), out var building)
                    ? ValueOrUnknown(building.BusinessName)
                    : "Unassigned";
                logisticsPlan.Destinations.Add(new HQCentralLogisticsDestination
                {
                    VanillaDestination = destination,
                    DestinationAddress = FormatAddress(destination.deliveryTargetAddress),
                    BusinessName = destinationName,
                    Status = destination.deliveryTargetAddress == null
                        ? "Unassigned"
                        : destination.stockTargets.Count == 0 ? "No products" : "Configured",
                    MinBoxes = 0,
                    MaxBoxes = targetAmount,
                    PlannedDeliveries = destination.stockTargets.Count
                });
            }

            headquarters.LogisticsPlans.Add(logisticsPlan);
        }

        private static void AddPurchasingPlan(
            HQCentralSnapshot snapshot,
            Dictionary<string, HQCentralHeadquarters> headquartersByAddress,
            Dictionary<string, EmployeeInstance> employeesById,
            ImportPartnership plan)
        {
            if (plan == null || !TryGetHeadquarters(snapshot, headquartersByAddress, plan.headquartersAddress, "purchasing plan", out var headquarters))
                return;

            headquarters.PurchasingPlans.Add(new HQCentralPurchasingPlan
            {
                VanillaPlan = plan,
                AssignedPurchasingAgentName = GetEmployeeName(employeesById, plan.employeeInstanceId),
                ProductCount = plan.products?.Count ?? 0,
                PartnershipCount = 1,
                Status = string.IsNullOrEmpty(plan.employeeInstanceId)
                    ? "Unassigned"
                    : plan.isActive ? "Active" : "Inactive"
            });
        }

        private static bool TryGetHeadquarters(
            HQCentralSnapshot snapshot,
            Dictionary<string, HQCentralHeadquarters> headquartersByAddress,
            Address? address,
            string category,
            out HQCentralHeadquarters headquarters)
        {
            if (headquartersByAddress.TryGetValue(AddressKey(address), out headquarters!))
                return true;

            snapshot.Issues.Add(CreateIssue(
                "Warning",
                "Orphaned plan",
                FormatAddress(address),
                "A " + category + " could not be matched to a detected headquarters."));
            return false;
        }

        private static Dictionary<string, EmployeeInstance> IndexEmployees(List<EmployeeInstance> employees)
        {
            var result = new Dictionary<string, EmployeeInstance>(StringComparer.Ordinal);
            foreach (var employee in employees)
            {
                if (employee != null && !string.IsNullOrEmpty(employee.id))
                    result[employee.id] = employee;
            }

            return result;
        }

        private static Dictionary<string, BuildingRegistration> IndexBuildings(List<BuildingRegistration> buildings)
        {
            var result = new Dictionary<string, BuildingRegistration>(StringComparer.Ordinal);
            foreach (var building in buildings)
            {
                if (building != null)
                    result[AddressKey(building.Address)] = building;
            }

            return result;
        }

        private static string GetEmployeeName(Dictionary<string, EmployeeInstance> employeesById, string? employeeId)
        {
            if (string.IsNullOrEmpty(employeeId))
                return "Unassigned";

            return employeesById.TryGetValue(employeeId, out var employee)
                ? ValueOrUnknown(employee.characterData?.name)
                : "Unknown employee (" + employeeId + ")";
        }

        private static string GetEmployeeStatus(EmployeeInstance employee)
        {
            if (employee.isBeingReplaced)
                return "Being replaced";
            if (employee.isAbsent)
                return "Absent";
            return employee.IsEmployeeAvailable() ? "Available" : "Unavailable";
        }

        private static void CountManagerRole(HQCentralSnapshot snapshot, string role)
        {
            switch (role)
            {
                case "HR Manager":
                    snapshot.TotalHrManagers++;
                    break;
                case "Headhunter":
                    snapshot.TotalHeadhunters++;
                    break;
                case "Logistics Manager":
                    snapshot.TotalLogisticsManagers++;
                    break;
                case "Purchasing Agent":
                    snapshot.TotalPurchasingAgents++;
                    break;
            }
        }

        private static string FormatRole(string? skillName)
        {
            switch (skillName)
            {
                case "ba:skill_hrmanager":
                    return "HR Manager";
                case "ba:skill_headhunter":
                    return "Headhunter";
                case "ba:skill_logisticsmanager":
                    return "Logistics Manager";
                case "ba:skill_purchasingagent":
                    return "Purchasing Agent";
                case null:
                case "":
                    return "Unknown";
                default:
                    return skillName.Replace("ba:skill_", string.Empty).Replace('_', ' ');
            }
        }

        private static string AddressKey(Address? address)
        {
            return address == null ? "<null>" : address.ToFormattedString();
        }

        private static string FormatAddress(Address? address)
        {
            return address == null ? "Unassigned" : address.ToFormattedString();
        }

        private static string ValueOrUnknown(string? value, string fallback = "Unknown")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static HQCentralIssue CreateIssue(string severity, string category, string headquartersAddress, string message)
        {
            return new HQCentralIssue
            {
                Severity = severity,
                Category = category,
                HeadquartersAddress = headquartersAddress,
                Message = message
            };
        }
    }
}
