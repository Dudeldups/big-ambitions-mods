#nullable enable
using UnityEngine;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.UI
{
    public sealed class VehicleRuntimeTunerLayoutDefaults
    {
        public string VehicleInstanceId = string.Empty;
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

        public void Capture(ActiveVehicleInfo activeVehicle)
        {
            VehicleInstanceId = activeVehicle.VehicleInstanceId;
            Capture(activeVehicle.FrontLeftWheelVisual, ref FrontLeftWheelX, ref FrontLeftWheelY, ref FrontLeftWheelZ);
            Capture(activeVehicle.FrontRightWheelVisual, ref FrontRightWheelX, ref FrontRightWheelY, ref FrontRightWheelZ);
            Capture(activeVehicle.RearLeftWheelVisual, ref RearLeftWheelX, ref RearLeftWheelY, ref RearLeftWheelZ);
            Capture(activeVehicle.RearRightWheelVisual, ref RearRightWheelX, ref RearRightWheelY, ref RearRightWheelZ);
            Capture(activeVehicle.FrontLeftWheelController, ref FrontLeftControllerX, ref FrontLeftControllerY, ref FrontLeftControllerZ);
            Capture(activeVehicle.FrontRightWheelController, ref FrontRightControllerX, ref FrontRightControllerY, ref FrontRightControllerZ);
            Capture(activeVehicle.RearLeftWheelController, ref RearLeftControllerX, ref RearLeftControllerY, ref RearLeftControllerZ);
            Capture(activeVehicle.RearRightWheelController, ref RearRightControllerX, ref RearRightControllerY, ref RearRightControllerZ);
            Capture(activeVehicle.BodyColliderTransform, ref BodyColliderX, ref BodyColliderY, ref BodyColliderZ);
            Capture(activeVehicle.BodyTransform, ref BodyX, ref BodyY, ref BodyZ);
            Capture(activeVehicle.PaintTransform, ref PaintX, ref PaintY, ref PaintZ);
        }

        private static void Capture(Transform? transform, ref string x, ref string y, ref string z)
        {
            if (transform == null)
                return;

            var localPosition = transform.localPosition;
            x = localPosition.x.ToString("0.###", InvariantParsing.Culture);
            y = localPosition.y.ToString("0.###", InvariantParsing.Culture);
            z = localPosition.z.ToString("0.###", InvariantParsing.Culture);
        }
    }
}
