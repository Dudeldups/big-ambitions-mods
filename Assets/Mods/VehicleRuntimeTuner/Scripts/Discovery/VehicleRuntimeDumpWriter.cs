#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Discovery
{
    public sealed class VehicleRuntimeDumpWriter
    {
        public string WriteDump(ActiveVehicleInfo activeVehicle, IReadOnlyList<VehicleMemberSnapshot> snapshots)
        {
            Directory.CreateDirectory(VehicleRuntimeTunerPaths.DumpsDirectory);

            var vehicleTypeName = VehicleRuntimeTunerPaths.SanitizeFileName(activeVehicle.VehicleTypeName);
            var fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{vehicleTypeName}_runtime-dump.md";
            var path = Path.Combine(VehicleRuntimeTunerPaths.DumpsDirectory, fileName);

            var builder = new StringBuilder();
            builder.AppendLine("# Vehicle Runtime Dump");
            builder.AppendLine();
            builder.AppendLine($"- Vehicle Type: `{activeVehicle.VehicleTypeName}`");
            builder.AppendLine($"- Vehicle Instance Id: `{activeVehicle.VehicleInstanceId}`");
            builder.AppendLine($"- Root: `{VehicleRuntimeTunerReflection.GetGameObjectPath(activeVehicle.Root?.transform)}`");
            builder.AppendLine();

            if (activeVehicle.Rigidbody != null)
            {
                builder.AppendLine("## Rigidbody");
                builder.AppendLine($"- mass = {activeVehicle.Rigidbody.mass}");
                builder.AppendLine($"- drag = {activeVehicle.Rigidbody.drag}");
                builder.AppendLine($"- angularDrag = {activeVehicle.Rigidbody.angularDrag}");
                builder.AppendLine($"- centerOfMass = {activeVehicle.Rigidbody.centerOfMass}");
                builder.AppendLine();
            }

            foreach (var wheelCollider in activeVehicle.WheelColliders)
            {
                if (wheelCollider == null)
                    continue;

                var spring = wheelCollider.suspensionSpring;
                builder.AppendLine($"## WheelCollider: {wheelCollider.name}");
                builder.AppendLine($"- path = `{VehicleRuntimeTunerReflection.GetGameObjectPath(wheelCollider.transform)}`");
                builder.AppendLine($"- localPosition = {wheelCollider.transform.localPosition}");
                builder.AppendLine($"- radius = {wheelCollider.radius}");
                builder.AppendLine($"- suspensionDistance = {wheelCollider.suspensionDistance}");
                builder.AppendLine($"- suspensionSpring.spring = {spring.spring}");
                builder.AppendLine($"- suspensionSpring.damper = {spring.damper}");
                builder.AppendLine($"- suspensionSpring.targetPosition = {spring.targetPosition}");
                builder.AppendLine();
            }

            foreach (var group in snapshots.GroupBy(x => $"{x.ComponentTypeName} @ {x.ComponentPath}"))
            {
                builder.AppendLine($"## Component: {group.Key}");
                foreach (var snapshot in group.OrderBy(x => x.MemberName, StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine($"- {snapshot.MemberName} = {snapshot.ValueText}");
                builder.AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
