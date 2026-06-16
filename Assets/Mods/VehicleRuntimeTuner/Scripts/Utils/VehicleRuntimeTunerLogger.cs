#nullable enable
using BAModAPI;

namespace VehicleRuntimeTuner.Utils
{
    public sealed class VehicleRuntimeTunerLogger
    {
        private ModContext? context;

        public void Initialize(ModContext modContext)
        {
            context = modContext;
            if (VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                VehicleRuntimeTunerFileLogger.Log("INFO", "logger initialized");
        }

        public void Info(string message)
        {
            if (!VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                return;

            context?.Logger.Info("VehicleRuntimeTuner: " + message);
            VehicleRuntimeTunerFileLogger.Log("INFO", message);
        }

        public void Warn(string message)
        {
            if (!VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                return;

            context?.Logger.Warn("VehicleRuntimeTuner: " + message);
            VehicleRuntimeTunerFileLogger.Log("WARN", message);
        }

        public void Error(string message)
        {
            if (!VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                return;

            context?.Logger.Warn("VehicleRuntimeTuner: " + message);
            VehicleRuntimeTunerFileLogger.Log("ERROR", message);
        }
    }
}
