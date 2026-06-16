#nullable enable
using System.IO;
using System.Text;
using UnityEngine;

namespace VehicleRuntimeTuner.Utils
{
    public static class VehicleRuntimeTunerPaths
    {
        public static string RootDirectory => Path.Combine(Application.persistentDataPath, "VehicleRuntimeTuner");
        public static string ProfilesDirectory => Path.Combine(RootDirectory, "Profiles");
        public static string DumpsDirectory => Path.Combine(RootDirectory, "Dumps");
        public static string ExportsDirectory => Path.Combine(RootDirectory, "Exports");
        public static string LogFilePath => Path.Combine(RootDirectory, "vehicle-runtime-tuner.log");

        public static string GetProfilePath(string vehicleTypeName)
        {
            Directory.CreateDirectory(ProfilesDirectory);
            return Path.Combine(ProfilesDirectory, $"{SanitizeFileName(vehicleTypeName)}.json");
        }

        public static string SanitizeFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown-vehicle";

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (System.Array.IndexOf(invalidChars, ch) >= 0 || ch == ':' || ch == '/' || ch == '\\')
                    builder.Append('_');
                else
                    builder.Append(ch);
            }

            return builder.ToString();
        }
    }
}
