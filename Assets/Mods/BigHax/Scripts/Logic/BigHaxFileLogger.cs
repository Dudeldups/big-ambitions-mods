#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxFileLogger
    {
        private static readonly object Sync = new object();
        private static readonly System.Collections.Generic.Dictionary<string, string> NamedLogPaths =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string PreferredWorkspaceLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";
        private static string? defaultLogPath;

        public static string LogPath
        {
            get
            {
                return GetLogPath("BigHax.log", "BigHax.log");
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

        public static void Log(string fileName, string fallbackFileName, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (Sync)
                {
                    var path = GetLogPath(fileName, fallbackFileName);
                    File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }

        private static string GetLogPath(string fileName, string fallbackFileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "BigHax.log";

            lock (Sync)
            {
                if (string.Equals(fileName, "BigHax.log", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(defaultLogPath))
                    return defaultLogPath;

                if (NamedLogPaths.TryGetValue(fileName, out var cachedPath))
                    return cachedPath;

                string resolvedPath;
                try
                {
                    var directory = PreferredWorkspaceLogDirectory;
                    Directory.CreateDirectory(directory);
                    resolvedPath = Path.Combine(directory, fileName);
                }
                catch
                {
                    resolvedPath = Path.Combine(Path.GetTempPath(), fallbackFileName);
                }

                NamedLogPaths[fileName] = resolvedPath;
                if (string.Equals(fileName, "BigHax.log", StringComparison.OrdinalIgnoreCase))
                    defaultLogPath = resolvedPath;

                return resolvedPath;
            }
        }
    }
}
