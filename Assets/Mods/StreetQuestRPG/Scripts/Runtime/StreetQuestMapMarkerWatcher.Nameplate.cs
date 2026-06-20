using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using UnityEngine;
using UnityEngine.UI;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestMapMarkerWatcher
    {
        private void EnsureMarkerHoverTarget(RectTransform markerRoot, string characterId)
        {
            if (markerRoot == null || string.IsNullOrWhiteSpace(characterId))
                return;

            var existing = markerRoot.Find("StreetQuestMarkerHoverHitTarget");
            if (existing != null)
            {
                var existingTarget = existing.GetComponent<StreetQuestMapMarkerHoverTarget>();
                if (existingTarget != null)
                    existingTarget.Configure(this, characterId, markerRoot);
                return;
            }

            var hitTargetObject = new GameObject("StreetQuestMarkerHoverHitTarget", typeof(RectTransform), typeof(Image), typeof(StreetQuestMapMarkerHoverTarget));
            var hitRect = hitTargetObject.GetComponent<RectTransform>();
            hitRect.SetParent(markerRoot, false);
            hitRect.anchorMin = new Vector2(0.5f, 0.5f);
            hitRect.anchorMax = new Vector2(0.5f, 0.5f);
            hitRect.pivot = new Vector2(0.5f, 0.5f);
            hitRect.sizeDelta = new Vector2(48f, 48f);
            hitRect.anchoredPosition = Vector2.zero;
            hitRect.SetAsLastSibling();

            var image = hitTargetObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.01f);
            image.raycastTarget = true;

            hitTargetObject.GetComponent<StreetQuestMapMarkerHoverTarget>().Configure(this, characterId, markerRoot);
            DebugLog($"Map marker hover target created characterId={characterId}");
        }
        internal void ShowMarkerNameplate(string characterId, RectTransform markerRoot)
        {
            if (string.IsNullOrWhiteSpace(characterId) || markerRoot == null || _streetQuestRoot == null)
                return;

            EnsureNameplate();
            if (_nameplateRoot == null || _nameplateText == null)
                return;

            var changed = !string.Equals(_hoveredCharacterId, characterId, StringComparison.OrdinalIgnoreCase);
            _hoveredCharacterId = characterId;
            _hoveredMarkerRoot = markerRoot;
            _nameplateText.text = StreetQuestShared.ResolveCharacterDisplayName(characterId);
            _nameplateRoot.gameObject.SetActive(_mapFilterVisible);
            UpdateNameplatePosition();

            if (changed)
                DebugLog($"Map marker nameplate shown characterId={characterId} text={_nameplateText.text}");
        }
        internal void HideMarkerNameplate(string characterId, RectTransform markerRoot)
        {
            if (!string.IsNullOrWhiteSpace(characterId) &&
                !string.Equals(_hoveredCharacterId, characterId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            HideMarkerNameplate();
        }
        private void HideMarkerNameplate()
        {
            _hoveredCharacterId = null;
            _hoveredMarkerRoot = null;

            if (_nameplateRoot != null && _nameplateRoot.gameObject != null)
                _nameplateRoot.gameObject.SetActive(false);
        }
        private void EnsureNameplate()
        {
            if (_nameplateRoot != null && _nameplateRoot.gameObject != null)
                return;

            if (_streetQuestRoot == null)
                return;

            var rootObject = new GameObject("StreetQuestMarkerNameplate", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _nameplateRoot = rootObject.GetComponent<RectTransform>();
            _nameplateRoot.SetParent(_streetQuestRoot, false);
            _nameplateRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _nameplateRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _nameplateRoot.pivot = new Vector2(0.5f, 0f);
            _nameplateRoot.sizeDelta = new Vector2(170f, NameplateHeight);
            _nameplateRoot.anchoredPosition = Vector2.zero;
            _nameplateRoot.SetAsLastSibling();

            var background = rootObject.GetComponent<Image>();
            background.sprite = GetNameplateBackgroundSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color(0.05f, 0.04f, 0.035f, 0.93f);
            background.raycastTarget = false;

            var canvasGroup = rootObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(_nameplateRoot, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 3f);
            textRect.offsetMax = new Vector2(-8f, -3f);

            _nameplateText = textObject.GetComponent<Text>();
            _nameplateText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _nameplateText.fontSize = NameplateFontSize;
            _nameplateText.alignment = TextAnchor.MiddleCenter;
            _nameplateText.color = Color.white;
            _nameplateText.raycastTarget = false;

            rootObject.SetActive(false);
            DebugLog("Map marker nameplate panel created.");
        }
        private void UpdateNameplatePosition()
        {
            if (_nameplateRoot == null || _nameplateText == null || _hoveredMarkerRoot == null)
                return;

            if (!_mapFilterVisible || !_hoveredMarkerRoot.gameObject.activeInHierarchy)
            {
                HideMarkerNameplate();
                return;
            }

            var width = Mathf.Clamp(_nameplateText.preferredWidth + 36f, 110f, 320f);
            _nameplateRoot.sizeDelta = new Vector2(width, NameplateHeight);
            _nameplateRoot.anchoredPosition = _hoveredMarkerRoot.anchoredPosition + new Vector2(0f, NameplateVerticalOffset);
            _nameplateRoot.SetAsLastSibling();
        }
        private Sprite GetNameplateBackgroundSprite()
        {
            if (_nameplateBackgroundSprite != null)
                return _nameplateBackgroundSprite;

            var texture = new Texture2D(NameplateBackgroundWidth, NameplateBackgroundHeight, TextureFormat.RGBA32, false)
            {
                name = "StreetQuestMarkerNameplateRoundedBackground"
            };
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[NameplateBackgroundWidth * NameplateBackgroundHeight];
            var transparent = new Color32(255, 255, 255, 0);
            var opaque = new Color32(255, 255, 255, 255);
            for (var y = 0; y < NameplateBackgroundHeight; y++)
            {
                for (var x = 0; x < NameplateBackgroundWidth; x++)
                {
                    pixels[y * NameplateBackgroundWidth + x] = IsInsideRoundedRect(
                        x + 0.5f,
                        y + 0.5f,
                        NameplateBackgroundWidth,
                        NameplateBackgroundHeight,
                        NameplateCornerRadiusPixels)
                        ? opaque
                        : transparent;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _nameplateBackgroundSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, NameplateBackgroundWidth, NameplateBackgroundHeight),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(
                    NameplateCornerRadiusPixels,
                    NameplateCornerRadiusPixels,
                    NameplateCornerRadiusPixels,
                    NameplateCornerRadiusPixels));
            _nameplateBackgroundSprite.name = "StreetQuestMarkerNameplateRoundedBackgroundSprite";
            return _nameplateBackgroundSprite;
        }
        private static bool IsInsideRoundedRect(float x, float y, int width, int height, float radius)
        {
            var clampedX = Mathf.Clamp(x, radius, width - radius);
            var clampedY = Mathf.Clamp(y, radius, height - radius);
            var dx = x - clampedX;
            var dy = y - clampedY;
            return dx * dx + dy * dy <= radius * radius;
        }
    }
}
