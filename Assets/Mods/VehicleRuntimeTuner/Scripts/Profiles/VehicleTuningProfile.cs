#nullable enable
using System;
using UnityEngine;

namespace VehicleRuntimeTuner.Profiles
{
    [Serializable]
    public sealed class VehicleTuningProfile
    {
        public string profileName = "Default";
        public string vehicleTypeName = string.Empty;
        public BodyTuning body = new BodyTuning();
        public EngineTuning engine = new EngineTuning();
        public BrakeTuning brakes = new BrakeTuning();
        public SuspensionTuning suspension = new SuspensionTuning();
        public WheelTuning wheels = new WheelTuning();
        public GearboxTuning gearbox = new GearboxTuning();

        public static VehicleTuningProfile CreateDefault()
        {
            return new VehicleTuningProfile();
        }
    }

    [Serializable]
    public sealed class BodyTuning
    {
        public OptionalFloat mass = new OptionalFloat();
        public OptionalFloat drag = new OptionalFloat();
        public OptionalFloat angularDrag = new OptionalFloat();
        public OptionalVector3 centerOfMass = new OptionalVector3();
    }

    [Serializable]
    public sealed class EngineTuning
    {
        public OptionalFloat enginePower = new OptionalFloat();
        public OptionalFloat maxSpeed = new OptionalFloat();
        public OptionalFloat maxRpm = new OptionalFloat();
        public OptionalFloat torqueMultiplier = new OptionalFloat();
    }

    [Serializable]
    public sealed class BrakeTuning
    {
        public OptionalFloat brakeTorque = new OptionalFloat();
        public OptionalFloat handbrakeTorque = new OptionalFloat();
    }

    [Serializable]
    public sealed class SuspensionTuning
    {
        public OptionalFloat frontSpring = new OptionalFloat();
        public OptionalFloat frontDamper = new OptionalFloat();
        public OptionalFloat frontTargetPosition = new OptionalFloat();
        public OptionalFloat frontSuspensionDistance = new OptionalFloat();
        public OptionalFloat rearSpring = new OptionalFloat();
        public OptionalFloat rearDamper = new OptionalFloat();
        public OptionalFloat rearTargetPosition = new OptionalFloat();
        public OptionalFloat rearSuspensionDistance = new OptionalFloat();
    }

    [Serializable]
    public sealed class WheelTuning
    {
        public OptionalFloat frontRadius = new OptionalFloat();
        public OptionalFloat rearRadius = new OptionalFloat();
        public OptionalFloat frontWidth = new OptionalFloat();
        public OptionalFloat rearWidth = new OptionalFloat();
        public OptionalFloat wheelMass = new OptionalFloat();
        public OptionalFloat wheelDampingRate = new OptionalFloat();
    }

    [Serializable]
    public sealed class GearboxTuning
    {
        public OptionalFloat finalDriveRatio = new OptionalFloat();
    }

    [Serializable]
    public sealed class OptionalFloat
    {
        public bool hasValue;
        public float value;
    }

    [Serializable]
    public sealed class OptionalVector3
    {
        public bool hasValue;
        public Vector3 value;
    }
}
