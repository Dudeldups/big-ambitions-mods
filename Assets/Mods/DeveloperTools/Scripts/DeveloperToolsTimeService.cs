#nullable enable
using System.Linq;
using System.Reflection;
using BAModAPI;
using Timemachine;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsTimeService
    {
        private const float SpeedMultiplier = 6f;
        private const BindingFlags TimeMachineFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? TimeSpeedCurveField = typeof(TimeMachine).GetField(
            "timeSpeedCurve",
            TimeMachineFieldFlags);
        private static readonly FieldInfo? UseConstantSpeedField = typeof(TimeMachine).GetField(
            "_useConstantSpeed",
            TimeMachineFieldFlags);
        private readonly ModContext context;
        private TimeMachine? cachedTimeMachine;
        private TimeMachine? acceleratedTimeMachine;
        private AnimationCurve? originalCurve;
        private bool? originalUseConstantSpeed;

        public DeveloperToolsTimeService(ModContext context)
        {
            this.context = context;
            GlobalEvents.onTimeMachineEnded += HandleTimeMachineEnded;
        }

        public bool AdvanceHours(int hours, out string message)
        {
            if (hours <= 0 || SaveGameManager.Current == null)
            {
                message = "A loaded save and positive duration are required.";
                return false;
            }

            var machine = ResolveTimeMachine();
            if (machine == null)
            {
                message = "The game's time machine is not available in this scene.";
                context.Logger.Warn("DeveloperTools: time advance failed; TimeMachine component unavailable.");
                return false;
            }
            if (machine.isRunning)
            {
                message = "A time advance is already running.";
                return false;
            }

            var baseCurve = TimeSpeedCurveField?.GetValue(machine) as AnimationCurve;
            var baseUseConstantSpeed = UseConstantSpeedField?.GetValue(machine) as bool?;
            var target = TimeHelper.Now().AddHours(hours);
            machine.StartTimeMachine(target);
            Accelerate(machine, baseCurve, baseUseConstantSpeed);
            message = "Advancing simulation by " + hours + " hour" + (hours == 1 ? "." : "s.");
            return true;
        }

        public void Shutdown()
        {
            GlobalEvents.onTimeMachineEnded -= HandleTimeMachineEnded;
            RestoreOriginalSettings();
        }

        private void Accelerate(TimeMachine machine, AnimationCurve? baseCurve, bool? baseUseConstantSpeed)
        {
            if (TimeSpeedCurveField == null || UseConstantSpeedField == null || acceleratedTimeMachine != null)
            {
                context.Logger.Warn("DeveloperTools: could not accelerate the TimeMachine because its speed members were unavailable.");
                return;
            }

            if (baseCurve == null)
            {
                context.Logger.Warn("DeveloperTools: could not accelerate the TimeMachine because its speed curve was unavailable.");
                return;
            }

            originalCurve = baseCurve;
            originalUseConstantSpeed = baseUseConstantSpeed;
            acceleratedTimeMachine = machine;
            TimeSpeedCurveField.SetValue(machine, CreateAcceleratedCurve(baseCurve));
            UseConstantSpeedField.SetValue(machine, false);
        }

        private void HandleTimeMachineEnded() => RestoreOriginalSettings();

        private void RestoreOriginalSettings()
        {
            if (acceleratedTimeMachine != null && originalCurve != null && TimeSpeedCurveField != null)
                TimeSpeedCurveField.SetValue(acceleratedTimeMachine, originalCurve);
            if (acceleratedTimeMachine != null && originalUseConstantSpeed.HasValue && UseConstantSpeedField != null)
                UseConstantSpeedField.SetValue(acceleratedTimeMachine, originalUseConstantSpeed.Value);

            acceleratedTimeMachine = null;
            originalCurve = null;
            originalUseConstantSpeed = null;
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

        private TimeMachine? ResolveTimeMachine()
        {
            if (cachedTimeMachine != null)
                return cachedTimeMachine;

            cachedTimeMachine = Object.FindObjectOfType<TimeMachine>();
            if (cachedTimeMachine == null)
            {
                cachedTimeMachine = Resources.FindObjectsOfTypeAll<TimeMachine>()
                    .FirstOrDefault(value => value != null && value.gameObject.hideFlags == HideFlags.None);
            }
            return cachedTimeMachine;
        }
    }
}
