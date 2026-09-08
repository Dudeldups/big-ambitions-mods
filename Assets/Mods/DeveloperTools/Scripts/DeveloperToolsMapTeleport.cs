#nullable enable
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsMapTeleport
    {
        private const float DoubleClickSeconds = 0.35f;
        private const float MaximumPixelDriftSquared = 144f;
        private static readonly FieldInfo? WasClickingUiField = typeof(CityMapCam).GetField(
            "_wasClickingUI", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly ModContext context;
        private readonly DeveloperToolsPlayerService playerService;
        private CityMapCam? mapCameraController;
        private bool wasOpen;
        private float lastClickAt = -10f;
        private Vector3 lastClickPosition;

        public DeveloperToolsMapTeleport(ModContext context, DeveloperToolsPlayerService playerService)
        {
            this.context = context;
            this.playerService = playerService;
        }

        public void Update(bool pointerBlockedByTestingUi)
        {
            var open = CityMap.IsOpen;
            if (open != wasOpen)
            {
                lastClickAt = -10f;
                wasOpen = open;
                mapCameraController = open ? Object.FindObjectOfType<CityMapCam>() : null;
            }
            if (!open || !Input.GetMouseButtonDown(0) || pointerBlockedByTestingUi || WasMapUiClicked())
                return;

            var mouse = Input.mousePosition;
            var isDoubleClick = Time.unscaledTime - lastClickAt <= DoubleClickSeconds &&
                                (mouse - lastClickPosition).sqrMagnitude <= MaximumPixelDriftSquared;
            lastClickAt = Time.unscaledTime;
            lastClickPosition = mouse;
            if (!isDoubleClick)
                return;

            var camera = GameManager.GetMainCamera();
            if (camera == null)
            {
                context.Logger.Warn("DeveloperTools: map double-click ignored; main camera unavailable.");
                return;
            }

            var ray = camera.ScreenPointToRay(mouse);
            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out var distance))
            {
                context.Logger.Warn("DeveloperTools: map double-click ignored; camera ray did not intersect the world plane.");
                return;
            }

            var requested = ray.GetPoint(distance);
            if (!playerService.Teleport(requested, true, out _, out _))
                return;

            var cityMap = Object.FindObjectOfType<CityMap>();
            cityMap?.Close();
        }

        private bool WasMapUiClicked()
        {
            if (mapCameraController == null || WasClickingUiField == null)
                return false;
            try
            {
                return WasClickingUiField.GetValue(mapCameraController) as bool? ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
