#nullable enable
using System.Reflection;
using PlayerActivity;
using Timemachine;
using UnityEngine;

namespace BigHax
{
    /// <summary>
    /// Speeds up only a BigHax extended bed sleep. It is driven by the game's
    /// time-machine start/end events and does no per-frame or scene-wide work.
    /// </summary>
    internal sealed class BigHaxSleepTimeAccelerationService
    {
        private const float SpeedMultiplier = 6f;
        private static readonly FieldInfo? TimeSpeedCurveField = typeof(TimeMachine).GetField(
            "timeSpeedCurve",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private TimeMachine? acceleratedTimeMachine;
        private AnimationCurve? originalCurve;

        public void HandleTimeMachineStarted(BigHaxSettings? settings)
        {
            if (settings?.EnableExtendedBedSleep != true || !IsBedSleepActive())
                return;

            var timeMachine = Object.FindObjectOfType<TimeMachine>();
            if (timeMachine == null || TimeSpeedCurveField == null)
                return;

            var currentCurve = TimeSpeedCurveField.GetValue(timeMachine) as AnimationCurve;
            if (currentCurve == null || acceleratedTimeMachine != null)
                return;

            originalCurve = currentCurve;
            TimeSpeedCurveField.SetValue(timeMachine, CreateAcceleratedCurve(currentCurve));
            acceleratedTimeMachine = timeMachine;
        }

        public void HandleTimeMachineEnded()
        {
            RestoreOriginalCurve();
        }

        public void RestoreOriginalCurve()
        {
            if (acceleratedTimeMachine != null && originalCurve != null && TimeSpeedCurveField != null)
                TimeSpeedCurveField.SetValue(acceleratedTimeMachine, originalCurve);

            acceleratedTimeMachine = null;
            originalCurve = null;
        }

        private static bool IsBedSleepActive()
        {
            var activityUi = Object.FindObjectOfType<PlayerActivityUI>();
            return activityUi?.GetCurrentActivity is SleepActivity;
        }

        private static AnimationCurve CreateAcceleratedCurve(AnimationCurve source)
        {
            var keys = source.keys;
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                key.value *= SpeedMultiplier;
                keys[index] = key;
            }

            return new AnimationCurve(keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
        }
    }
}
