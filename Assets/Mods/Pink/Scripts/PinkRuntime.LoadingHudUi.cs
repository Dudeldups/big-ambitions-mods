#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private const float ExplicitUiPinkStrength = 0.32f;
        private static readonly Color ExplicitUiPink = new Color(0.62f, 0.08f, 0.38f, 1f);
        private static readonly Dictionary<int, ExplicitUiGraphicSnapshot> ExplicitUiPatchedGraphics = new Dictionary<int, ExplicitUiGraphicSnapshot>();

        internal static int ApplyLoadingUiTintPass()
        {
            var patched = 0;
            patched += TryTintExplicitUiGraphic("LoadingScreen(Clone)", "Background");
            patched += TryTintExplicitUiGraphic("LoadingScreen(Clone)", "ProgressBar/BarIndicator/Background");
            patched += TryTintExplicitUiGraphic("LoadingSpinner", "Panel");
            return patched;
        }

        internal static bool ApplyMainHudUiTintPass()
        {
            var foundMainBarTargets = 0;
            var foundTaskTargets = 0;
            var foundSmartphoneTargets = 0;

            // The visible HUD chrome is mostly white-tinted sprite imagery, not dark Image.color values.
            // These targets came from TOPBAR_DIAG_V2.
            if (TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar", out _))
                foundMainBarTargets++;

            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/Avatar", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/Avatar/DanceButton/Background", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/Avatar/AccessoriesButton", out _);

            // Keep stat backgrounds as secondary targets. They are mostly hidden behind the colored fillers,
            // but still useful when a bar is not full.
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/PlayerStats/Hunger", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/PlayerStats/Energy", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Left Bar/PlayerStats/Happiness", out _);

            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/NotificationsButton", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/HelpButton", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/ReportBugButton", out _);
            TryTintExplicitUiGraphic("Canvases", "Topbar/Container/Buttons/SettingsButton", out _);

            // Objectives/tasks panel. The F9 log showed Canvases/Tasks labels/icons; the actual visible
            // panel chrome is likely a white-tinted sprite like the topbar, so target the panel containers,
            // not labels/icons.
            // Objectives/tasks body only. Do NOT tint Tasks/.../Headline: the header is intended to stay bright.
            if (TryTintExplicitUiGraphic("Canvases", "Tasks/Container/Panel", out _))
                foundTaskTargets++;
            TryTintExplicitUiGraphic("Canvases", "Tasks/ExpandButton/Background", out _);

            // BizPhone / smartphone body only. Do NOT tint Smartphone/.../Headline: the header is intended to stay bright.
            if (TryTintExplicitUiGraphic("Canvases", "Smartphone/Container/Phone", out _))
                foundSmartphoneTargets++;
            TryTintExplicitUiGraphic("Canvases", "Smartphone/Container/Phone/Radio", out _);
            TryTintExplicitUiGraphic("Canvases", "Smartphone/Container/Phone/Radio/Splitter", out _);

            PinkFileLogger.Info($"MAIN_HUD_UI_TINT_PASS foundMainBarTargets={foundMainBarTargets}/1 foundTaskTargets={foundTaskTargets}/1 foundSmartphoneTargets={foundSmartphoneTargets}/1");
            return foundMainBarTargets >= 1;
        }

        private static int TryTintExplicitUiGraphic(string rootName, string relativePath)
        {
            return TryTintExplicitUiGraphic(rootName, relativePath, out _) ? 1 : 0;
        }

        private static bool TryTintExplicitUiGraphic(string rootName, string relativePath, out bool patched)
        {
            patched = false;

            var fullPath = string.IsNullOrEmpty(relativePath) ? rootName : rootName + "/" + relativePath;
            Transform? target = null;

            var direct = GameObject.Find(fullPath);
            if (direct != null)
            {
                target = direct.transform;
            }
            else
            {
                var root = GameObject.Find(rootName);
                if (root != null)
                    target = string.IsNullOrEmpty(relativePath) ? root.transform : root.transform.Find(relativePath);
            }

            if (target == null)
            {
                PinkFileLogger.Verbose($"Explicit UI target not found: {fullPath}");
                return false;
            }

            var graphic = target.GetComponent<Graphic>();
            if (graphic == null)
            {
                PinkFileLogger.Verbose($"Explicit UI target has no Graphic: {fullPath}");
                return false;
            }

            var id = graphic.GetInstanceID();
            if (ExplicitUiPatchedGraphics.TryGetValue(id, out var existingSnapshot))
            {
                // Vanilla can overwrite UI colors after we set them. Re-apply from the original snapshot.
                graphic.color = BlendExplicitUiColor(existingSnapshot.OriginalColor);
                return true;
            }

            ExplicitUiPatchedGraphics[id] = new ExplicitUiGraphicSnapshot(graphic, graphic.color);
            graphic.color = BlendExplicitUiColor(graphic.color);
            PinkFileLogger.Info($"EXPLICIT_UI_TINT patched path={fullPath}, graphic={graphic.GetType().Name}, name={graphic.name}");
            patched = true;
            return true;
        }

        private static Color BlendExplicitUiColor(Color original)
        {
            var target = new Color(ExplicitUiPink.r, ExplicitUiPink.g, ExplicitUiPink.b, original.a);
            var blended = Color.Lerp(original, target, ExplicitUiPinkStrength);
            blended.a = original.a;
            return blended;
        }

        private static int RestoreExplicitUiTint()
        {
            var restored = 0;
            foreach (var snapshot in ExplicitUiPatchedGraphics.Values)
            {
                if (snapshot.Graphic == null)
                    continue;

                snapshot.Graphic.color = snapshot.OriginalColor;
                restored++;
            }

            ExplicitUiPatchedGraphics.Clear();
            return restored;
        }

        private static void ResetExplicitUiTintState()
        {
            ExplicitUiPatchedGraphics.Clear();
        }

        private readonly struct ExplicitUiGraphicSnapshot
        {
            internal ExplicitUiGraphicSnapshot(Graphic graphic, Color originalColor)
            {
                Graphic = graphic;
                OriginalColor = originalColor;
            }

            internal Graphic Graphic { get; }
            internal Color OriginalColor { get; }
        }
    }
}
