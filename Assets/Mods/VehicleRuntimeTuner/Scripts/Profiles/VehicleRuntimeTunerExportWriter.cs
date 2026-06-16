#nullable enable
using System;
using System.IO;
using System.Text;
using VehicleRuntimeTuner.Utils;

namespace VehicleRuntimeTuner.Profiles
{
    public sealed class VehicleRuntimeTunerExportWriter
    {
        public string Write(VehicleTuningProfile profile)
        {
            Directory.CreateDirectory(VehicleRuntimeTunerPaths.ExportsDirectory);
            var path = Path.Combine(
                VehicleRuntimeTunerPaths.ExportsDirectory,
                $"{VehicleRuntimeTunerPaths.SanitizeFileName(profile.vehicleTypeName)}_unity-values.md");

            var builder = new StringBuilder();
            builder.AppendLine($"# {profile.vehicleTypeName} Final Tuning Values");
            builder.AppendLine();
            builder.AppendLine("Apply these values to the Unity prefab/asset after testing in-game.");
            builder.AppendLine();
            builder.AppendLine("## Body");
            AppendOptional(builder, "Mass", profile.body.mass);
            AppendOptional(builder, "Drag", profile.body.drag);
            AppendOptional(builder, "Angular Drag", profile.body.angularDrag);
            if (profile.body.centerOfMass.hasValue)
                builder.AppendLine($"- Center of Mass: X {profile.body.centerOfMass.value.x}, Y {profile.body.centerOfMass.value.y}, Z {profile.body.centerOfMass.value.z}");
            builder.AppendLine();
            builder.AppendLine("## Engine");
            AppendOptional(builder, "Engine Power", profile.engine.enginePower);
            AppendOptional(builder, "Max Speed", profile.engine.maxSpeed);
            builder.AppendLine();
            builder.AppendLine("## Brakes");
            AppendOptional(builder, "Brake Torque", profile.brakes.brakeTorque);
            AppendOptional(builder, "Handbrake Torque", profile.brakes.handbrakeTorque);
            builder.AppendLine();
            builder.AppendLine("## Suspension");
            AppendOptional(builder, "Front Spring", profile.suspension.frontSpring);
            AppendOptional(builder, "Front Damper", profile.suspension.frontDamper);
            AppendOptional(builder, "Front Target Position", profile.suspension.frontTargetPosition);
            AppendOptional(builder, "Front Suspension Distance", profile.suspension.frontSuspensionDistance);
            AppendOptional(builder, "Rear Spring", profile.suspension.rearSpring);
            AppendOptional(builder, "Rear Damper", profile.suspension.rearDamper);
            AppendOptional(builder, "Rear Target Position", profile.suspension.rearTargetPosition);
            AppendOptional(builder, "Rear Suspension Distance", profile.suspension.rearSuspensionDistance);
            builder.AppendLine();
            builder.AppendLine("## Wheels");
            AppendOptional(builder, "Front Radius", profile.wheels.frontRadius);
            AppendOptional(builder, "Rear Radius", profile.wheels.rearRadius);
            AppendOptional(builder, "Front Width", profile.wheels.frontWidth);
            AppendOptional(builder, "Rear Width", profile.wheels.rearWidth);
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine("- These values were tested at runtime.");
            builder.AppendLine("- Some prefab wiring values may still need manual Unity changes.");

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            return path;
        }

        private static void AppendOptional(StringBuilder builder, string label, OptionalFloat value)
        {
            if (value.hasValue)
                builder.AppendLine($"- {label}: {value.value}");
        }
    }
}
