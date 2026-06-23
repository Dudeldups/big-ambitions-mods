using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.SaveSystem.Legacy;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestApartmentEntryOverlay : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.2f;
        private const float Width = 360f;

        private static readonly RectOffset Padding = new(12, 12, 12, 12);

        private float _elapsedSeconds;
        private float _nextRefreshAtSeconds;
        private int _lastStateVersion = int.MinValue;
        private string _lastExteriorAddress = string.Empty;
        private Rect _panelRect;
        private List<StreetQuestShared.ApartmentEntryOption> _currentOptions = new();
        private GUIStyle _panelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _textStyle;
        private Texture2D _panelTexture;

        public void Tick()
        {
            _elapsedSeconds += Time.unscaledDeltaTime;
            if (_elapsedSeconds < _nextRefreshAtSeconds)
                return;

            _nextRefreshAtSeconds = _elapsedSeconds + RefreshIntervalSeconds;
            RefreshOptionsIfNeeded();
        }

        public bool ShouldBlockGameplayInput()
        {
            return IsVisible() && _panelRect.Contains(GetGuiMousePositionFromInput());
        }

        private void OnGUI()
        {
            if (!IsVisible())
                return;

            EnsureStyles();
            var height = 52f + (_currentOptions.Count * 42f);
            _panelRect = new Rect(Screen.width - Width - 32f, Screen.height * 0.5f - height * 0.5f, Width, height);
            GUILayout.BeginArea(_panelRect, _panelStyle);
            GUILayout.Label("Apartment Access", _headerStyle);
            foreach (var option in _currentOptions)
            {
                if (option == null)
                    continue;

                if (GUILayout.Button(option.ButtonText, _buttonStyle, GUILayout.Height(34f)))
                    HandleEntryClicked(option);
            }

            GUILayout.Label("Shown only when you are outside at a matching building entrance.", _textStyle);
            GUILayout.EndArea();

            ConsumePointerEvents();
        }

        private void RefreshOptionsIfNeeded()
        {
            if (!IsInActiveGameSession())
            {
                SetCurrentOptions(Array.Empty<StreetQuestShared.ApartmentEntryOption>(), string.Empty);
                return;
            }

            if (StreetQuestShared.IsCityMapOpen() || StreetQuestShared.IsIndoorGameplayContextActive())
            {
                SetCurrentOptions(Array.Empty<StreetQuestShared.ApartmentEntryOption>(), string.Empty);
                return;
            }

            var exteriorAddress = StreetQuestShared.GetCurrentExteriorBuildingAddressKey() ?? string.Empty;
            var stateVersion = StreetQuestShared.GetQuestStateVersion();
            if (stateVersion == _lastStateVersion &&
                string.Equals(exteriorAddress, _lastExteriorAddress, StringComparison.Ordinal))
            {
                return;
            }

            _lastStateVersion = stateVersion;
            _lastExteriorAddress = exteriorAddress;
            SetCurrentOptions(StreetQuestShared.GetAvailableApartmentEntryOptions(exteriorAddress), exteriorAddress);
        }

        private void SetCurrentOptions(IReadOnlyList<StreetQuestShared.ApartmentEntryOption> options, string exteriorAddress)
        {
            _currentOptions = options?.Where(value => value != null).ToList() ?? new List<StreetQuestShared.ApartmentEntryOption>();
        }

        private void HandleEntryClicked(StreetQuestShared.ApartmentEntryOption option)
        {
            if (option == null)
                return;

            StreetQuestShared.LogDebug(
                $"ApartmentEntryClicked character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress} interiorAddress={option.InteriorAddress}");
            StreetQuestShared.NotifyInfo(
                StreetQuestShared.BuildApartmentEntryPlaceholderMessage(option),
                $"streetquest:apartment_entry_click:{option.CharacterId}",
                3.5f);
        }

        private bool IsVisible()
        {
            return _currentOptions != null &&
                   _currentOptions.Count > 0 &&
                   IsInActiveGameSession() &&
                   !StreetQuestShared.IsCityMapOpen() &&
                   !StreetQuestShared.IsIndoorGameplayContextActive();
        }

        private static bool IsInActiveGameSession()
        {
            return SaveGameManager.Current != null && PlayerHelper.PlayerController != null;
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelTexture.SetPixel(0, 0, new Color(0.13f, 0.13f, 0.13f, 0.96f));
            _panelTexture.Apply(false, false);

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = Padding
            };
            _panelStyle.normal.background = _panelTexture;
            _panelStyle.normal.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                wordWrap = true
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            _headerStyle.normal.textColor = Color.white;

            _textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
            _textStyle.normal.textColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        }

        private void ConsumePointerEvents()
        {
            var currentEvent = Event.current;
            if (currentEvent == null || !_panelRect.Contains(currentEvent.mousePosition))
                return;

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.MouseMove:
                case EventType.ScrollWheel:
                    currentEvent.Use();
                    break;
            }
        }

        private static Vector2 GetGuiMousePositionFromInput()
        {
            var mousePosition = Input.mousePosition;
            return new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        }
    }
}
