#nullable enable
using System;
using System.IO;
using System.Text;

namespace BigHax
{
    // Temporary, focused diagnostics for the Big Ambitions 1.0 customer scheduler.
    // Keep this separate from the normal release logger so it cannot create noisy
    // output from unrelated Big Hax features.
    internal static class BigHaxCustomerTrafficDebugLog
    {
        private const string LogPath =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods\BigHax-customer-traffic-debug.log";

        private static readonly object Sync = new object();

        public static void StartSession(string modId)
        {
            Write($"===== BigHax customer traffic diagnostic session started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}; modId={modId} =====");
        }

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    var directory = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.AppendAllText(
                        LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostics must never affect customer scheduling or gameplay.
            }
        }
    }
}
