#nullable enable
using HQCentral.Model;
using UnityEngine;

namespace HQCentral.UI
{
    internal sealed partial class HQCentralWindow
    {
        private void DrawLogisticsView()
        {
            DrawLogisticsFilters();
            GUILayout.BeginHorizontal();
            DrawLogisticsPlanList();
            DrawSelectedLogisticsPlan();
            GUILayout.EndHorizontal();
        }

        private void DrawLogisticsFilters()
        {
            GUILayout.BeginHorizontal();
            DrawFilterButton("All", LogisticsFilter.All);
            DrawFilterButton("Warehouses", LogisticsFilter.Warehouses);
            DrawFilterButton("Factories", LogisticsFilter.Factories);
            GUILayout.EndHorizontal();
        }

        private void DrawFilterButton(string label, LogisticsFilter filter)
        {
            GUI.enabled = logisticsFilter != filter;
            if (GUILayout.Button(label, GUILayout.Height(28f)))
            {
                logisticsFilter = filter;
                logisticsListScrollPosition = Vector2.zero;
                if (selectedLogisticsPlan != null && !MatchesLogisticsFilter(selectedLogisticsPlan))
                    selectedLogisticsPlan = null;
            }

            GUI.enabled = true;
        }

        private void DrawLogisticsPlanList()
        {
            GUILayout.BeginVertical(GUILayout.Width(390f));
            GUILayout.Label("Managers and origins", GUI.skin.box);
            logisticsListScrollPosition = GUILayout.BeginScrollView(logisticsListScrollPosition);

            var visibleCount = 0;
            foreach (var plan in logisticsPlans)
            {
                if (!MatchesLogisticsFilter(plan))
                    continue;

                visibleCount++;
                var selected = ReferenceEquals(selectedLogisticsPlan, plan);
                GUI.enabled = !selected;
                var label =
                    $"{plan.AssignedManagerName}\n" +
                    $"{(plan.IsFactory ? "Factory" : "Warehouse")}: {plan.OriginName}\n" +
                    $"HQ: {plan.HeadquartersName} | Destinations: {plan.Destinations.Count}";
                if (GUILayout.Button(label, GUILayout.MinHeight(66f)))
                {
                    selectedLogisticsPlan = plan;
                    logisticsDetailsScrollPosition = Vector2.zero;
                    logisticsPlanSelectedAction?.Invoke(plan);
                }

                GUI.enabled = true;
            }

            if (visibleCount == 0)
                GUILayout.Label("No logistics plans match this filter.");

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawSelectedLogisticsPlan()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("Plan details", GUI.skin.box);
            logisticsDetailsScrollPosition = GUILayout.BeginScrollView(logisticsDetailsScrollPosition);

            if (selectedLogisticsPlan == null)
            {
                GUILayout.Label("Select a logistics manager to inspect the read-only plan.");
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                return;
            }

            var plan = selectedLogisticsPlan;
            GUILayout.Label($"Manager: {plan.AssignedManagerName}");
            GUILayout.Label($"Headquarters: {plan.HeadquartersName} ({plan.HeadquartersAddress})");
            GUILayout.Label($"Type: {(plan.IsFactory ? "Factory" : "Warehouse")}");
            GUILayout.Label($"Origin: {plan.OriginName} ({plan.OriginAddress})");
            GUILayout.Label($"Status: {plan.Status}");
            GUILayout.Space(8f);
            GUILayout.Label($"Destinations ({plan.Destinations.Count})", GUI.skin.box);

            foreach (var destination in plan.Destinations)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(destination.BusinessName);
                GUILayout.Label($"Address: {destination.DestinationAddress}");
                GUILayout.Label(
                    $"Product targets: {destination.PlannedDeliveries} | Target boxes: {destination.MaxBoxes} | " +
                    $"Status: {destination.Status}");
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private bool MatchesLogisticsFilter(HQCentralLogisticsPlan plan)
        {
            switch (logisticsFilter)
            {
                case LogisticsFilter.Warehouses:
                    return !plan.IsFactory;
                case LogisticsFilter.Factories:
                    return plan.IsFactory;
                default:
                    return true;
            }
        }
    }
}
