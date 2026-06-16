#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace VehicleRuntimeTuner.Utils
{
    public static class VehicleRuntimeTunerFileLogger
    {
        private static readonly object Sync = new object();
        private static string? logPath;

        public static string LogPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(logPath))
                    return logPath;

                try
                {
                    Directory.CreateDirectory(VehicleRuntimeTunerPaths.RootDirectory);
                    logPath = Path.Combine(VehicleRuntimeTunerPaths.RootDirectory, "vehicle-runtime-tuner.log");
                }
                catch
                {
                    logPath = Path.Combine(Path.GetTempPath(), "vehicle-runtime-tuner.log");
                }

                return logPath;
            }
        }

        public static void Log(string level, string message)
        {
            if (!VehicleRuntimeTunerDebugOptions.EnableDebugLogging)
                return;

            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
