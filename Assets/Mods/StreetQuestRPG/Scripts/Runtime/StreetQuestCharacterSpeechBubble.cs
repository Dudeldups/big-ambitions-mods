using System;
using System.Linq;
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
        private float _maxDistance;
        private float _nextToggleAt;
        private bool _isVisible;
        private bool _isEnabled;
        private bool _configured;
        private string[] _resolvedTexts = Array.Empty<string>();
        private int _nextTextIndex;
        private string _currentText = "...";
        private Color32 _resolvedBackgroundColor = new(248, 231, 151, 255);
        private Color32 _resolvedTextColor = new(48, 42, 30, 255);
        private float _sizeMultiplier = 1f;

        public void Configure(Transform anchor, StreetQuestCharacterDefinition definition)
        {
            if (anchor == null || definition == null)
            {
                DisableBubble();
                return;
            }

            _anchor = anchor;
            _localOffset = definition.SpeechBubbleLocalOffsetOr(new Vector3(0f, 1.95f, 0f));
            _visibleSeconds = Mathf.Max(0.75f, definition.speechBubbleVisibleSeconds);
            _intervalSeconds = Mathf.Max(_visibleSeconds + 0.5f, definition.speechBubbleIntervalSeconds);
            _maxDistance = definition.speechBubbleMaxDistance > 0f ? definition.speechBubbleMaxDistance : 14f;
            _maxDistance = ApplySpeechBubbleDistanceModifier(_maxDistance, definition.speechBubbleColor);
            _sizeMultiplier = ResolveSpeechBubbleSizeMultiplier(definition.speechBubbleColor);
            _resolvedTexts = ResolveBubbleTexts(definition);
            _nextTextIndex = _resolvedTexts.Length > 1 ? UnityEngine.Random.Range(0, _resolvedTexts.Length) : 0;
            _currentText = GetNextResolvedText();
            ResolveBubbleColors(definition.speechBubbleColor, out _resolvedBackgroundColor, out _resolvedTextColor);

            EnsureBubbleUi();
            ApplyBubbleColors();
            ApplyBubbleContent();

            _configured = true;
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
                if (!IsPlayerCloseEnough())
                {
                    _nextToggleAt = Time.unscaledTime + Mathf.Max(0.75f, _intervalSeconds * 0.5f);
                    return;
                }

                _currentText = GetNextResolvedText();
                ApplyBubbleContent();
                SetBubbleVisible(true);
                _nextToggleAt = Time.unscaledTime + _visibleSeconds;
            }
        }

        private bool IsPlayerCloseEnough()
        {
            if (_anchor == null)
                return false;

            if (!StreetQuestShared.TryGetPlayerWorldPosition(out var playerPosition))
                return false;

            var anchorPosition = _anchor.position;
            var maxDistanceSquared = _maxDistance * _maxDistance;
            return (playerPosition - anchorPosition).sqrMagnitude <= maxDistanceSquared;
        }

        private void EnsureBubbleUi()
        {
            if (_bubbleRoot != null)
                return;

            var canvas = EnsureOverlayCanvas();
            if (canvas == null)
                return;

            var bubbleObject = new GameObject("StreetQuestSpeechBubbleUI", typeof(RectTransform), typeof(Image), typeof(Outline));
            bubbleObject.transform.SetParent(canvas.transform, false);
            _bubbleRoot = bubbleObject.GetComponent<RectTransform>();
            _bubbleRoot.pivot = new Vector2(0.5f, 0.2f);
            _bubbleRoot.anchorMin = Vector2.zero;
            _bubbleRoot.anchorMax = Vector2.zero;

            var background = bubbleObject.GetComponent<Image>();
            background.sprite = GetBackgroundSprite();
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            _backgroundRect = _bubbleRoot;

            var backgroundOutline = bubbleObject.GetComponent<Outline>();
            backgroundOutline.effectColor = new Color32(255, 255, 255, 235);
            backgroundOutline.effectDistance = new Vector2(2f, -2f);
            backgroundOutline.useGraphicAlpha = true;

            var tailObject = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            tailObject.transform.SetParent(_bubbleRoot, false);
            _tailRect = tailObject.GetComponent<RectTransform>();
            _tailRect.anchorMin = new Vector2(0.5f, 0f);
            _tailRect.anchorMax = new Vector2(0.5f, 0f);
            _tailRect.pivot = new Vector2(0.5f, 1f);
            _tailRect.anchoredPosition = new Vector2(0f, 1f);
            _tailRect.sizeDelta = new Vector2(16f, 14f);

            var tailImage = tailObject.GetComponent<Image>();
            tailImage.sprite = GetTailSprite();
            tailImage.type = Image.Type.Simple;
            tailImage.raycastTarget = false;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(_bubbleRoot, false);
            _textRect = textObject.GetComponent<RectTransform>();
            _textRect.anchorMin = Vector2.zero;
            _textRect.anchorMax = Vector2.one;
            _textRect.offsetMin = new Vector2(14f, 12f);
            _textRect.offsetMax = new Vector2(-14f, -8f);

            _textLabel = textObject.GetComponent<Text>();
            _textLabel.font = GetUiFont();
            _textLabel.fontSize = 26;
            _textLabel.alignment = TextAnchor.MiddleCenter;
            _textLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _textLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _textLabel.supportRichText = false;
            _textLabel.resizeTextForBestFit = true;
            _textLabel.resizeTextMinSize = 14;
            _textLabel.resizeTextMaxSize = 28;
            _textLabel.raycastTarget = false;

            SetBubbleVisible(false);
        }

        private void ApplyBubbleContent()
        {
            if (_backgroundRect == null || _textLabel == null)
                return;

            var text = string.IsNullOrWhiteSpace(_currentText) ? "..." : _currentText.Trim();
            _textLabel.text = text;
            _textLabel.font = GetUiFont();

            const float horizontalPadding = 36f;
            const float verticalPadding = 24f;
            const float minWidth = 126f;
            const float maxWidth = 420f;
            const float minHeight = 50f;
            const float maxHeight = 180f;
            var preferredWidth = Mathf.Max(80f, _textLabel.preferredWidth);
            var width = Mathf.Clamp(preferredWidth + horizontalPadding, minWidth, maxWidth) * _sizeMultiplier;
            var textWidth = Mathf.Max(80f, width - horizontalPadding);
            var generationSettings = _textLabel.GetGenerationSettings(new Vector2(textWidth, 1000f));
            generationSettings.horizontalOverflow = HorizontalWrapMode.Wrap;
            generationSettings.verticalOverflow = VerticalWrapMode.Overflow;
            var preferredHeight = _textLabel.cachedTextGeneratorForLayout.GetPreferredHeight(text, generationSettings) /
                                  Mathf.Max(0.0001f, _textLabel.pixelsPerUnit);
            preferredHeight = Mathf.Max(24f, preferredHeight);
            var height = Mathf.Clamp(preferredHeight + verticalPadding, minHeight, maxHeight) * _sizeMultiplier;
            _backgroundRect.sizeDelta = new Vector2(width, height);
            _textRect.offsetMin = new Vector2(14f, 12f);
            _textRect.offsetMax = new Vector2(-14f, -8f);
            _tailRect.sizeDelta = width > 180f ? new Vector2(18f, 16f) : new Vector2(16f, 14f);
            _tailRect.anchoredPosition = new Vector2(0f, 1f);
        }

        private void ApplyBubbleColors()
        {
            if (_bubbleRoot == null)
                return;

            var background = _bubbleRoot.GetComponent<Image>();
            if (background != null)
                background.color = _resolvedBackgroundColor;

            if (_tailRect != null)
            {
                var tailImage = _tailRect.GetComponent<Image>();
                if (tailImage != null)
                    tailImage.color = _resolvedBackgroundColor;
            }

            if (_textLabel != null)
                _textLabel.color = _resolvedTextColor;
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

        private static string[] ResolveBubbleTexts(StreetQuestCharacterDefinition definition)
        {
            if (definition == null)
                return new[] { "..." };

            if (definition.speechBubbleTextKeys != null && definition.speechBubbleTextKeys.Length > 0)
            {
                var localizedValues = definition.speechBubbleTextKeys
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Localize().ToString().Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                if (localizedValues.Length > 0)
                    return localizedValues;
            }

            if (!string.IsNullOrWhiteSpace(definition.speechBubbleTextKey))
            {
                var localized = definition.speechBubbleTextKey.Localize().ToString().Trim();
                if (!string.IsNullOrWhiteSpace(localized))
                    return new[] { localized };
            }

            var fallback = ResolveBubbleFallbackText(definition.speechBubbleEmojiName);
            return new[] { string.IsNullOrWhiteSpace(fallback) ? "..." : fallback };
        }

        private string GetNextResolvedText()
        {
            if (_resolvedTexts == null || _resolvedTexts.Length == 0)
                return "...";

            var index = Mathf.Clamp(_nextTextIndex, 0, _resolvedTexts.Length - 1);
            var value = _resolvedTexts[index];
            _nextTextIndex = (_nextTextIndex + 1) % _resolvedTexts.Length;
            return string.IsNullOrWhiteSpace(value) ? "..." : value;
        }

        private static string ResolveBubbleFallbackText(string emojiName)
        {
            if (string.IsNullOrWhiteSpace(emojiName))
                return string.Empty;

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

            if (emojiName.IndexOf("angry", StringComparison.OrdinalIgnoreCase) >= 0)
                return "!";

            return "...";
        }

        private static void ResolveBubbleColors(string colorName, out Color32 backgroundColor, out Color32 textColor)
        {
            textColor = new Color32(48, 42, 30, 255);
            if (string.IsNullOrWhiteSpace(colorName))
            {
                backgroundColor = new Color32(248, 231, 151, 255);
                return;
            }

            switch (colorName.Trim().ToLowerInvariant())
            {
                case "yellow":
                    backgroundColor = new Color32(248, 231, 151, 255);
                    break;
                case "red":
                    backgroundColor = new Color32(242, 162, 162, 255);
                    break;
                case "blue":
                    backgroundColor = new Color32(170, 212, 247, 255);
                    break;
                case "neutral":
                    backgroundColor = new Color32(246, 244, 238, 255);
                    break;
                default:
                    backgroundColor = new Color32(248, 231, 151, 255);
                    break;
            }
        }

        private static float ApplySpeechBubbleDistanceModifier(float baseDistance, string colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
                return baseDistance;

            return string.Equals(colorName.Trim(), "red", StringComparison.OrdinalIgnoreCase)
                ? baseDistance * 1.75f
                : baseDistance;
        }

        private static float ResolveSpeechBubbleSizeMultiplier(string colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
                return 1f;

            return string.Equals(colorName.Trim(), "red", StringComparison.OrdinalIgnoreCase)
                ? 1.15f
                : 1f;
        }

        private void SetBubbleVisible(bool visible)
        {
            _isVisible = visible;
            if (_bubbleRoot != null)
                _bubbleRoot.gameObject.SetActive(visible);
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
            const int cornerRadius = 12;
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
                new Vector4(22f, 22f, 22f, 22f));
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
                    texture.SetPixel(x, y, Mathf.Abs(x - centerX) <= halfWidth ? fill : clear);
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
            return dx * dx + dy * dy <= radius * radius;
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
