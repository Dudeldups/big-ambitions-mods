#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Entities;
using Helpers;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxEmployeeTrainingService
    {
        private const float VanillaTrainingSkillIncrease = 10f;

        private readonly Dictionary<string, TrackedTraining> trackedTrainingByEmployeeId =
            new Dictionary<string, TrackedTraining>(StringComparer.Ordinal);

        private readonly HashSet<string> processedFinishedTrainingKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public void InvalidateCache()
        {
            trackedTrainingByEmployeeId.Clear();
            processedFinishedTrainingKeys.Clear();
        }

        public void Update(ModContext context, BigHaxSettings settings)
        {
            TrackActiveTrainingSessions();

            if (settings.EmployeeTrainingSkillIncrease <= BigHaxSettings.DefaultEmployeeTrainingSkillIncrease)
                return;

            foreach (var pair in EmployeeHelper.FinishedTrainingEmployees)
            {
                var employee = pair.Key;
                var skillName = pair.Value;
                if (employee == null || string.IsNullOrWhiteSpace(employee.id) || string.IsNullOrWhiteSpace(skillName))
                    continue;

                var processedKey = BuildProcessedKey(employee.id, SaveGameManager.Current?.Day ?? 0, skillName);
                if (!processedFinishedTrainingKeys.Add(processedKey))
                    continue;

                ApplyConfiguredTrainingGain(context, employee, skillName, settings.EmployeeTrainingSkillIncrease);
                trackedTrainingByEmployeeId.Remove(employee.id);
            }
        }

        private void TrackActiveTrainingSessions()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.EmployeeInstances == null)
                return;

            foreach (var employee in saveGame.EmployeeInstances)
            {
                if (employee?.trainingSession == null || string.IsNullOrWhiteSpace(employee.id))
                    continue;

                var skillName = employee.trainingSession.skill;
                if (!TryGetSkillValue(employee, skillName, out var currentSkillValue))
                    continue;

                if (trackedTrainingByEmployeeId.TryGetValue(employee.id, out var trackedTraining) &&
                    trackedTraining.StartDay == employee.trainingSession.startDay &&
                    string.Equals(trackedTraining.SkillName, skillName, StringComparison.Ordinal))
                {
                    continue;
                }

                trackedTrainingByEmployeeId[employee.id] = new TrackedTraining(skillName, employee.trainingSession.startDay, currentSkillValue);
            }
        }

        private void ApplyConfiguredTrainingGain(ModContext context, EmployeeInstance employee, string skillName, int configuredGain)
        {
            if (!TryGetSkillValue(employee, skillName, out var currentSkillValue))
                return;

            var trackedStartValue = trackedTrainingByEmployeeId.TryGetValue(employee.id, out var trackedTraining) &&
                                    string.Equals(trackedTraining.SkillName, skillName, StringComparison.Ordinal)
                ? trackedTraining.StartingValue
                : Mathf.Max(0f, currentSkillValue - VanillaTrainingSkillIncrease);

            var targetValue = Mathf.Min(100f, trackedStartValue + configuredGain);
            var extraGain = targetValue - currentSkillValue;
            if (extraGain <= 0f)
                return;

            employee.IncreaseSkill(skillName, extraGain);
            employee.IncreaseWageFromTraining(extraGain);
            BigHaxLogger.Info(
                context,
                $"BigHax: boosted completed training for {employee.characterData.name} in {skillName} by +{extraGain:0.##} skill.");
        }

        private static string BuildProcessedKey(string employeeId, int day, string skillName)
        {
            return employeeId + "|" + day + "|" + skillName;
        }

        private static bool TryGetSkillValue(EmployeeInstance employee, string skillName, out float value)
        {
            value = 0f;
            var skills = employee.characterData?.skills;
            if (skills == null)
                return false;

            for (var index = 0; index < skills.Count; index++)
            {
                var skill = skills[index];
                if (skill != null && string.Equals(skill.name, skillName, StringComparison.Ordinal))
                {
                    value = skill.value;
                    return true;
                }
            }

            return false;
        }

        private readonly struct TrackedTraining
        {
            public TrackedTraining(string skillName, int startDay, float startingValue)
            {
                SkillName = skillName;
                StartDay = startDay;
                StartingValue = startingValue;
            }

            public string SkillName { get; }

            public int StartDay { get; }

            public float StartingValue { get; }
        }
    }
}
