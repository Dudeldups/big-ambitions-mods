#nullable enable
using System.Collections.Generic;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace VehicleRuntimeTuner.Vehicle
{
    public sealed class ActiveVehicleInfo
    {
        public object? VehicleController { get; set; }
        public GameObject? Root { get; set; }
        public object? VehicleInstance { get; set; }
        public string VehicleInstanceId { get; set; } = string.Empty;
        public string VehicleTypeName { get; set; } = string.Empty;
        public Rigidbody? Rigidbody { get; set; }
        public VehicleType? VehicleType { get; set; }
        public IReadOnlyList<WheelCollider> WheelColliders { get; set; } = new List<WheelCollider>();
        public IReadOnlyList<MonoBehaviour> MonoBehaviours { get; set; } = new List<MonoBehaviour>();
    }
}
