#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxFileLogger
    {
        private static readonly object Sync = new object();
        private static string? logPath;

        public static string LogPath
        {
            get
            {
                if (!string.IsNullOrEmpty(logPath))
                    return logPath;

                try
                {
                    var directory = Path.Combine(Environment.CurrentDirectory, "Logs", "Mods");
                    Directory.CreateDirectory(directory);
                    logPath = Path.Combine(directory, "BigHax.log");
                }
                catch
                {
                    logPath = Path.Combine(Path.GetTempPath(), "BigHax.log");
                }

                return logPath;
            }
        }

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
