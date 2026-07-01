#nullable enable
using HQCentral.Model;
using UnityEngine;

namespace HQCentral.UI
{
    internal sealed partial class HQCentralWindow
    {
        private void DrawOverview(HQCentralSnapshot currentSnapshot)
        {
            if (currentSnapshot.Headquarters.Count == 0)
            {
                GUILayout.Space(15f);
                GUILayout.Label("No headquarters detected. See HQCentral-data.log for discovery details.");
                return;
            }

            DrawHeadquartersSelector(currentSnapshot);
            var headquarters = currentSnapshot.Headquarters[selectedHeadquartersIndex];

            overviewScrollPosition = GUILayout.BeginScrollView(overviewScrollPosition);
            GUILayout.Label($"{headquarters.DisplayName} - {headquarters.Address}", GUI.skin.box);
            GUILayout.Label($"Type: {headquarters.BusinessTypeName}");

            DrawEmployees(headquarters);
            DrawHrPlans(headquarters);
            DrawHeadhunterPlans(headquarters);
            DrawOverviewLogisticsPlans(headquarters);
            DrawPurchasingPlans(headquarters);
            DrawIssues(currentSnapshot, headquarters.Address);
            GUILayout.EndScrollView();
        }

        private void DrawHeadquartersSelector(HQCentralSnapshot currentSnapshot)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = selectedHeadquartersIndex > 0;
            if (GUILayout.Button("<", GUILayout.Width(45f)))
            {
                selectedHeadquartersIndex--;
                overviewScrollPosition = Vector2.zero;
            }

            GUI.enabled = true;
            GUILayout.Label(
                $"Headquarters {selectedHeadquartersIndex + 1} of {currentSnapshot.Headquarters.Count}: " +
                currentSnapshot.Headquarters[selectedHeadquartersIndex].DisplayName,
                GUILayout.ExpandWidth(true));
            GUI.enabled = selectedHeadquartersIndex < currentSnapshot.Headquarters.Count - 1;
            if (GUILayout.Button(">", GUILayout.Width(45f)))
            {
                selectedHeadquartersIndex++;
                overviewScrollPosition = Vector2.zero;
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private static void DrawEmployees(HQCentralHeadquarters headquarters)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Employees ({headquarters.Employees.Count})", GUI.skin.box);
            if (headquarters.Employees.Count == 0)
                GUILayout.Label("No directly assigned employees.");

            foreach (var employee in headquarters.Employees)
            {
                GUILayout.Label(
                    $"- {employee.Name} - {employee.Role} - skill {employee.Skill:0.#}% - " +
                    $"${employee.Salary:0.00}/h - {employee.Status} - {employee.TrainingState}");
            }
        }

        private static void DrawHrPlans(HQCentralHeadquarters headquarters)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"HR plans ({headquarters.HrPlans.Count})", GUI.skin.box);
            foreach (var plan in headquarters.HrPlans)
                GUILayout.Label($"- {plan.AssignedManagerName} - employees {plan.AssignedEmployeeCount}/{plan.MaxEmployeeCount} - {plan.Status}");
        }

        private static void DrawHeadhunterPlans(HQCentralHeadquarters headquarters)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Headhunter plans ({headquarters.HeadhunterPlans.Count})", GUI.skin.box);
            foreach (var plan in headquarters.HeadhunterPlans)
            {
                GUILayout.Label(
                    $"- {plan.AssignedHeadhunterName} - candidates {plan.CandidateCount} - {plan.Status} - " +
                    $"next: {plan.NextRecruitDayText} - auto-replace: {(plan.AutomaticReplacementEnabled ? "yes" : "no")}");
            }
        }

        private static void DrawOverviewLogisticsPlans(HQCentralHeadquarters headquarters)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Logistics plans ({headquarters.LogisticsPlans.Count})", GUI.skin.box);
            foreach (var plan in headquarters.LogisticsPlans)
            {
                GUILayout.Label(
                    $"- {plan.AssignedManagerName} - {(plan.IsFactory ? "Factory" : "Warehouse")} " +
                    $"{plan.OriginName} ({plan.OriginAddress}) - {plan.Destinations.Count} destinations - {plan.Status}");
            }
        }

        private static void DrawPurchasingPlans(HQCentralHeadquarters headquarters)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Purchasing plans ({headquarters.PurchasingPlans.Count})", GUI.skin.box);
            foreach (var plan in headquarters.PurchasingPlans)
                GUILayout.Label($"- {plan.AssignedPurchasingAgentName} - {plan.ProductCount} products - {plan.Status}");
        }

        private static void DrawIssues(HQCentralSnapshot currentSnapshot, string headquartersAddress)
        {
            var headingDrawn = false;
            foreach (var issue in currentSnapshot.Issues)
            {
                if (issue.HeadquartersAddress != headquartersAddress && issue.HeadquartersAddress != "Unknown")
                    continue;

                if (!headingDrawn)
                {
                    GUILayout.Space(8f);
                    GUILayout.Label("Issues", GUI.skin.box);
                    headingDrawn = true;
                }

                GUILayout.Label($"- [{issue.Severity}] {issue.Category}: {issue.Message}");
            }
        }
    }
}
