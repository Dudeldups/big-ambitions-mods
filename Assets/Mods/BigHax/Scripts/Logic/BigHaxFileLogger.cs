#nullable enable
namespace BigHax
{
    internal static class BigHaxFileLogger
    {
        // Big Ambitions 1.0's player profile strips the System.IO APIs that the
        // legacy development logger used. Keep this compatibility shim so that
        // optional diagnostics can never interrupt gameplay or UI creation.
        public static string LogPath => "disabled";
        public static void Log(string message) { }
        public static void Log(string fileName, string fallbackFileName, string message) { }
    }
}
