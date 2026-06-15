#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private const float TopbarDiagLoadingUiPinkStrength = 0.22f;
        private static readonly Color TopbarDiagLoadingUiPink = new Color(0.62f, 0.08f, 0.38f, 1f);
        private static readonly Dictionary<int, TopbarDiagLoadingUiSnapshot> TopbarDiagLoadingUiPatchedGraphics = new Dictionary<int, TopbarDiagLoadingUiSnapshot>();

        internal static void ApplyTopbarDiagLoadingUiTintPass()
        {
            TryTintTopbarDiagLoadingUiGraphic("LoadingScreen(Clone)", "Background");
            TryTintTopbarDiagLoadingUiGraphic("LoadingScreen(Clone)", "ProgressBar/BarIndicator/Background");
            TryTintTopbarDiagLoadingUiGraphic("LoadingSpinner", "Panel");
        }

        private static void TryTintTopbarDiagLoadingUiGraphic(string rootName, string relativePath)
        {
            var root = GameObject.Find(rootName);
            if (root == null)
                return;

            var target = root.transform.Find(relativePath);
            if (target == null)
                return;

            var graphic = target.GetComponent<Graphic>();
            if (graphic == null)
                return;

            var id = graphic.GetInstanceID();
            if (TopbarDiagLoadingUiPatchedGraphics.ContainsKey(id))
                return;

            TopbarDiagLoadingUiPatchedGraphics[id] = new TopbarDiagLoadingUiSnapshot(graphic, graphic.color);
            graphic.color = BlendTopbarDiagLoadingUiColor(graphic.color);
        }

        private static Color BlendTopbarDiagLoadingUiColor(Color original)
        {
            var target = new Color(TopbarDiagLoadingUiPink.r, TopbarDiagLoadingUiPink.g, TopbarDiagLoadingUiPink.b, original.a);
            var blended = Color.Lerp(original, target, TopbarDiagLoadingUiPinkStrength);
            blended.a = original.a;
            return blended;
        }

        private static int RestoreTopbarDiagLoadingUiTint()
        {
            var restored = 0;
            foreach (var snapshot in TopbarDiagLoadingUiPatchedGraphics.Values)
            {
                if (snapshot.Graphic == null)
                    continue;

                snapshot.Graphic.color = snapshot.OriginalColor;
                restored++;
            }

            TopbarDiagLoadingUiPatchedGraphics.Clear();
            return restored;
        }

        private static void ResetTopbarDiagLoadingUiTintState()
        {
            TopbarDiagLoadingUiPatchedGraphics.Clear();
        }

        private readonly struct TopbarDiagLoadingUiSnapshot
        {
            internal TopbarDiagLoadingUiSnapshot(Graphic graphic, Color originalColor)
            {
                Graphic = graphic;
                OriginalColor = originalColor;
            }

            internal Graphic Graphic { get; }
            internal Color OriginalColor { get; }
        }
    }
}
