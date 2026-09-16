#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using BAModAPI;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        private static readonly HashSet<string> Warnings = new HashSet<string>();

        public static void Info(ModContext? context, string message)
        {
            context?.Logger.Info(message);
        }

        public static void WarnOnce(ModContext? context, string key, string message)
        {
            if (!Warnings.Add(key))
                return;

            context?.Logger.Warn(message);
        }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void StartDiagnosticSession() { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void Diagnostic(string message) { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void DiagnosticException(string source, System.Exception exception) { }
    }
}
