#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        internal static void LogTopbarDiagnostics()
        {
            var root = GameObject.Find("Canvases/Topbar");
            if (root == null)
            {
                PinkFileLogger.Info("TOPBAR_DIAG root not found: Canvases/Topbar");
                return;
            }

            var graphics = root.GetComponentsInChildren<Graphic>(true);
            PinkFileLogger.Info($"TOPBAR_DIAG_START root={GetPath(root.transform, 8)}, graphics={graphics.Length}");

            for (var index = 0; index < graphics.Length; index++)
            {
                var graphic = graphics[index];
                if (graphic == null)
                    continue;

                var rect = graphic.transform as RectTransform;
                var rectSize = rect != null ? rect.rect.size.ToString() : "<no-rect>";
                var color = graphic.color;

                PinkFileLogger.Info(
                    $"TOPBAR_DIAG {index + 1}/{graphics.Length}: type={graphic.GetType().Name}, name={graphic.name}, " +
                    $"activeSelf={graphic.gameObject.activeSelf}, activeInHierarchy={graphic.gameObject.activeInHierarchy}, enabled={graphic.enabled}, " +
                    $"raycast={graphic.raycastTarget}, color={ColorToHexTopbar(color)}, rgba=({color.r:0.000},{color.g:0.000},{color.b:0.000},{color.a:0.000}), " +
                    $"rectSize={rectSize}, path={GetPath(graphic.transform, 16)}");
            }

            PinkFileLogger.Info("TOPBAR_DIAG_END");
        }

        private static string ColorToHexTopbar(Color color)
        {
            var r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            var g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            var b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            var a = Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }
    }
}
