namespace CameraTools
{
    public sealed class CameraToolsSettings
    {
        public bool EnableGameplayTweaks { get; set; } = true;

        public int GameplayMaxZoom { get; set; } = 90;

        public int GameplayDefaultPitch { get; set; } = 35;

        public int GameplayMinPitch { get; set; } = 15;

        public int GameplayMaxPitch { get; set; } = 80;

        public int VehicleMaxZoom { get; set; } = 65;

        public bool EnableCameraToolsDebug { get; set; } = false;

        public bool EnableVehicleDebugLogging { get; set; } = false;

        public bool EnableVehicleDebugOverlay { get; set; } = false;

        public bool EnableMapTopDown { get; set; } = true;

        public int MapPitch { get; set; } = 90;

        public int MapDistance { get; set; } = 800;

        public int MapOrthographicSize { get; set; } = 70;
    }
}
