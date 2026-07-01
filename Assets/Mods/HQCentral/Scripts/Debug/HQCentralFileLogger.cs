#nullable enable
using System;
using System.IO;
using System.Text;

namespace HQCentral.Debugging
{
    internal static class HQCentralFileLogger
    {
        private const string PreferredLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

        private static readonly object Sync = new object();
        private static readonly string ResolvedLogDirectory = ResolveLogDirectory();

        public static string LogPath => Path.Combine(ResolvedLogDirectory, "HQCentral.log");

        public static string UiLogPath => Path.Combine(ResolvedLogDirectory, "HQCentral-ui.log");

        public static string DataLogPath => Path.Combine(ResolvedLogDirectory, "HQCentral-data.log");

        public static void StartSession()
        {
            Append(LogPath, $"===== HQCentral session started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
        }

        public static void Info(string message)
        {
            Append(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] INFO  {message}");
        }

        public static void Error(string message, Exception exception)
        {
            Append(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR {message}{Environment.NewLine}{exception}");
        }

        public static void AppendUiSnapshot(StringBuilder snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            Append(UiLogPath, snapshot.ToString());
        }

        public static void AppendDataSnapshot(StringBuilder snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            Append(DataLogPath, snapshot.ToString());
        }

        private static void Append(string path, string message)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ResolvedLogDirectory);
                File.AppendAllText(path, message + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static string ResolveLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(PreferredLogDirectory);
                return PreferredLogDirectory;
            }
            catch
            {
                var fallback = Path.Combine(Path.GetTempPath(), "HQCentral", "Logs");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }
    }
}
