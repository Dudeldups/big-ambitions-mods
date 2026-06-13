#nullable enable
using System.Collections.Generic;
using BAModAPI;

namespace StorageTools
{
    internal static class StorageToolsLogger
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
            StorageToolsFileLogger.Log(message);
        }

        public static void Warn(ModContext? context, string message)
        {
            if (!debugLoggingEnabled)
                return;

            context?.Logger.Warn(message);
            StorageToolsFileLogger.Log("WARN: " + message);
        }

        public static void WarnOnce(ModContext? context, string key, string message)
        {
            if (!WarnedKeys.Add(key))
                return;

            Warn(context, message);
        }
    }
}
