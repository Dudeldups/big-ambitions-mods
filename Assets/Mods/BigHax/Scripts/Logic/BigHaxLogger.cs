#nullable enable
using System.Collections.Generic;
using BAModAPI;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>();
        private static bool debugLoggingEnabled;

        public static void SetDebugLoggingEnabled(bool enabled)
        {
            debugLoggingEnabled = enabled;
        }

        public static void Info(ModContext? context, string message)
        {
            if (!debugLoggingEnabled)
                return;

            context?.Logger.Info(message);
            BigHaxFileLogger.Log(message);
        }

        public static void Warn(ModContext? context, string message)
        {
            if (!debugLoggingEnabled)
                return;

            context?.Logger.Warn(message);
            BigHaxFileLogger.Log("WARN: " + message);
        }

        public static void WarnOnce(ModContext? context, string key, string message)
        {
            if (!WarnedKeys.Add(key))
                return;

            Warn(context, message);
        }
    }
}
