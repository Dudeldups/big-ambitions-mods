#nullable enable
using System;
using System.Reflection;
using Extensions;
using Streets;

namespace HQCentral.Discovery
{
    internal sealed class HQDiscoveryService
    {
        private const string HeadquartersBusinessType = "ba:businesstype_headquarters";
        private static readonly PropertyInfo? IsHeadquartersProperty =
            typeof(BuildingRegistration).GetProperty(
                "IsHeadquarters",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public HQDiscoveryResult Discover()
        {
            var result = new HQDiscoveryResult
            {
                IsHeadquartersPropertyAvailable = IsHeadquartersProperty?.PropertyType == typeof(bool)
            };

            var save = SaveGameManager.Current;
            if (save == null)
                return result;

            result.SaveLoaded = true;
            result.Buildings.AddRange(save.BuildingRegistrations);
            result.Employees.AddRange(save.EmployeeInstances);
            result.HrPlans.AddRange(save.hrManagerPlans);
            result.HeadhunterPlans.AddRange(save.headhunterPlans);
            result.LogisticsPlans.AddRange(save.logisticsManagerPlans);
            result.PurchasingPlans.AddRange(save.importPartnerships);

            foreach (var building in result.Buildings)
            {
                if (building == null)
                    continue;

                var isHeadquarters = TryReadIsHeadquarters(building, out var detectionSource);
                var looksLikeHeadquarters = isHeadquarters || LooksLikeHeadquarters(building.businessTypeName);
                if (!looksLikeHeadquarters)
                    continue;

                result.Candidates.Add(new HQCandidateDiagnostic
                {
                    Address = FormatAddress(building.Address),
                    DisplayName = ValueOrUnknown(building.BusinessName),
                    BusinessTypeName = ValueOrUnknown(building.businessTypeName),
                    IsHeadquarters = isHeadquarters,
                    DetectionSource = detectionSource
                });

                if (isHeadquarters)
                    result.Headquarters.Add(building);
            }

            result.KnownUiTypes.Add("UI.Smartphone.Apps.BizMan.HeadquartersList");
            result.KnownUiTypes.Add("UI.Smartphone.Apps.BizMan.HRManagers.HrManagersPlanList");
            result.KnownUiTypes.Add("UI.Smartphone.Apps.BizMan.Headhunters.HeadhuntersPlanList");
            result.KnownUiTypes.Add("UI.Smartphone.Apps.BizMan.LogisticsManagers.LogisticsManagersPlanList");
            result.KnownUiTypes.Add("UI.Smartphone.Apps.BizMan.PurchasingAgentsPlanList");
            return result;
        }

        private static bool TryReadIsHeadquarters(BuildingRegistration building, out string detectionSource)
        {
            if (IsHeadquartersProperty?.PropertyType == typeof(bool))
            {
                try
                {
                    detectionSource = "IsHeadquarters property";
                    return (bool)(IsHeadquartersProperty.GetValue(building) ?? false);
                }
                catch
                {
                    detectionSource = "businessTypeName fallback (property read failed)";
                    return IsHeadquartersBusinessType(building.businessTypeName);
                }
            }

            detectionSource = "businessTypeName fallback (property unavailable)";
            return IsHeadquartersBusinessType(building.businessTypeName);
        }

        private static bool IsHeadquartersBusinessType(string? businessTypeName)
        {
            return string.Equals(businessTypeName, HeadquartersBusinessType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeHeadquarters(string? businessTypeName)
        {
            if (string.IsNullOrWhiteSpace(businessTypeName))
                return false;

            return businessTypeName.IndexOf("headquarter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                businessTypeName.IndexOf("hq", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatAddress(Address? address)
        {
            return address == null ? "Unknown" : address.ToFormattedString();
        }

        private static string ValueOrUnknown(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        }
    }
}
