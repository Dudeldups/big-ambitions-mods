#nullable enable
using System;
using System.Collections.Generic;
using HQCentral.Model;
using UnityEngine;

namespace HQCentral.UI
{
    internal sealed partial class HQCentralWindow : IDisposable
    {
        private const int WindowId = 0x484143;
        private readonly List<HQCentralLogisticsPlan> logisticsPlans = new List<HQCentralLogisticsPlan>();
        private Rect windowRect;
        private Vector2 overviewScrollPosition;
        private Vector2 logisticsListScrollPosition;
        private Vector2 logisticsDetailsScrollPosition;
        private HQCentralSnapshot? snapshot;
        private HQCentralLogisticsPlan? selectedLogisticsPlan;
        private HeadquartersView view = HeadquartersView.Overview;
        private LogisticsFilter logisticsFilter = LogisticsFilter.All;
        private int selectedHeadquartersIndex;
        private Action? refreshAction;
        private Action? closeAction;
        private Action<HQCentralLogisticsPlan>? logisticsPlanSelectedAction;
        private Texture2D? opaqueBackgroundTexture;
        private GUIStyle? opaqueWindowStyle;

        public bool IsVisible { get; private set; }

        public void Show(HQCentralSnapshot newSnapshot)
        {
            snapshot = newSnapshot;
            selectedHeadquartersIndex = Mathf.Clamp(selectedHeadquartersIndex, 0, Math.Max(0, newSnapshot.Headquarters.Count - 1));
            RebuildLogisticsIndex(newSnapshot);
            windowRect = new Rect(
                Mathf.Max(20f, (Screen.width - 1100f) * 0.5f),
                Mathf.Max(20f, (Screen.height - 800f) * 0.5f),
                Mathf.Min(1100f, Screen.width - 40f),
                Mathf.Min(800f, Screen.height - 40f));
            IsVisible = true;
        }

        public void SetSnapshot(HQCentralSnapshot newSnapshot)
        {
            snapshot = newSnapshot;
            selectedHeadquartersIndex = Mathf.Clamp(selectedHeadquartersIndex, 0, Math.Max(0, newSnapshot.Headquarters.Count - 1));
            overviewScrollPosition = Vector2.zero;
            logisticsListScrollPosition = Vector2.zero;
            logisticsDetailsScrollPosition = Vector2.zero;
            RebuildLogisticsIndex(newSnapshot);
        }

        public void Hide()
        {
            IsVisible = false;
            refreshAction = null;
            closeAction = null;
            logisticsPlanSelectedAction = null;
        }

        public void Dispose()
        {
            Hide();
            if (opaqueBackgroundTexture != null)
                UnityEngine.Object.Destroy(opaqueBackgroundTexture);

            opaqueBackgroundTexture = null;
            opaqueWindowStyle = null;
        }

        public void OnGui(
            Action onRefresh,
            Action onClose,
            Action<HQCentralLogisticsPlan> onLogisticsPlanSelected)
        {
            if (!IsVisible || snapshot == null)
                return;

            refreshAction = onRefresh;
            closeAction = onClose;
            logisticsPlanSelectedAction = onLogisticsPlanSelected;
            EnsureOpaqueWindowStyle();
            windowRect = GUILayout.Window(
                WindowId,
                windowRect,
                DrawWindow,
                "HQ Central - Read-only overview",
                opaqueWindowStyle!);
        }

        private void DrawWindow(int windowId)
        {
            if (snapshot == null)
                return;

            DrawToolbar(snapshot);
            DrawViewSelector();

            if (view == HeadquartersView.Logistics)
                DrawLogisticsView();
            else
                DrawOverview(snapshot);

            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 28f));
        }

        private void DrawToolbar(HQCentralSnapshot currentSnapshot)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"HQs: {currentSnapshot.TotalHeadquarters}   Employees: {currentSnapshot.TotalEmployees}   " +
                $"HR: {currentSnapshot.TotalHrManagers}   Headhunters: {currentSnapshot.TotalHeadhunters}   " +
                $"Logistics: {currentSnapshot.TotalLogisticsManagers}   Purchasing: {currentSnapshot.TotalPurchasingAgents}",
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                refreshAction?.Invoke();
            if (GUILayout.Button("Close", GUILayout.Width(90f)))
                closeAction?.Invoke();
            GUILayout.EndHorizontal();
        }

        private void DrawViewSelector()
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = view != HeadquartersView.Overview;
            if (GUILayout.Button("Headquarters", GUILayout.Height(30f)))
                view = HeadquartersView.Overview;
            GUI.enabled = view != HeadquartersView.Logistics;
            if (GUILayout.Button($"Logistics ({logisticsPlans.Count})", GUILayout.Height(30f)))
                view = HeadquartersView.Logistics;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void EnsureOpaqueWindowStyle()
        {
            if (opaqueWindowStyle != null)
                return;

            opaqueBackgroundTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "HQCentralOpaqueWindowBackground",
                hideFlags = HideFlags.HideAndDontSave
            };
            opaqueBackgroundTexture.SetPixel(0, 0, new Color32(24, 28, 34, 255));
            opaqueBackgroundTexture.Apply(false, true);

            opaqueWindowStyle = new GUIStyle(GUI.skin.window);
            opaqueWindowStyle.normal.background = opaqueBackgroundTexture;
            opaqueWindowStyle.onNormal.background = opaqueBackgroundTexture;
            opaqueWindowStyle.focused.background = opaqueBackgroundTexture;
            opaqueWindowStyle.onFocused.background = opaqueBackgroundTexture;
        }

        private void RebuildLogisticsIndex(HQCentralSnapshot currentSnapshot)
        {
            var selectedKey = GetLogisticsPlanKey(selectedLogisticsPlan);
            logisticsPlans.Clear();
            selectedLogisticsPlan = null;

            foreach (var headquarters in currentSnapshot.Headquarters)
            {
                foreach (var plan in headquarters.LogisticsPlans)
                {
                    logisticsPlans.Add(plan);
                    if (selectedKey != null && GetLogisticsPlanKey(plan) == selectedKey)
                        selectedLogisticsPlan = plan;
                }
            }
        }

        private static string? GetLogisticsPlanKey(HQCentralLogisticsPlan? plan)
        {
            return plan == null
                ? null
                : plan.HeadquartersAddress + "|" + plan.OriginAddress + "|" + plan.AssignedManagerName;
        }

        private enum HeadquartersView
        {
            Overview,
            Logistics
        }

        private enum LogisticsFilter
        {
            All,
            Warehouses,
            Factories
        }
    }
}
