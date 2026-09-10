#nullable enable
using System.Diagnostics;
using BAModAPI;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        // These compatibility hooks keep focused diagnostic call sites out of
        // release builds without retaining a file-writing logger in the mod.
        public static void Info(ModContext? context, string message) { }
        public static void WarnOnce(ModContext? context, string key, string message) { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void StartDiagnosticSession() { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void Diagnostic(string message) { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void DiagnosticException(string source, System.Exception exception) { }
    }
}
