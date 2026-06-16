#nullable enable
using UnityEngine;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.UI
{
    public sealed class VehicleRuntimeTunerLayoutBuffer
    {
        public string FrontLeftWheelX = string.Empty;
        public string FrontLeftWheelY = string.Empty;
        public string FrontLeftWheelZ = string.Empty;
        public string FrontRightWheelX = string.Empty;
        public string FrontRightWheelY = string.Empty;
        public string FrontRightWheelZ = string.Empty;
        public string RearLeftWheelX = string.Empty;
        public string RearLeftWheelY = string.Empty;
        public string RearLeftWheelZ = string.Empty;
        public string RearRightWheelX = string.Empty;
        public string RearRightWheelY = string.Empty;
        public string RearRightWheelZ = string.Empty;

        public string FrontLeftControllerX = string.Empty;
        public string FrontLeftControllerY = string.Empty;
        public string FrontLeftControllerZ = string.Empty;
        public string FrontRightControllerX = string.Empty;
        public string FrontRightControllerY = string.Empty;
        public string FrontRightControllerZ = string.Empty;
        public string RearLeftControllerX = string.Empty;
        public string RearLeftControllerY = string.Empty;
        public string RearLeftControllerZ = string.Empty;
        public string RearRightControllerX = string.Empty;
        public string RearRightControllerY = string.Empty;
        public string RearRightControllerZ = string.Empty;

        public string BodyColliderX = string.Empty;
        public string BodyColliderY = string.Empty;
        public string BodyColliderZ = string.Empty;
        public string BodyX = string.Empty;
        public string BodyY = string.Empty;
        public string BodyZ = string.Empty;
        public string PaintX = string.Empty;
        public string PaintY = string.Empty;
        public string PaintZ = string.Empty;

        public void SyncFromVehicle(ActiveVehicleInfo activeVehicle)
        {
            SyncVector3(activeVehicle.FrontLeftWheelVisual, ref FrontLeftWheelX, ref FrontLeftWheelY, ref FrontLeftWheelZ);
            SyncVector3(activeVehicle.FrontRightWheelVisual, ref FrontRightWheelX, ref FrontRightWheelY, ref FrontRightWheelZ);
            SyncVector3(activeVehicle.RearLeftWheelVisual, ref RearLeftWheelX, ref RearLeftWheelY, ref RearLeftWheelZ);
            SyncVector3(activeVehicle.RearRightWheelVisual, ref RearRightWheelX, ref RearRightWheelY, ref RearRightWheelZ);

            SyncVector3(activeVehicle.FrontLeftWheelController, ref FrontLeftControllerX, ref FrontLeftControllerY, ref FrontLeftControllerZ);
            SyncVector3(activeVehicle.FrontRightWheelController, ref FrontRightControllerX, ref FrontRightControllerY, ref FrontRightControllerZ);
            SyncVector3(activeVehicle.RearLeftWheelController, ref RearLeftControllerX, ref RearLeftControllerY, ref RearLeftControllerZ);
            SyncVector3(activeVehicle.RearRightWheelController, ref RearRightControllerX, ref RearRightControllerY, ref RearRightControllerZ);

            SyncVector3(activeVehicle.BodyColliderTransform, ref BodyColliderX, ref BodyColliderY, ref BodyColliderZ);
            SyncVector3(activeVehicle.BodyTransform, ref BodyX, ref BodyY, ref BodyZ);
            SyncVector3(activeVehicle.PaintTransform, ref PaintX, ref PaintY, ref PaintZ);
        }

        public void FillEmptyFieldsFromDefaults(VehicleRuntimeTunerLayoutDefaults defaults)
        {
            ApplyDefaultIfEmpty(ref FrontLeftWheelX, defaults.FrontLeftWheelX);
            ApplyDefaultIfEmpty(ref FrontLeftWheelY, defaults.FrontLeftWheelY);
            ApplyDefaultIfEmpty(ref FrontLeftWheelZ, defaults.FrontLeftWheelZ);
            ApplyDefaultIfEmpty(ref FrontRightWheelX, defaults.FrontRightWheelX);
            ApplyDefaultIfEmpty(ref FrontRightWheelY, defaults.FrontRightWheelY);
            ApplyDefaultIfEmpty(ref FrontRightWheelZ, defaults.FrontRightWheelZ);
            ApplyDefaultIfEmpty(ref RearLeftWheelX, defaults.RearLeftWheelX);
            ApplyDefaultIfEmpty(ref RearLeftWheelY, defaults.RearLeftWheelY);
            ApplyDefaultIfEmpty(ref RearLeftWheelZ, defaults.RearLeftWheelZ);
            ApplyDefaultIfEmpty(ref RearRightWheelX, defaults.RearRightWheelX);
            ApplyDefaultIfEmpty(ref RearRightWheelY, defaults.RearRightWheelY);
            ApplyDefaultIfEmpty(ref RearRightWheelZ, defaults.RearRightWheelZ);
            ApplyDefaultIfEmpty(ref FrontLeftControllerX, defaults.FrontLeftControllerX);
            ApplyDefaultIfEmpty(ref FrontLeftControllerY, defaults.FrontLeftControllerY);
            ApplyDefaultIfEmpty(ref FrontLeftControllerZ, defaults.FrontLeftControllerZ);
            ApplyDefaultIfEmpty(ref FrontRightControllerX, defaults.FrontRightControllerX);
            ApplyDefaultIfEmpty(ref FrontRightControllerY, defaults.FrontRightControllerY);
            ApplyDefaultIfEmpty(ref FrontRightControllerZ, defaults.FrontRightControllerZ);
            ApplyDefaultIfEmpty(ref RearLeftControllerX, defaults.RearLeftControllerX);
            ApplyDefaultIfEmpty(ref RearLeftControllerY, defaults.RearLeftControllerY);
            ApplyDefaultIfEmpty(ref RearLeftControllerZ, defaults.RearLeftControllerZ);
            ApplyDefaultIfEmpty(ref RearRightControllerX, defaults.RearRightControllerX);
            ApplyDefaultIfEmpty(ref RearRightControllerY, defaults.RearRightControllerY);
            ApplyDefaultIfEmpty(ref RearRightControllerZ, defaults.RearRightControllerZ);
            ApplyDefaultIfEmpty(ref BodyColliderX, defaults.BodyColliderX);
            ApplyDefaultIfEmpty(ref BodyColliderY, defaults.BodyColliderY);
            ApplyDefaultIfEmpty(ref BodyColliderZ, defaults.BodyColliderZ);
            ApplyDefaultIfEmpty(ref BodyX, defaults.BodyX);
            ApplyDefaultIfEmpty(ref BodyY, defaults.BodyY);
            ApplyDefaultIfEmpty(ref BodyZ, defaults.BodyZ);
            ApplyDefaultIfEmpty(ref PaintX, defaults.PaintX);
            ApplyDefaultIfEmpty(ref PaintY, defaults.PaintY);
            ApplyDefaultIfEmpty(ref PaintZ, defaults.PaintZ);
        }

        public void Apply(ActiveVehicleInfo activeVehicle)
        {
            ApplyVector3(activeVehicle.FrontLeftWheelVisual, FrontLeftWheelX, FrontLeftWheelY, FrontLeftWheelZ);
            ApplyVector3(activeVehicle.FrontRightWheelVisual, FrontRightWheelX, FrontRightWheelY, FrontRightWheelZ);
            ApplyVector3(activeVehicle.RearLeftWheelVisual, RearLeftWheelX, RearLeftWheelY, RearLeftWheelZ);
            ApplyVector3(activeVehicle.RearRightWheelVisual, RearRightWheelX, RearRightWheelY, RearRightWheelZ);
            ApplyVector3(activeVehicle.FrontLeftWheelController, FrontLeftControllerX, FrontLeftControllerY, FrontLeftControllerZ);
            ApplyVector3(activeVehicle.FrontRightWheelController, FrontRightControllerX, FrontRightControllerY, FrontRightControllerZ);
            ApplyVector3(activeVehicle.RearLeftWheelController, RearLeftControllerX, RearLeftControllerY, RearLeftControllerZ);
            ApplyVector3(activeVehicle.RearRightWheelController, RearRightControllerX, RearRightControllerY, RearRightControllerZ);
            ApplyVector3(activeVehicle.BodyColliderTransform, BodyColliderX, BodyColliderY, BodyColliderZ);
            ApplyVector3(activeVehicle.BodyTransform, BodyX, BodyY, BodyZ);
            ApplyVector3(activeVehicle.PaintTransform, PaintX, PaintY, PaintZ);
        }

        private static void SyncVector3(Transform? transform, ref string x, ref string y, ref string z)
        {
            if (transform == null)
                return;

            var localPosition = transform.localPosition;
            x = localPosition.x.ToString("0.###", InvariantParsing.Culture);
            y = localPosition.y.ToString("0.###", InvariantParsing.Culture);
            z = localPosition.z.ToString("0.###", InvariantParsing.Culture);
        }

        private static void ApplyVector3(Transform? transform, string xText, string yText, string zText)
        {
            if (transform == null)
                return;

            if (!InvariantParsing.TryParseFloat(xText, out var x) ||
                !InvariantParsing.TryParseFloat(yText, out var y) ||
                !InvariantParsing.TryParseFloat(zText, out var z))
                return;

            transform.localPosition = new Vector3(x, y, z);
        }

        private static void ApplyDefaultIfEmpty(ref string value, string defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(defaultValue))
                value = defaultValue;
        }
    }
}
