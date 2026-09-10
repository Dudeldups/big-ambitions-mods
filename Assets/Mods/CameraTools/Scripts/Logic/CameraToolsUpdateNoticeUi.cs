#nullable enable
using Localizor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CameraTools
{
    internal sealed class CameraToolsUpdateNoticeUi
    {
        private const int CurrentNoticeVersion = 2;
        private const string SeenVersionPreference = "camera_tools_update_notice_seen_version";
        private const int WindowId = 348723;
        private const float WindowWidth = 540f;
        private const float WindowHeight = 220f;
        private const float WindowMargin = 24f;

        private Rect windowRect = new Rect(0f, 0f, WindowWidth, WindowHeight);
        private string modId = string.Empty;
        private bool isVisible;
        private bool needsCentering = true;

        private Texture2D? solidTexture;
        private Texture2D? windowBackgroundTexture;
        private Texture2D? buttonBackgroundTexture;
        private Texture2D? buttonActiveBackgroundTexture;
        private GameObject? inputBlockerRoot;
        private GUIStyle? windowStyle;
        private GUIStyle? titleStyle;
        private GUIStyle? bodyStyle;
        private GUIStyle? buttonStyle;

        public void Initialize(string currentModId)
        {
            modId = currentModId;
            isVisible = LoadSeenVersion() < CurrentNoticeVersion;
            needsCentering = true;
            ResetStyleCache();
        }

        public void ConsumeGameplayInputIfNeeded()
        {
            if (!ShouldDisplay())
            {
                DestroyInputBlocker();
                return;
            }

            EnsureInputBlocker();
            Input.ResetInputAxes();
        }

        public void OnGui()
        {
            if (!ShouldDisplay())
            {
                DestroyInputBlocker();
                return;
            }

            EnsureInputBlocker();
            EnsureStyles();
            EnsureWindowIsCenteredIfNeeded();

            var previousColor = GUI.color;
            var previousBackgroundColor = GUI.backgroundColor;
            var previousContentColor = GUI.contentColor;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUI.contentColor = Color.white;
                GUI.DrawTexture(windowRect, windowBackgroundTexture!, ScaleMode.StretchToFill, true);
                windowRect = GUI.Window(WindowId, windowRect, _ => DrawWindow(), GUIContent.none, windowStyle!);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.contentColor = previousContentColor;
            }
        }

        public void Shutdown()
        {
            isVisible = false;
            modId = string.Empty;
            DestroyInputBlocker();
        }

        private void DrawWindow()
        {
            GUILayout.BeginVertical();
            GUILayout.Label(Localize("cameratools_update_notice_title"), titleStyle!);
            DrawSeparator();
            GUILayout.Label(Localize("cameratools_update_notification"), bodyStyle!);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var buttonRect = GUILayoutUtility.GetRect(150f, 42f, GUILayout.Width(150f), GUILayout.Height(42f));
            var currentEvent = Event.current;
            var buttonTexture = currentEvent != null &&
                buttonRect.Contains(currentEvent.mousePosition) &&
                (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
                    ? buttonActiveBackgroundTexture
                    : buttonBackgroundTexture;
            GUI.DrawTexture(buttonRect, buttonTexture!, ScaleMode.StretchToFill, true);
            if (GUI.Button(buttonRect, Localize("cameratools_update_notice_got_it"), buttonStyle!))
                Acknowledge();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private bool ShouldDisplay()
        {
            return isVisible && SaveGameManager.Current != null;
        }

        private int LoadSeenVersion()
        {
            var key = GetPreferenceKey();
            return UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetInt(key, 0) : 0;
        }

        private void Acknowledge()
        {
            UnityEngine.PlayerPrefs.SetInt(GetPreferenceKey(), CurrentNoticeVersion);
            UnityEngine.PlayerPrefs.Save();
            isVisible = false;
            DestroyInputBlocker();
        }

        private string GetPreferenceKey()
        {
            return modId + "." + SeenVersionPreference;
        }

        private void DrawSeparator()
        {
            GUILayout.Space(12f);
            var rect = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
            var previousColor = GUI.color;
            GUI.color = new Color(0.78f, 0.82f, 0.87f, 1f);
            GUI.DrawTexture(rect, solidTexture!);
            GUI.color = previousColor;
            GUILayout.Space(12f);
        }

        private void EnsureWindowIsCenteredIfNeeded()
        {
            if (!needsCentering)
                return;

            windowRect.width = Mathf.Min(WindowWidth, Screen.width - (WindowMargin * 2f));
            windowRect.height = Mathf.Min(WindowHeight, Screen.height - (WindowMargin * 2f));
            windowRect.x = Mathf.Max(WindowMargin, (Screen.width - windowRect.width) * 0.5f);
            windowRect.y = Mathf.Max(WindowMargin, (Screen.height - windowRect.height) * 0.5f);
            needsCentering = false;
        }

        private void ResetStyleCache()
        {
            windowStyle = null;
            titleStyle = null;
            bodyStyle = null;
            buttonStyle = null;
        }

        private void EnsureStyles()
        {
            if (solidTexture == null)
            {
                solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solidTexture.SetPixel(0, 0, Color.white);
                solidTexture.Apply();
            }

            windowBackgroundTexture ??= MakeRoundedRectTexture(
                64,
                64,
                new Color(0.97f, 0.97f, 0.98f, 1f),
                14);
            buttonBackgroundTexture ??= MakeRoundedRectTexture(
                48,
                48,
                new Color(0.22f, 0.56f, 0.93f, 1f),
                8);
            buttonActiveBackgroundTexture ??= MakeRoundedRectTexture(
                48,
                48,
                new Color(0.17f, 0.47f, 0.84f, 1f),
                8);
            windowStyle ??= CreateWindowStyle();
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 0, 0, 0)
            };
            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                wordWrap = true,
                richText = true,
                normal = { textColor = new Color(0.10f, 0.12f, 0.16f, 1f) },
                margin = new RectOffset(0, 0, 0, 0)
            };
            buttonStyle ??= CreateButtonStyle();
        }

        private GUIStyle CreateWindowStyle()
        {
            return new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(22, 22, 20, 20),
                border = new RectOffset(14, 14, 14, 14),
                normal = { background = null, textColor = Color.clear },
                hover = { background = null, textColor = Color.clear },
                active = { background = null, textColor = Color.clear },
                focused = { background = null, textColor = Color.clear },
                onNormal = { background = null, textColor = Color.clear },
                onHover = { background = null, textColor = Color.clear },
                onActive = { background = null, textColor = Color.clear },
                onFocused = { background = null, textColor = Color.clear }
            };
        }

        private GUIStyle CreateButtonStyle()
        {
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 40f,
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(8, 8, 8, 8),
                normal = { background = null, textColor = Color.white },
                hover = { background = null, textColor = Color.white },
                active = { background = null, textColor = Color.white },
                focused = { background = null, textColor = Color.white },
                onNormal = { background = null, textColor = Color.white },
                onHover = { background = null, textColor = Color.white },
                onActive = { background = null, textColor = Color.white },
                onFocused = { background = null, textColor = Color.white }
            };
        }

        private void EnsureInputBlocker()
        {
            if (inputBlockerRoot != null)
                return;

            inputBlockerRoot = new GameObject(
                "CameraTools_UpdateNotice_InputBlocker",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(Image),
                typeof(CameraToolsUpdateNoticeInputBlocker));
            Object.DontDestroyOnLoad(inputBlockerRoot);

            var canvas = inputBlockerRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = inputBlockerRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var image = inputBlockerRoot.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            var rectTransform = inputBlockerRoot.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private void DestroyInputBlocker()
        {
            if (inputBlockerRoot == null)
                return;

            Object.Destroy(inputBlockerRoot);
            inputBlockerRoot = null;
        }

        private Texture2D MakeRoundedRectTexture(int width, int height, Color color, int radius)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var transparent = new Color(0f, 0f, 0f, 0f);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var drawPixel = true;

                    if (x < radius && y < radius)
                        drawPixel = IsInsideCorner(x, y, radius - 1, radius - 1, radius);
                    else if (x >= width - radius && y < radius)
                        drawPixel = IsInsideCorner(x, y, width - radius, radius - 1, radius);
                    else if (x < radius && y >= height - radius)
                        drawPixel = IsInsideCorner(x, y, radius - 1, height - radius, radius);
                    else if (x >= width - radius && y >= height - radius)
                        drawPixel = IsInsideCorner(x, y, width - radius, height - radius, radius);

                    texture.SetPixel(x, y, drawPixel ? color : transparent);
                }
            }

            texture.Apply();
            return texture;
        }

        private static bool IsInsideCorner(int x, int y, int centerX, int centerY, int radius)
        {
            var deltaX = x - centerX;
            var deltaY = y - centerY;
            return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
        }

        private static string Localize(string key)
        {
            return key.Localize().ToString();
        }
    }

    internal sealed class CameraToolsUpdateNoticeInputBlocker : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IDragHandler,
        IScrollHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            Consume(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Consume(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Consume(eventData);
        }

        public void OnScroll(PointerEventData eventData)
        {
            Consume(eventData);
        }

        private static void Consume(PointerEventData eventData)
        {
            eventData.Use();
            Input.ResetInputAxes();
        }
    }
}
