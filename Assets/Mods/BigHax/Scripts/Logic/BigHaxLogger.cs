#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BAModAPI;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        private const string PreferredLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

        private static readonly object Sync = new object();
        public static string DiagnosticLogPath => Path.Combine(PreferredLogDirectory, "BigHax-debug.log");

        // Release logging remains compiled out. Define BIGHAX_DIAGNOSTICS for a
        // focused troubleshooting build without changing call sites.
        public static void Info(ModContext? context, string message) { }
        public static void WarnOnce(ModContext? context, string key, string message) { }

        // Temporary focused logging for the options-renderer runtime validation.
        // Remove after the BA Unified UI and fallback paths are confirmed in game.
        public static void UiDiagnostic(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Write("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] Options UI: " + message);
        }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void StartDiagnosticSession()
        {
            Write("===== BigHax diagnostic session started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " =====");
        }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void Diagnostic(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Write("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);
        }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void DiagnosticException(string source, Exception exception)
        {
            Diagnostic(source + " failed: " + exception);
        }

        private static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(PreferredLogDirectory);
                    File.AppendAllText(DiagnosticLogPath, message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostics must never affect game play if the SDK workspace is unavailable.
            }
        }
    }
}
