#nullable enable
using UnityEngine;
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.Utils;

namespace VehicleRuntimeTuner.UI
{
    public sealed class VehicleRuntimeTunerFieldBuffer
    {
        public string Mass = string.Empty;
        public string Drag = string.Empty;
        public string AngularDrag = string.Empty;
        public string CenterOfMassX = string.Empty;
        public string CenterOfMassY = string.Empty;
        public string CenterOfMassZ = string.Empty;
        public string EnginePower = string.Empty;
        public string MaxSpeed = string.Empty;
        public string BrakeTorque = string.Empty;
        public string FrontSpring = string.Empty;
        public string FrontDamper = string.Empty;
        public string FrontTarget = string.Empty;
        public string FrontSuspensionDistance = string.Empty;
        public string RearSpring = string.Empty;
        public string RearDamper = string.Empty;
        public string RearTarget = string.Empty;
        public string RearSuspensionDistance = string.Empty;
        public string FrontRadius = string.Empty;
        public string RearRadius = string.Empty;
        public string FrontWidth = string.Empty;
        public string RearWidth = string.Empty;

        public void SyncFromProfile(VehicleTuningProfile profile)
        {
            Mass = InvariantParsing.FormatOptional(profile.body.mass);
            Drag = InvariantParsing.FormatOptional(profile.body.drag);
            AngularDrag = InvariantParsing.FormatOptional(profile.body.angularDrag);
            CenterOfMassX = profile.body.centerOfMass.hasValue ? profile.body.centerOfMass.value.x.ToString(InvariantParsing.Culture) : string.Empty;
            CenterOfMassY = profile.body.centerOfMass.hasValue ? profile.body.centerOfMass.value.y.ToString(InvariantParsing.Culture) : string.Empty;
            CenterOfMassZ = profile.body.centerOfMass.hasValue ? profile.body.centerOfMass.value.z.ToString(InvariantParsing.Culture) : string.Empty;
            EnginePower = InvariantParsing.FormatOptional(profile.engine.enginePower);
            MaxSpeed = InvariantParsing.FormatOptional(profile.engine.maxSpeed);
            BrakeTorque = InvariantParsing.FormatOptional(profile.brakes.brakeTorque);
            FrontSpring = InvariantParsing.FormatOptional(profile.suspension.frontSpring);
            FrontDamper = InvariantParsing.FormatOptional(profile.suspension.frontDamper);
            FrontTarget = InvariantParsing.FormatOptional(profile.suspension.frontTargetPosition);
            FrontSuspensionDistance = InvariantParsing.FormatOptional(profile.suspension.frontSuspensionDistance);
            RearSpring = InvariantParsing.FormatOptional(profile.suspension.rearSpring);
            RearDamper = InvariantParsing.FormatOptional(profile.suspension.rearDamper);
            RearTarget = InvariantParsing.FormatOptional(profile.suspension.rearTargetPosition);
            RearSuspensionDistance = InvariantParsing.FormatOptional(profile.suspension.rearSuspensionDistance);
            FrontRadius = InvariantParsing.FormatOptional(profile.wheels.frontRadius);
            RearRadius = InvariantParsing.FormatOptional(profile.wheels.rearRadius);
            FrontWidth = InvariantParsing.FormatOptional(profile.wheels.frontWidth);
            RearWidth = InvariantParsing.FormatOptional(profile.wheels.rearWidth);
        }

        public VehicleTuningProfile ToProfile(VehicleTuningProfile baseProfile)
        {
            var profile = baseProfile ?? VehicleTuningProfile.CreateDefault();

            InvariantParsing.TryApplyOptionalFloat(Mass, profile.body.mass);
            InvariantParsing.TryApplyOptionalFloat(Drag, profile.body.drag);
            InvariantParsing.TryApplyOptionalFloat(AngularDrag, profile.body.angularDrag);

            var hasX = InvariantParsing.TryParseFloat(CenterOfMassX, out var x);
            var hasY = InvariantParsing.TryParseFloat(CenterOfMassY, out var y);
            var hasZ = InvariantParsing.TryParseFloat(CenterOfMassZ, out var z);
            var hasCenterOfMass = hasX && hasY && hasZ;
            profile.body.centerOfMass.hasValue = hasCenterOfMass;
            if (hasCenterOfMass)
                profile.body.centerOfMass.value = new Vector3(x, y, z);

            InvariantParsing.TryApplyOptionalFloat(EnginePower, profile.engine.enginePower);
            InvariantParsing.TryApplyOptionalFloat(MaxSpeed, profile.engine.maxSpeed);
            InvariantParsing.TryApplyOptionalFloat(BrakeTorque, profile.brakes.brakeTorque);
            InvariantParsing.TryApplyOptionalFloat(FrontSpring, profile.suspension.frontSpring);
            InvariantParsing.TryApplyOptionalFloat(FrontDamper, profile.suspension.frontDamper);
            InvariantParsing.TryApplyOptionalFloat(FrontTarget, profile.suspension.frontTargetPosition);
            InvariantParsing.TryApplyOptionalFloat(FrontSuspensionDistance, profile.suspension.frontSuspensionDistance);
            InvariantParsing.TryApplyOptionalFloat(RearSpring, profile.suspension.rearSpring);
            InvariantParsing.TryApplyOptionalFloat(RearDamper, profile.suspension.rearDamper);
            InvariantParsing.TryApplyOptionalFloat(RearTarget, profile.suspension.rearTargetPosition);
            InvariantParsing.TryApplyOptionalFloat(RearSuspensionDistance, profile.suspension.rearSuspensionDistance);
            InvariantParsing.TryApplyOptionalFloat(FrontRadius, profile.wheels.frontRadius);
            InvariantParsing.TryApplyOptionalFloat(RearRadius, profile.wheels.rearRadius);
            InvariantParsing.TryApplyOptionalFloat(FrontWidth, profile.wheels.frontWidth);
            InvariantParsing.TryApplyOptionalFloat(RearWidth, profile.wheels.rearWidth);

            return profile;
        }
    }
}
