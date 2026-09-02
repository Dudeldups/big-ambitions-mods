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
        private static readonly FieldInfo? UseConstantSpeedField = typeof(TimeMachine).GetField(
            "_useConstantSpeed",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private TimeMachine? acceleratedTimeMachine;
        private AnimationCurve? originalCurve;
        private bool? originalUseConstantSpeed;

        public void HandleTimeMachineStarted(BigHaxSettings? settings)
        {
            if (settings?.EnableExtendedBedSleep != true || !IsBedSleepActive())
                return;

            var timeMachine = Object.FindObjectOfType<TimeMachine>();
            if (timeMachine == null || TimeSpeedCurveField == null || UseConstantSpeedField == null)
                return;

            var currentCurve = TimeSpeedCurveField.GetValue(timeMachine) as AnimationCurve;
            if (currentCurve == null || acceleratedTimeMachine != null)
                return;

            originalCurve = currentCurve;
            originalUseConstantSpeed = (bool)UseConstantSpeedField.GetValue(timeMachine);
            TimeSpeedCurveField.SetValue(timeMachine, CreateAcceleratedCurve(currentCurve));
            // Sleep starts the time machine in constant-speed mode, which
            // otherwise bypasses timeSpeedCurve entirely.
            UseConstantSpeedField.SetValue(timeMachine, false);
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

            if (acceleratedTimeMachine != null && originalUseConstantSpeed.HasValue && UseConstantSpeedField != null)
                UseConstantSpeedField.SetValue(acceleratedTimeMachine, originalUseConstantSpeed.Value);

            acceleratedTimeMachine = null;
            originalCurve = null;
            originalUseConstantSpeed = null;
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
