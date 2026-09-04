#nullable enable
using System;
using System.IO;

namespace BigHax
{
    /// <summary>
    /// Temporary, narrowly scoped diagnostics for the vehicle condition hax.
    /// Remove this file once the collision/refuel issue is resolved.
    /// </summary>
    internal static class BigHaxVehicleConditionDebugLog
    {
        private const string LogPath = @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods\BigHax-vehicle-conditions-debug.log";

        public static void Write(string message)
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(LogPath, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never affect gameplay or mod loading.
            }
        }
    }
}
