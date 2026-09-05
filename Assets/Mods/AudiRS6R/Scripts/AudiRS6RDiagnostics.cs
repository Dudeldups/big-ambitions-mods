#nullable enable
using System;
using System.IO;
using System.Text;
using BAModAPI;
using UnityEngine;

internal static class AudiRS6RDiagnostics
{
    private const string LogDirectoryName = "AudiRS6R";
    private const string VehicleLogFileName = "audi-rs6r-vehicle.log";
    private const string DamageLogFileName = "audi-rs6r-damage.log";

    private static readonly object Sync = new object();
    private static ModContext? context;
    private static string? logDirectory;

    public static string VehicleLogPath => Path.Combine(GetLogDirectory(), VehicleLogFileName);
    public static string DamageLogPath => Path.Combine(GetLogDirectory(), DamageLogFileName);

    public static void Initialize(ModContext modContext, string vehicleTypeName)
    {
        context = modContext;
        var header =
            $"===== AudiRS6R diagnostic session started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===== " +
            $"modId={modContext.ModId} vehicleType={vehicleTypeName} gameVersion={Application.version} " +
            $"unityVersion={Application.unityVersion} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";

        Write(VehicleLogPath, "SESSION", header);
        Write(DamageLogPath, "SESSION", header);
        modContext.Logger.Info($"AudiRS6R: vehicle diagnostics enabled. Vehicle log: '{VehicleLogPath}'. Damage log: '{DamageLogPath}'.");
    }

    public static void Shutdown()
    {
        Write(VehicleLogPath, "SESSION", "===== AudiRS6R diagnostic session ended =====");
        context = null;
    }

    public static void Vehicle(string category, string message)
    {
        Write(VehicleLogPath, category, message);
    }

    public static void Damage(string message)
    {
        Write(VehicleLogPath, "DAMAGE", message);
        Write(DamageLogPath, "DAMAGE", message);

        try
        {
            context?.Logger.Warn("AudiRS6R damage diagnostic: " + message);
        }
        catch
        {
            // A logging backend must never interfere with vehicle physics.
        }
    }

    public static void Error(string scope, Exception exception)
    {
        var message = $"{scope} failed: {exception.GetType().Name}: {exception.Message}";
        Write(VehicleLogPath, "ERROR", message);

        try
        {
            context?.Logger.Warn("AudiRS6R diagnostics: " + message);
        }
        catch
        {
            // A logging backend must never interfere with vehicle physics.
        }
    }

    private static string GetLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(logDirectory))
            return logDirectory!;

        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(logDirectory))
                return logDirectory!;

            try
            {
                logDirectory = Path.Combine(Application.persistentDataPath, LogDirectoryName);
                Directory.CreateDirectory(logDirectory);
            }
            catch
            {
                logDirectory = Path.Combine(Path.GetTempPath(), LogDirectoryName);
                Directory.CreateDirectory(logDirectory);
            }

            return logDirectory;
        }
    }

    private static void Write(string path, string category, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics are intentionally best-effort.
        }
    }
}
