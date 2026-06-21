using System;
using Localizor;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestCharacterSpeechBubble : MonoBehaviour
    {
        private static Sprite _backgroundSprite;
        private static Material _backgroundMaterial;
        private static Material _textMaterial;

        private Transform _anchor;
        private GameObject _bubbleRoot;
        private SpriteRenderer _backgroundRenderer;
        private TextMesh _textMesh;
        private Vector3 _localOffset;
        private float _visibleSeconds;
        private float _intervalSeconds;
        private float _nextToggleAt;
        private bool _isVisible;
        private bool _isEnabled;
        private bool _configured;
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

            EnsureBubbleRoot();
            ApplyBubbleText();

            _configured = true;
            _nextToggleAt = Time.unscaledTime + UnityEngine.Random.Range(0.15f, 1.1f);
            SetBubbleVisible(false);
            StreetQuestShared.LogDebug($"SpeechBubble configured character={definition.id} text='{_resolvedText}'");
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

        private void LateUpdate()
        {
            if (!_configured || !_isEnabled || _bubbleRoot == null || _anchor == null)
                return;

            _bubbleRoot.transform.position = _anchor.TransformPoint(_localOffset);
            FaceMainCamera();

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

        private void EnsureBubbleRoot()
        {
            if (_bubbleRoot != null)
                return;

            _bubbleRoot = new GameObject("StreetQuestSpeechBubble");
            _bubbleRoot.transform.SetParent(transform, false);

            var backgroundObject = new GameObject("Background");
            backgroundObject.transform.SetParent(_bubbleRoot.transform, false);
            _backgroundRenderer = backgroundObject.AddComponent<SpriteRenderer>();
            _backgroundRenderer.sprite = GetBackgroundSprite();
            _backgroundRenderer.sharedMaterial = GetBackgroundMaterial();
            _backgroundRenderer.color = new Color32(248, 231, 151, 255);
            _backgroundRenderer.sortingOrder = short.MaxValue - 8;

            var textObject = new GameObject("BubbleText");
            textObject.transform.SetParent(_bubbleRoot.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0.03f, -0.01f);
            _textMesh = textObject.AddComponent<TextMesh>();
            _textMesh.anchor = TextAnchor.MiddleCenter;
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.fontSize = 84;
            _textMesh.characterSize = 0.052f;
            _textMesh.richText = false;
            _textMesh.color = new Color32(48, 42, 30, 255);

            var renderer = _textMesh.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetTextMaterial();
            renderer.sortingOrder = short.MaxValue - 7;

            SetBubbleVisible(false);
        }

        private void ApplyBubbleText()
        {
            if (_textMesh == null || _backgroundRenderer == null)
                return;

            var text = string.IsNullOrWhiteSpace(_resolvedText) ? "..." : _resolvedText.Trim();
            _textMesh.text = text;

            var width = Mathf.Clamp(0.42f + (text.Length * 0.085f), 0.52f, 1.7f);
            var height = text.Length > 10 ? 0.54f : 0.46f;
            _backgroundRenderer.transform.localScale = new Vector3(width, height, 1f);
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
                _bubbleRoot.SetActive(visible);
        }

        private void DisableBubble()
        {
            _configured = false;
            _isEnabled = false;
            SetBubbleVisible(false);
        }

        private void FaceMainCamera()
        {
            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (cameraTransform == null || _bubbleRoot == null)
                return;

            var forward = cameraTransform.position - _bubbleRoot.transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;

            _bubbleRoot.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static Sprite GetBackgroundSprite()
        {
            if (_backgroundSprite != null)
                return _backgroundSprite;

            const int width = 196;
            const int height = 128;
            const int cornerRadius = 28;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var clear = new Color32(255, 255, 255, 0);
            var fill = new Color32(255, 255, 255, 255);
            var tailCenterX = width / 2;
            var tailBaseY = 32;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var inBody = IsInsideRoundedRect(x, y, 10, 28, width - 20, height - 40, cornerRadius);
                    var inTail = y >= 6 &&
                                 y <= tailBaseY &&
                                 Mathf.Abs(x - tailCenterX) <= Mathf.Lerp(2f, 16f, (y - 6f) / Mathf.Max(1f, tailBaseY - 6f));
                    texture.SetPixel(x, y, inBody || inTail ? fill : clear);
                }
            }

            texture.Apply(false, true);
            _backgroundSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.16f),
                100f);
            _backgroundSprite.name = "StreetQuestSpeechBubbleSprite";
            return _backgroundSprite;
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

        private static Material GetBackgroundMaterial()
        {
            if (_backgroundMaterial != null)
                return _backgroundMaterial;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _backgroundMaterial = new Material(shader)
            {
                name = "StreetQuestSpeechBubbleBackground"
            };
            _backgroundMaterial.renderQueue = 5000;
            _backgroundMaterial.SetInt("_ZWrite", 0);
            _backgroundMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return _backgroundMaterial;
        }

        private static Material GetTextMaterial()
        {
            if (_textMaterial != null)
                return _textMaterial;

            var shader = Shader.Find("GUI/Text Shader") ?? Shader.Find("GUI/Text Shader Linear");
            if (shader == null)
            {
                var temp = new GameObject("StreetQuestSpeechBubbleTextProbe");
                try
                {
                    var mesh = temp.AddComponent<TextMesh>();
                    var renderer = mesh.GetComponent<MeshRenderer>();
                    _textMaterial = renderer != null ? renderer.sharedMaterial : null;
                }
                finally
                {
                    Destroy(temp);
                }
            }
            else
            {
                _textMaterial = new Material(shader)
                {
                    name = "StreetQuestSpeechBubbleText"
                };
            }

            if (_textMaterial != null)
            {
                _textMaterial.renderQueue = 5001;
                _textMaterial.SetInt("_ZWrite", 0);
                _textMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            return _textMaterial;
        }
    }
}
