#nullable enable
using System.Linq;
using BAModAPI;
using Timemachine;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsTimeService
    {
        private readonly ModContext context;
        private TimeMachine? cachedTimeMachine;

        public DeveloperToolsTimeService(ModContext context) => this.context = context;

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

            var target = TimeHelper.Now().AddHours(hours);
            machine.StartTimeMachine(target);
            message = "Advancing simulation by " + hours + " hour" + (hours == 1 ? "." : "s.");
            return true;
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
