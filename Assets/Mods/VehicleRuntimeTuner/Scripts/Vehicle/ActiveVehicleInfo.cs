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
        public Transform? FrontLeftWheelVisual { get; set; }
        public Transform? FrontRightWheelVisual { get; set; }
        public Transform? RearLeftWheelVisual { get; set; }
        public Transform? RearRightWheelVisual { get; set; }
        public Transform? FrontLeftWheelController { get; set; }
        public Transform? FrontRightWheelController { get; set; }
        public Transform? RearLeftWheelController { get; set; }
        public Transform? RearRightWheelController { get; set; }
        public Transform? BodyColliderTransform { get; set; }
        public Transform? BodyTransform { get; set; }
        public Transform? PaintTransform { get; set; }
    }
}
