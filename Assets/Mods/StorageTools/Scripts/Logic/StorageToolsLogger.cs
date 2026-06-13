#nullable enable
using System.Collections.Generic;
using BAModAPI;

namespace StorageTools
{
    internal static class StorageToolsLogger
    {
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>();

        public static void Info(ModContext? context, string message)
        {
            context?.Logger.Info(message);
        }

        public static void Warn(ModContext? context, string message)
        {
            context?.Logger.Warn(message);
        }

        public static void WarnOnce(ModContext? context, string key, string message)
        {
            if (!WarnedKeys.Add(key))
                return;

            Warn(context, message);
        }
    }
}
