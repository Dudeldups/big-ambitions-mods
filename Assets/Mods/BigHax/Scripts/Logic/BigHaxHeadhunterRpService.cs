#nullable enable
using System;
using System.Collections;
using System.Reflection;
using Buildings.Office.Headquarters;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxHeadhunterRpService
    {
        private const int DiagnosticCalculationLimit = 12;

        private static bool enabled;
        private static int diagnosticCalculationCount;

        private BigHaxMethodDetour? helperDetour;
        private BigHaxMethodDetour? planGetterDetour;

        public void Initialize()
        {
            helperDetour = Install(
                typeof(HeadhunterHelper).GetMethod(nameof(HeadhunterHelper.CalculateMaxDealBreakersPoints), new[] { typeof(float) }),
                typeof(BigHaxHeadhunterRpService).GetMethod(nameof(CalculateMaxDealBreakersPoints), BindingFlags.Static | BindingFlags.NonPublic),
                "headhunter RP/helper");
            planGetterDetour = Install(
                typeof(HeadhunterPlan).GetProperty(nameof(HeadhunterPlan.AvailableDealBreakersPoints))?.GetGetMethod(),
                typeof(BigHaxHeadhunterRpService).GetMethod(nameof(GetAvailableDealBreakersPoints), BindingFlags.Static | BindingFlags.NonPublic),
                "headhunter RP/plan getter");
            AttachUiHooks();
        }

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
            var changed = enabled != settings.EnableMaximumHeadhunterRecruitmentPoints;
            enabled = settings.EnableMaximumHeadhunterRecruitmentPoints;
            if (changed)
            {
                diagnosticCalculationCount = 0;
                BigHaxLogger.Diagnostic(
                    "Headhunter RP configured: enabled=" + enabled +
                    ", override=" + BigHaxSettings.MaximumHeadhunterRecruitmentPoints +
                    ", helperDetour=" + (helperDetour?.IsApplied == true) +
                    ", planGetterDetour=" + (planGetterDetour?.IsApplied == true) + ".");
            }

            AttachUiHooks();
            BigHaxHeadhunterRpUiHook.RefreshAll();
        }

        public void AttachUiHooks()
        {
            var added = 0;
            foreach (var tab in Resources.FindObjectsOfTypeAll<HeadhuntersRecruitingTab>())
            {
                if (tab == null || tab.GetComponent<BigHaxHeadhunterRpUiHook>() != null)
                    continue;

                tab.gameObject.AddComponent<BigHaxHeadhunterRpUiHook>();
                added++;
            }

            if (added > 0)
                BigHaxLogger.Diagnostic("Headhunter RP UI hooks attached: count=" + added + ".");
        }

        public void Shutdown()
        {
            enabled = false;
            BigHaxHeadhunterRpUiHook.RefreshAll();
            Restore(planGetterDetour, "headhunter RP/plan getter");
            Restore(helperDetour, "headhunter RP/helper");
            planGetterDetour = null;
            helperDetour = null;
            diagnosticCalculationCount = 0;
        }

        internal static int GetConfiguredPoints(float skill)
        {
            var value = enabled
                ? BigHaxSettings.MaximumHeadhunterRecruitmentPoints
                : Mathf.FloorToInt(skill / 100f * 100f);
            if (diagnosticCalculationCount < DiagnosticCalculationLimit)
            {
                diagnosticCalculationCount++;
                BigHaxLogger.Diagnostic(
                    "Headhunter RP calculated: skill=" + skill +
                    ", enabled=" + enabled +
                    ", availablePoints=" + value + ".");
            }

            return value;
        }

        private static int CalculateMaxDealBreakersPoints(float skill)
        {
            return GetConfiguredPoints(skill);
        }

        private static int GetAvailableDealBreakersPoints(HeadhunterPlan plan)
        {
            return GetConfiguredPoints(plan?.HeadhunterSkillValue ?? 0f);
        }

        private static BigHaxMethodDetour? Install(MethodInfo? target, MethodInfo? replacement, string name)
        {
            if (target == null || replacement == null)
            {
                BigHaxLogger.Diagnostic(name + " detour failed: method not found.");
                return null;
            }

            var detour = new BigHaxMethodDetour(target, replacement);
            if (!detour.Apply(out var error))
            {
                BigHaxLogger.Diagnostic(name + " detour failed: " + error);
                return detour;
            }

            BigHaxLogger.Diagnostic(name + " detour installed.");
            return detour;
        }

        private static void Restore(BigHaxMethodDetour? detour, string name)
        {
            if (detour != null && !detour.Restore(out var error))
                BigHaxLogger.Diagnostic(name + " detour restore skipped: " + error);
        }
    }

    internal sealed class BigHaxHeadhunterRpUiHook : MonoBehaviour
    {
        private static readonly FieldInfo? DealBreakersField = typeof(HeadhuntersRecruitingTab).GetField(
            "dealBreakers",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private Coroutine? refreshCoroutine;

        private void OnEnable()
        {
            ScheduleRefresh();
        }

        public static void RefreshAll()
        {
            foreach (var hook in Resources.FindObjectsOfTypeAll<BigHaxHeadhunterRpUiHook>())
                hook?.ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (refreshCoroutine == null && gameObject.activeInHierarchy)
                refreshCoroutine = StartCoroutine(RefreshAfterVanillaSetup());
        }

        private IEnumerator RefreshAfterVanillaSetup()
        {
            yield return null;
            refreshCoroutine = null;

            try
            {
                var tab = GetComponent<HeadhuntersRecruitingTab>();
                var dealBreakers = tab == null || DealBreakersField == null
                    ? null
                    : DealBreakersField.GetValue(tab) as HeadhuntersDealBreakers;
                if (dealBreakers == null)
                {
                    BigHaxLogger.Diagnostic("Headhunter RP UI refresh failed: deal-breaker controller unavailable.");
                    yield break;
                }

                if (tab == null)
                    yield break;

                var planUi = tab.GetComponentInParent<HeadhunterPlanUI>();
                var skill = planUi?.currentPlan?.HeadhunterSkillValue ?? 0f;
                dealBreakers.availableDealBreakersPoints = BigHaxHeadhunterRpService.GetConfiguredPoints(skill);
                BigHaxLogger.Diagnostic(
                    "Headhunter RP UI refreshed: plan=" + (planUi?.currentPlan?.id ?? "none") +
                    ", availablePoints=" + dealBreakers.availableDealBreakersPoints + ".");
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException("Headhunter RP UI refresh", exception);
            }
        }
    }
}
