#nullable enable
using BAModAPI;
using UI;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsPauseService
    {
        private readonly ModContext context;
        private GameSpeedController? pausedController;

        public DeveloperToolsPauseService(ModContext context) => this.context = context;

        public void PauseForOverlay()
        {
            var controller = InstanceBehavior<UIs>.Instance?.gameSpeed;
            if (controller == null)
            {
                context.Logger.Warn("DeveloperTools: could not pause for the testing UI because GameSpeedController was unavailable.");
                return;
            }

            if (controller.Paused)
                return;

            pausedController = controller;
            controller.SetPause(true, true);
        }

        public void ResumeAfterOverlay()
        {
            if (pausedController == null)
                return;

            pausedController.Reset();
            pausedController = null;
        }
    }
}
