#nullable enable
using BAModAPI;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        // Retained as a source-compatible no-op for existing call sites. Release
        // builds do not emit diagnostic logs; unexpected failures use Logger.Error.
        public static void Info(ModContext? context, string message) { }
        public static void WarnOnce(ModContext? context, string key, string message) { }
    }
}
