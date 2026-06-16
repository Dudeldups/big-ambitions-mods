#nullable enable
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.Vehicle;
using UnityEngine;

namespace VehicleRuntimeTuner.Applying
{
    public sealed class VehicleTuningApplier
    {
        private readonly RigidbodyTuningApplier rigidbodyApplier = new RigidbodyTuningApplier();
        private readonly WheelColliderTuningApplier wheelColliderApplier = new WheelColliderTuningApplier();
        private readonly ReflectionTuningApplier reflectionTuningApplier = new ReflectionTuningApplier();

        public int LastRuntimeScalarWriteCount => reflectionTuningApplier.LastRuntimeScalarWriteCount;
        public int LastWheelStructWriteCount => reflectionTuningApplier.LastWheelStructWriteCount;

        public void Apply(ActiveVehicleInfo activeVehicle, VehicleTuningProfile profile)
        {
            rigidbodyApplier.Apply(activeVehicle, profile);
            wheelColliderApplier.Apply(activeVehicle.WheelColliders, profile);
            reflectionTuningApplier.Apply(activeVehicle, profile);

            if (activeVehicle.VehicleType == null)
                return;

            if (profile.engine.enginePower.hasValue)
                activeVehicle.VehicleType.enginePower = profile.engine.enginePower.value;
            if (profile.engine.maxSpeed.hasValue)
                activeVehicle.VehicleType.maxSpeed = Mathf.RoundToInt(profile.engine.maxSpeed.value);
            if (profile.brakes.brakeTorque.hasValue)
                activeVehicle.VehicleType.brakeForce = Mathf.RoundToInt(profile.brakes.brakeTorque.value);
        }
    }
}
