#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;

namespace TaxiBang
{
    internal static class TaxiDiagnostics
    {
        public static readonly bool DebugLoggingEnabled = true;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> NamedLogPaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string PreferredWorkspaceLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

        public static string LogPath => GetLogPath("Taxi.log", "Taxi.log");

        public static void Info(ModContext? context, string message)
        {
            if (!DebugLoggingEnabled)
                return;

            context?.Logger.Info(message);
            Log(message);
        }

        public static void Warn(ModContext? context, string message)
        {
            if (!DebugLoggingEnabled)
                return;

            context?.Logger.Warn(message);
            Log("WARN: " + message);
        }

        public static void Error(ModContext? context, string scope, Exception exception)
        {
            if (!DebugLoggingEnabled)
                return;

            context?.Logger.Error(exception);
            Log("ERROR [" + scope + "]: " + exception);
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

        private static string GetLogPath(string fileName, string fallbackFileName)
        {
            lock (Sync)
            {
                if (NamedLogPaths.TryGetValue(fileName, out var cachedPath))
                    return cachedPath;

                string resolvedPath;
                try
                {
                    Directory.CreateDirectory(PreferredWorkspaceLogDirectory);
                    resolvedPath = Path.Combine(PreferredWorkspaceLogDirectory, fileName);
                }
                catch
                {
                    resolvedPath = Path.Combine(Path.GetTempPath(), fallbackFileName);
                }

                NamedLogPaths[fileName] = resolvedPath;
                return resolvedPath;
            }
        }
    }
}
