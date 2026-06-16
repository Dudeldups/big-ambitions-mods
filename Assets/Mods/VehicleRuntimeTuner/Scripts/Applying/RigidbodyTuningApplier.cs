#nullable enable
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.Applying
{
    public sealed class RigidbodyTuningApplier
    {
        public void Apply(ActiveVehicleInfo activeVehicle, VehicleTuningProfile profile)
        {
            var rigidbody = activeVehicle.Rigidbody;
            if (rigidbody == null)
                return;

            if (profile.body.mass.hasValue)
                rigidbody.mass = profile.body.mass.value;
            if (profile.body.drag.hasValue)
                rigidbody.drag = profile.body.drag.value;
            if (profile.body.angularDrag.hasValue)
                rigidbody.angularDrag = profile.body.angularDrag.value;
            if (profile.body.centerOfMass.hasValue)
                rigidbody.centerOfMass = profile.body.centerOfMass.value;
        }
    }
}
