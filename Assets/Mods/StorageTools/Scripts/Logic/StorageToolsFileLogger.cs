#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace StorageTools
{
    internal static class StorageToolsFileLogger
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
                    var directory = Path.Combine(Application.persistentDataPath, "StorageTools");
                    Directory.CreateDirectory(directory);
                    logPath = Path.Combine(directory, "storage-tools.log");
                }
                catch
                {
                    logPath = Path.Combine(Path.GetTempPath(), "storage-tools.log");
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
