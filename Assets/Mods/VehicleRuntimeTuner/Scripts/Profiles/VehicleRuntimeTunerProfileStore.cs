#nullable enable
using System;
using System.IO;
using System.Text;
using VehicleRuntimeTuner.Utils;

namespace VehicleRuntimeTuner.Profiles
{
    public sealed class VehicleRuntimeTunerProfileStore
    {
        public VehicleTuningProfile? Load(string vehicleTypeName)
        {
            var path = VehicleRuntimeTunerPaths.GetProfilePath(vehicleTypeName);
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return UnityEngine.JsonUtility.FromJson<VehicleTuningProfile>(json);
        }

        public string Save(VehicleTuningProfile profile)
        {
            Directory.CreateDirectory(VehicleRuntimeTunerPaths.ProfilesDirectory);
            var path = VehicleRuntimeTunerPaths.GetProfilePath(profile.vehicleTypeName);

            if (File.Exists(path))
            {
                var backupPath = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Copy(path, backupPath, true);
            }

            var json = UnityEngine.JsonUtility.ToJson(profile, true);
            File.WriteAllText(path, json, Encoding.UTF8);
            return path;
        }
    }
}
