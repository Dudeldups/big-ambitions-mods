using System;
using Localizor;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestCharacterSpeechBubble : MonoBehaviour
    {
        private static Canvas _overlayCanvas;
        private static Sprite _backgroundSprite;
        private static Sprite _tailSprite;
        private static Font _uiFont;

        private Transform _anchor;
        private RectTransform _bubbleRoot;
        private RectTransform _backgroundRect;
        private RectTransform _tailRect;
        private RectTransform _textRect;
        private Text _textLabel;
        private Vector3 _localOffset;
        private float _visibleSeconds;
        private float _intervalSeconds;
        private float _nextToggleAt;
        private bool _isVisible;
        private bool _isEnabled;
        private bool _configured;
        private bool _loggedVisibleOnce;
        private string _resolvedText = "...";

        public void Configure(Transform anchor, StreetQuestCharacterDefinition definition)
        {
            if (anchor == null || definition == null)
            {
                DisableBubble();
                return;
            }

            _anchor = anchor;
            _localOffset = definition.SpeechBubbleLocalOffsetOr(new Vector3(0f, 2.15f, 0f));
            _visibleSeconds = Mathf.Max(0.75f, definition.speechBubbleVisibleSeconds);
            _intervalSeconds = Mathf.Max(_visibleSeconds + 0.5f, definition.speechBubbleIntervalSeconds);
            _resolvedText = ResolveBubbleText(definition);

            EnsureBubbleUi();
            ApplyBubbleText();

            _configured = true;
            _loggedVisibleOnce = false;
            _nextToggleAt = Time.unscaledTime + UnityEngine.Random.Range(0.15f, 1.1f);
            SetBubbleVisible(false);
        }

        public void OnVisibilityChanged(bool visible)
        {
            _isEnabled = visible;
            if (!_isEnabled)
            {
                SetBubbleVisible(false);
                return;
            }

            _nextToggleAt = Time.unscaledTime + UnityEngine.Random.Range(0.1f, 0.8f);
        }

        private void OnDisable()
        {
            SetBubbleVisible(false);
        }

        private void OnDestroy()
        {
            if (_bubbleRoot != null)
                Destroy(_bubbleRoot.gameObject);
        }

        private void LateUpdate()
        {
            if (!_configured || !_isEnabled || _bubbleRoot == null || _anchor == null)
                return;

            UpdateBubbleScreenPosition();

            if (Time.unscaledTime < _nextToggleAt)
                return;

            if (_isVisible)
            {
                SetBubbleVisible(false);
                _nextToggleAt = Time.unscaledTime + Mathf.Max(0.5f, _intervalSeconds - _visibleSeconds);
            }
            else
            {
                SetBubbleVisible(true);
                _nextToggleAt = Time.unscaledTime + _visibleSeconds;
            }
        }

        private void EnsureBubbleUi()
        {
            if (_bubbleRoot != null)
                return;

            var canvas = EnsureOverlayCanvas();
            if (canvas == null)
                return;

            var bubbleObject = new GameObject("StreetQuestSpeechBubbleUI", typeof(RectTransform), typeof(Image));
            bubbleObject.transform.SetParent(canvas.transform, false);
            _bubbleRoot = bubbleObject.GetComponent<RectTransform>();
            _bubbleRoot.pivot = new Vector2(0.5f, 0.2f);
            _bubbleRoot.anchorMin = new Vector2(0f, 0f);
            _bubbleRoot.anchorMax = new Vector2(0f, 0f);

            var background = bubbleObject.GetComponent<Image>();
            background.sprite = GetBackgroundSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color32(248, 231, 151, 255);
            _backgroundRect = _bubbleRoot;

            var tailObject = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            tailObject.transform.SetParent(_bubbleRoot, false);
            _tailRect = tailObject.GetComponent<RectTransform>();
            _tailRect.anchorMin = new Vector2(0.5f, 0f);
            _tailRect.anchorMax = new Vector2(0.5f, 0f);
            _tailRect.pivot = new Vector2(0.5f, 1f);
            _tailRect.anchoredPosition = new Vector2(0f, 2f);
            _tailRect.sizeDelta = new Vector2(20f, 18f);

            var tailImage = tailObject.GetComponent<Image>();
            tailImage.sprite = GetTailSprite();
            tailImage.type = Image.Type.Simple;
            tailImage.color = new Color32(248, 231, 151, 255);
            tailImage.raycastTarget = false;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(_bubbleRoot, false);
            _textRect = textObject.GetComponent<RectTransform>();
            _textRect.anchorMin = Vector2.zero;
            _textRect.anchorMax = Vector2.one;
            _textRect.offsetMin = new Vector2(14f, 16f);
            _textRect.offsetMax = new Vector2(-14f, -8f);

            _textLabel = textObject.GetComponent<Text>();
            _textLabel.font = GetUiFont();
            _textLabel.fontSize = 26;
            _textLabel.alignment = TextAnchor.MiddleCenter;
            _textLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _textLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _textLabel.color = new Color32(48, 42, 30, 255);
            _textLabel.supportRichText = false;
            _textLabel.resizeTextForBestFit = true;
            _textLabel.resizeTextMinSize = 14;
            _textLabel.resizeTextMaxSize = 28;
            _textLabel.raycastTarget = false;

            SetBubbleVisible(false);
        }

        private void ApplyBubbleText()
        {
            if (_textLabel == null || _backgroundRect == null)
                return;

            var text = string.IsNullOrWhiteSpace(_resolvedText) ? "..." : _resolvedText.Trim();
            _textLabel.text = text;

            var preferredWidth = Mathf.Max(80f, _textLabel.preferredWidth);
            var preferredHeight = Mathf.Max(24f, _textLabel.preferredHeight);
            var width = Mathf.Clamp(preferredWidth + 36f, 126f, 320f);
            var height = Mathf.Clamp(preferredHeight + 24f, 50f, 86f);
            _backgroundRect.sizeDelta = new Vector2(width, height);

            if (_textRect != null)
            {
                _textRect.offsetMin = new Vector2(14f, 12f);
                _textRect.offsetMax = new Vector2(-14f, -8f);
            }

            if (_tailRect != null)
            {
                _tailRect.sizeDelta = text.Length > 8
                    ? new Vector2(18f, 16f)
                    : new Vector2(16f, 14f);
                _tailRect.anchoredPosition = new Vector2(0f, 1f);
            }
        }

        private void UpdateBubbleScreenPosition()
        {
            if (_bubbleRoot == null || _anchor == null)
                return;

            var camera = Camera.main;
            if (camera == null)
            {
                _bubbleRoot.gameObject.SetActive(false);
                return;
            }

            var worldPosition = _anchor.TransformPoint(_localOffset);
            var screenPosition = camera.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f)
            {
                _bubbleRoot.gameObject.SetActive(false);
                return;
            }

            if (_isVisible && !_bubbleRoot.gameObject.activeSelf)
                _bubbleRoot.gameObject.SetActive(true);

            _bubbleRoot.position = screenPosition;
        }

        private static string ResolveBubbleText(StreetQuestCharacterDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition?.speechBubbleTextKey))
                return definition.speechBubbleTextKey.Localize().ToString();

            return ResolveEmojiText(definition?.speechBubbleEmojiName);
        }

        private static string ResolveEmojiText(string emojiName)
        {
            if (string.IsNullOrWhiteSpace(emojiName))
                return "...";

            if (emojiName.IndexOf("question", StringComparison.OrdinalIgnoreCase) >= 0)
                return "?";

            if (emojiName.IndexOf("exclamation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                emojiName.IndexOf("alert", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "!";
            }

            if (emojiName.IndexOf("happy", StringComparison.OrdinalIgnoreCase) >= 0)
                return ":)";

            if (emojiName.IndexOf("sad", StringComparison.OrdinalIgnoreCase) >= 0)
                return ":(";

            return "...";
        }

        private void SetBubbleVisible(bool visible)
        {
            _isVisible = visible;
            if (_bubbleRoot != null)
                _bubbleRoot.gameObject.SetActive(visible);

            if (visible && !_loggedVisibleOnce)
                _loggedVisibleOnce = true;
        }

        private void DisableBubble()
        {
            _configured = false;
            _isEnabled = false;
            SetBubbleVisible(false);
        }

        private static Canvas EnsureOverlayCanvas()
        {
            if (_overlayCanvas != null)
                return _overlayCanvas;

            var existing = GameObject.Find("StreetQuestSpeechBubbleCanvas");
            if (existing != null)
            {
                _overlayCanvas = existing.GetComponent<Canvas>();
                if (_overlayCanvas != null)
                    return _overlayCanvas;
            }

            var canvasObject = new GameObject(
                "StreetQuestSpeechBubbleCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);

            _overlayCanvas = canvasObject.GetComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = short.MaxValue - 16;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;
            return _overlayCanvas;
        }

        private static Sprite GetBackgroundSprite()
        {
            if (_backgroundSprite != null)
                return _backgroundSprite;

            const int width = 196;
            const int height = 92;
            const int cornerRadius = 26;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var clear = new Color32(255, 255, 255, 0);
            var fill = new Color32(255, 255, 255, 255);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var inBody = IsInsideRoundedRect(x, y, 8, 8, width - 16, height - 16, cornerRadius);
                    texture.SetPixel(x, y, inBody ? fill : clear);
                }
            }

            texture.Apply(false, true);
            _backgroundSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(26f, 26f, 26f, 26f));
            _backgroundSprite.name = "StreetQuestSpeechBubbleSprite";
            return _backgroundSprite;
        }

        private static Sprite GetTailSprite()
        {
            if (_tailSprite != null)
                return _tailSprite;

            const int width = 48;
            const int height = 36;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var clear = new Color32(255, 255, 255, 0);
            var fill = new Color32(255, 255, 255, 255);
            var centerX = width / 2f;

            for (var y = 0; y < height; y++)
            {
                var progress = y / (float)(height - 1);
                var halfWidth = Mathf.Lerp(2f, 20f, progress);
                for (var x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, Mathf.Abs(x - centerX) <= halfWidth ? fill : clear);
                }
            }

            texture.Apply(false, true);
            _tailSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 1f),
                100f);
            _tailSprite.name = "StreetQuestSpeechBubbleTailSprite";
            return _tailSprite;
        }

        private static bool IsInsideRoundedRect(int x, int y, int rectX, int rectY, int rectWidth, int rectHeight, int radius)
        {
            if (x < rectX || x >= rectX + rectWidth || y < rectY || y >= rectY + rectHeight)
                return false;

            var innerLeft = rectX + radius;
            var innerRight = rectX + rectWidth - radius;
            var innerBottom = rectY + radius;
            var innerTop = rectY + rectHeight - radius;

            if (x >= innerLeft && x < innerRight)
                return true;

            if (y >= innerBottom && y < innerTop)
                return true;

            var cornerX = x < innerLeft ? innerLeft : innerRight - 1;
            var cornerY = y < innerBottom ? innerBottom : innerTop - 1;
            var dx = x - cornerX;
            var dy = y - cornerY;
            return (dx * dx) + (dy * dy) <= radius * radius;
        }

        private static Font GetUiFont()
        {
            if (_uiFont != null)
                return _uiFont;

            _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _uiFont;
        }
    }
}
