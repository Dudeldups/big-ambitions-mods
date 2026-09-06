#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BigAmbitions.Characters.Skills;
using BigAmbitions.Tags;
using Buildings.Office.Headquarters;
using Entities.Employee.JobDemands;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxHeadhunterRpService
    {
        private const int DiagnosticCalculationLimit = 12;

        private static bool enabled;
        private static int diagnosticCalculationCount;
        private static int diagnosticCandidateCount;

        private BigHaxMethodDetour? candidateDemandsDetour;
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
            candidateDemandsDetour = Install(
                typeof(HeadhunterPlan).GetMethod("GetRandomDemandsForCandidate", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(BigHaxHeadhunterRpService).GetMethod(nameof(GetRandomDemandsForCandidate), BindingFlags.Static | BindingFlags.NonPublic),
                "headhunter RP/candidate demand generation");
            AttachUiHooks();
        }

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
            var changed = enabled != settings.EnableMaximumHeadhunterRecruitmentPoints;
            enabled = settings.EnableMaximumHeadhunterRecruitmentPoints;
            if (changed)
            {
                diagnosticCalculationCount = 0;
                diagnosticCandidateCount = 0;
                BigHaxLogger.Diagnostic(
                    "Headhunter RP configured: enabled=" + enabled +
                    ", override=" + BigHaxSettings.MaximumHeadhunterRecruitmentPoints +
                    ", helperDetour=" + (helperDetour?.IsApplied == true) +
                    ", planGetterDetour=" + (planGetterDetour?.IsApplied == true) +
                    ", candidateDemandsDetour=" + (candidateDemandsDetour?.IsApplied == true) + ".");
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
            Restore(candidateDemandsDetour, "headhunter RP/candidate demand generation");
            Restore(planGetterDetour, "headhunter RP/plan getter");
            Restore(helperDetour, "headhunter RP/helper");
            planGetterDetour = null;
            helperDetour = null;
            candidateDemandsDetour = null;
            diagnosticCalculationCount = 0;
            diagnosticCandidateCount = 0;
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

        private static List<string>? GetRandomDemandsForCandidate(HeadhunterPlan plan, float totalSkillValue)
        {
            var requiredDemandCount = JobDemandHelper.GetIdealNumberOfDemands(plan.skillRecruiting, totalSkillValue);
            var demands = new List<string>();
            if (requiredDemandCount == 0)
            {
                LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "no demands required");
                return demands;
            }

            var skillData = SkillHelper.GetData(plan.skillRecruiting);
            if (skillData == null)
            {
                LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "skill data unavailable");
                return null;
            }

            if (skillData.HasTag(TagRef.Skilltag.forcefulltime))
            {
                demands.Add("ba:jobdemand_fulltime");
                requiredDemandCount--;
            }
            else if (skillData.HasTag(TagRef.Skilltag.hashoursperweekdemand))
            {
                var excludePartTime = plan.dealBreakerTypes.Contains("ba:headhuntersdealbreaker_parttime");
                var excludeFullTime = plan.dealBreakerTypes.Contains("ba:headhuntersdealbreaker_fulltime");
                if (excludePartTime && excludeFullTime)
                {
                    if (!enabled)
                    {
                        LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "both work schedules excluded; vanilla rejection");
                        return null;
                    }

                    // With the 1000-RP hax, excluding every schedule demand means
                    // this candidate simply has no schedule demand.
                    requiredDemandCount--;
                }
                else if (excludePartTime)
                {
                    demands.Add("ba:jobdemand_fulltime");
                    requiredDemandCount--;
                }
                else if (excludeFullTime)
                {
                    demands.Add("ba:jobdemand_parttime");
                    requiredDemandCount--;
                }
                else
                {
                    var scheduleDemand = JobDemandHelper.GetRandomHoursPerWeekDemandForSkill(plan.skillRecruiting);
                    if (string.IsNullOrEmpty(scheduleDemand))
                    {
                        LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "schedule demand unavailable");
                        return null;
                    }

                    demands.Add(scheduleDemand);
                    requiredDemandCount--;
                }
            }

            var jobSpecificDemand = JobDemandHelper.GetRandomJobSpecificDemandForSkill(plan.skillRecruiting);
            if (!string.IsNullOrEmpty(jobSpecificDemand))
            {
                demands.Add(jobSpecificDemand);
                requiredDemandCount--;
            }

            var demandsToIgnore = new List<string>();
            if (plan.skillRecruiting == "ba:skill_hrmanager")
                demandsToIgnore.AddRange(JobDemandHelper.HealthInsuranceDemands);

            foreach (var dealBreakerType in plan.dealBreakerTypes)
            {
                var dealBreaker = HeadhunterHelper.GetData(dealBreakerType);
                if (dealBreaker?.applicableJobDemands != null)
                    demandsToIgnore.AddRange(dealBreaker.applicableJobDemands);
            }

            while (requiredDemandCount > 0)
            {
                var demand = JobDemandHelper.GetRandomDemandForSkill(plan.skillRecruiting, demands, demandsToIgnore);
                if (string.IsNullOrEmpty(demand))
                {
                    if (!enabled)
                    {
                        LogCandidateResult(plan, totalSkillValue, requiredDemandCount, demands, "no permitted random demand; vanilla rejection");
                        return null;
                    }

                    // All remaining demands were deliberately excluded. Treat that
                    // as a successful no-demand result instead of stopping recruitment.
                    break;
                }

                demands.Add(demand);
                requiredDemandCount--;
            }

            LogCandidateResult(
                plan,
                totalSkillValue,
                requiredDemandCount,
                demands,
                requiredDemandCount > 0 ? "excluded demand slots accepted by hax" : "candidate demands generated");
            return demands;
        }

        private static void LogCandidateResult(
            HeadhunterPlan plan,
            float totalSkillValue,
            int remainingDemandCount,
            List<string> demands,
            string result)
        {
            if (diagnosticCandidateCount >= 24)
                return;

            diagnosticCandidateCount++;
            BigHaxLogger.Diagnostic(
                "Headhunter candidate demand result: plan=" + plan.id +
                ", skill=" + plan.skillRecruiting +
                ", totalSkill=" + totalSkillValue +
                ", haxEnabled=" + enabled +
                ", exclusions=" + plan.dealBreakerTypes.Count +
                ", generatedDemands=" + demands.Count +
                ", remainingDemandSlots=" + remainingDemandCount +
                ", result=" + result +
                ", demands=[" + string.Join(",", demands.ToArray()) + "].");
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
