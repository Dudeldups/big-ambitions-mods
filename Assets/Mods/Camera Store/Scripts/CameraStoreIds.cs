#nullable enable

namespace CameraStore
{
    /// <summary>
    /// Public integration contract for Camera Store. These IDs are save-facing and must remain stable.
    /// </summary>
    public static class CameraStoreIds
    {
        public const string ModId = "Camera Store";
        public const string BundleKey = "AssetBundles/camerastore-businesstype.unity3d";
        public const string BusinessType = "camerastore:business_camera_store";

        public const string CompactCamera = "camerastore:item_compact_camera";
        public const string DslrCamera = "camerastore:item_dslr_camera";
        public const string ProfessionalCamera = "camerastore:item_professional_camera";
        public const string ActionCamera = "camerastore:item_action_camera";
        public const string CameraLens = "camerastore:item_camera_lens";
        public const string Tripod = "camerastore:item_tripod";
        public const string CameraFlash = "camerastore:item_camera_flash";
        public const string CameraBag = "camerastore:item_camera_bag";

        public const string CameraDisplay = "camerastore:item_camera_display";
        public const string CameraAccessoriesShelf = "camerastore:item_camera_accessories_shelf";

        public static readonly string[] Products =
        {
            CompactCamera,
            DslrCamera,
            ProfessionalCamera,
            ActionCamera,
            CameraLens,
            Tripod,
            CameraFlash,
            CameraBag
        };

        public static readonly string[] Furniture =
        {
            CameraDisplay,
            CameraAccessoriesShelf
        };

        public static readonly string[] CameraDisplayProducts =
        {
            CompactCamera,
            DslrCamera,
            ProfessionalCamera,
            ActionCamera,
            CameraLens,
            CameraFlash
        };

        public static readonly string[] AccessoriesShelfProducts =
        {
            CameraLens,
            Tripod,
            CameraFlash,
            CameraBag
        };
    }
}
