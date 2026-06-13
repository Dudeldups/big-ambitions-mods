#nullable enable
using System;
using System.IO;
using BAModAPI;
using UnityEngine;

namespace SharedWholesaleDesk
{
    internal static class SharedWholesaleDeskDebugSettings
    {
        internal const bool EnableFileLogging = true;
    }

    internal static class SharedWholesaleDeskLog
    {
        private static IModLogger? _logger;

        internal static void SetLogger(IModLogger? logger)
        {
            _logger = logger;
        }

        internal static void Info(string message)
        {
            try
            {
                _logger?.Info(message);
            }
            catch
            {
            }

            if (SharedWholesaleDeskDebugSettings.EnableFileLogging)
                SharedWholesaleDeskFileLogger.Log("INFO", message);

            Debug.Log($"SharedWholesaleDesk: {message}");
        }

        internal static void Warn(string message)
        {
            try
            {
                _logger?.Warn(message);
            }
            catch
            {
            }

            if (SharedWholesaleDeskDebugSettings.EnableFileLogging)
                SharedWholesaleDeskFileLogger.Log("WARN", message);

            Debug.LogWarning($"SharedWholesaleDesk: {message}");
        }
    }

    internal static class SharedWholesaleDeskFileLogger
    {
        private static readonly object Sync = new object();
        private static string? _logPath;

        internal static string LogPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_logPath))
                    return _logPath;

                try
                {
                    var directory = Path.Combine(Application.persistentDataPath, "SharedWholesaleDesk");
                    Directory.CreateDirectory(directory);
                    _logPath = Path.Combine(directory, "shared-wholesale-debug.log");
                }
                catch
                {
                    _logPath = Path.Combine(Path.GetTempPath(), "shared-wholesale-debug.log");
                }

                return _logPath;
            }
        }

        internal static void Log(string level, string message)
        {
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
