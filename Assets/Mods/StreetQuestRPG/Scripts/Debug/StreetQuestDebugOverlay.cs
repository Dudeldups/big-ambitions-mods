using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestDebugOverlay : MonoBehaviour
    {
        private const int WindowId = 184240;
        private const float Width = 540f;
        private const float Height = 480f;

        private static Rect _windowRect = new(24f, 24f, Width, Height);
        private static Vector2 _questScroll;
        private static Vector2 _favorScroll;
        private static bool _visible = true;
        private static int _selectedTab;
        private static bool _positionInitialized;
        private static GUIStyle _windowStyle;
        private static GUIStyle _panelStyle;
        private static GUIStyle _tabStyle;
        private static GUIStyle _activeTabStyle;
        private static GUIStyle _headerStyle;
        private static GUIStyle _textStyle;
        private static GUIStyle _buttonStyle;
        private static int _hotControlId;
        private static Texture2D _windowTexture;
        private static Texture2D _panelTexture;
        private static Texture2D _tabTexture;
        private static Texture2D _activeTabTexture;

        private enum DebugTab
        {
            Quests = 0,
            Favor = 1
        }

        public void TickToggle()
        {
            if (!StreetQuestDebugSettings.Enabled)
                return;

            if (Input.GetKeyDown(StreetQuestDebugSettings.ToggleOverlayKey))
                _visible = !_visible;
        }

        public bool ShouldBlockGameplayInput()
        {
            return StreetQuestDebugSettings.Enabled && _visible && IsMouseOverWindow();
        }

        private void OnGUI()
        {
            if (!StreetQuestDebugSettings.Enabled || !_visible)
                return;

            EnsureStyles();
            CaptureHotControl();
            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "StreetQuest Debug", _windowStyle);
            ConsumePointerEvents();
            ConsumeScrollWheelIfMouseOverWindow();
        }

        private void DrawWindow(int windowId)
        {
            var playerPosition = StreetQuestShared.GetPlayerWorldPosition();
            GUILayout.Label(
                $"Position: {playerPosition.x:0.00}, {playerPosition.y:0.00}, {playerPosition.z:0.00}",
                _headerStyle);

            GUILayout.Space(6f);
            DrawTabs();
            GUILayout.Space(6f);

            GUILayout.BeginVertical(_panelStyle, GUILayout.ExpandHeight(true));
            switch ((DebugTab)_selectedTab)
            {
                case DebugTab.Quests:
                    DrawQuestsTab();
                    break;
                case DebugTab.Favor:
                    DrawFavorTab();
                    break;
            }
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Toggle: {StreetQuestDebugSettings.ToggleOverlayKey}", _textStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Hide", _buttonStyle, GUILayout.Width(100f)))
                _visible = false;
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 28f));
        }

        private static void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            DrawTabButton(DebugTab.Quests, "Quests");
            DrawTabButton(DebugTab.Favor, "Favor");
            GUILayout.EndHorizontal();
        }

        private static void DrawTabButton(DebugTab tab, string label)
        {
            var isActive = _selectedTab == (int)tab;
            if (GUILayout.Button(label, isActive ? _activeTabStyle : _tabStyle, GUILayout.Height(32f)))
                _selectedTab = (int)tab;
        }

        private static void DrawQuestsTab()
        {
            _questScroll = GUILayout.BeginScrollView(_questScroll, GUILayout.ExpandHeight(true));

            var state = StreetQuestShared.GetQuestStateSnapshot();
            var currentQuest = StreetQuestShared.GetCurrentQuest();
            var currentProgress = currentQuest != null
                ? StreetQuestShared.GetQuestProgress(currentQuest.Id).ToString()
                : "None";

            GUILayout.Label("Accepted Quest", _headerStyle);
            if (currentQuest == null)
            {
                GUILayout.Label("No active quest.", _textStyle);
            }
            else
            {
                GUILayout.Label($"ID: {currentQuest.Id}", _textStyle);
                GUILayout.Label($"Giver: {currentQuest.GiverCharacterId}", _textStyle);
                GUILayout.Label($"Turn-in: {currentQuest.TurnInCharacterId}", _textStyle);
                GUILayout.Label($"State: {currentProgress}", _textStyle);
                GUILayout.Label("Objectives", _headerStyle);
                foreach (var objective in currentQuest.Objectives.Where(value => value != null))
                {
                    var satisfied = StreetQuestShared.IsObjectiveSatisfiedForDebug(currentQuest, objective);
                    GUILayout.Label(
                        $"{(satisfied ? "[Done]" : "[ ]")} {BuildObjectiveDebugText(currentQuest, objective)}",
                        _textStyle);
                    DrawObjectiveDebugActions(objective, satisfied);
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("Completed Quests", _headerStyle);
            if (state.CompletedQuestIds.Count == 0)
            {
                GUILayout.Label("None yet.", _textStyle);
            }
            else
            {
                foreach (var questId in state.CompletedQuestIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                    GUILayout.Label(questId, _textStyle);
            }

            GUILayout.EndScrollView();
        }

        private static void DrawFavorTab()
        {
            _favorScroll = GUILayout.BeginScrollView(_favorScroll, GUILayout.ExpandHeight(true));

            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null && value.enabled))
            {
                GUILayout.BeginVertical(_panelStyle);
                GUILayout.Label(character.displayName ?? character.id, _headerStyle);
                GUILayout.Label($"Favor: {StreetQuestShared.GetFavor(character.id)}", _textStyle);
                GUILayout.Label($"Position: {FormatVector3(character.PositionOr(Vector3.zero))}", _textStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Teleport", _buttonStyle, GUILayout.Width(120f)))
                    StreetQuestShared.TeleportPlayerToCharacter(character.id);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(6f);
            }

            GUILayout.EndScrollView();
        }

        private static string BuildObjectiveDebugText(
            StreetQuestQuestDefinition quest,
            StreetQuestQuestObjectiveDefinition objective)
        {
            return objective.ObjectiveType switch
            {
                StreetQuestQuestObjectiveType.BringItem => $"Bring {objective.Amount}x {objective.ItemName}",
                StreetQuestQuestObjectiveType.TalkToCharacter => $"Talk to {objective.CharacterId}",
                StreetQuestQuestObjectiveType.VisitLocation => $"Visit {FormatVector3(objective.worldPosition != null ? objective.worldPosition.ToVector3() : Vector3.zero)} r={objective.Radius:0.0}",
                StreetQuestQuestObjectiveType.HaveStoryFlag => $"Flag {objective.StoryFlagId}",
                StreetQuestQuestObjectiveType.CompleteQuest => $"Complete {objective.QuestId}",
                _ => objective.Id
            };
        }

        private static string FormatVector3(Vector3 value) => $"{value.x:0.00}, {value.y:0.00}, {value.z:0.00}";

        private static void DrawObjectiveDebugActions(
            StreetQuestQuestObjectiveDefinition objective,
            bool satisfied)
        {
            if (objective == null)
                return;

            GUILayout.BeginHorizontal();

            if (!satisfied &&
                objective.ObjectiveType == StreetQuestQuestObjectiveType.BringItem &&
                objective.InventorySource != StreetQuestQuestInventorySource.Quest &&
                !string.IsNullOrWhiteSpace(objective.ItemName))
            {
                var amountToGive = Mathf.Max(1, objective.Amount - StreetQuestShared.GetPlayerItemAmount(objective.ItemName));
                if (GUILayout.Button($"Spawn item ({amountToGive})", _buttonStyle, GUILayout.Width(150f)))
                    StreetQuestShared.TryGivePlayerQuestItem(objective.ItemName, amountToGive);
            }

            if (!satisfied &&
                objective.ObjectiveType == StreetQuestQuestObjectiveType.BringItem &&
                objective.InventorySource == StreetQuestQuestInventorySource.Quest &&
                objective.worldPosition != null)
            {
                if (GUILayout.Button("Teleport to item", _buttonStyle, GUILayout.Width(150f)))
                    StreetQuestShared.TeleportPlayerToWorldPosition(objective.worldPosition.ToVector3());
            }

            if (objective.ObjectiveType == StreetQuestQuestObjectiveType.VisitLocation &&
                objective.worldPosition != null)
            {
                if (GUILayout.Button("Teleport to item", _buttonStyle, GUILayout.Width(150f)))
                    StreetQuestShared.TeleportPlayerToWorldPosition(objective.worldPosition.ToVector3());
            }

            if (objective.ObjectiveType == StreetQuestQuestObjectiveType.TalkToCharacter &&
                !string.IsNullOrWhiteSpace(objective.CharacterId))
            {
                if (GUILayout.Button("Teleport to NPC", _buttonStyle, GUILayout.Width(150f)))
                    StreetQuestShared.TeleportPlayerToCharacter(objective.CharacterId);
            }

            GUILayout.EndHorizontal();
        }

        private static void EnsureStyles()
        {
            if (!_positionInitialized)
            {
                _windowRect.x = 24f;
                _windowRect.y = Mathf.Max(24f, (Screen.height - _windowRect.height) * 0.5f);
                _positionInitialized = true;
            }

            if (_windowStyle != null)
                return;

            _windowTexture ??= CreateTexture(new Color(0.14f, 0.14f, 0.14f, 1f));
            _panelTexture ??= CreateTexture(new Color(0.18f, 0.18f, 0.18f, 1f));
            _tabTexture ??= CreateTexture(new Color(0.26f, 0.26f, 0.26f, 1f));
            _activeTabTexture ??= CreateTexture(Color.white);

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.fontSize = 15;
            _windowStyle.normal.background = _windowTexture;
            _windowStyle.onNormal.background = _windowTexture;
            _windowStyle.normal.textColor = Color.white;
            _windowStyle.padding = new RectOffset(10, 10, 24, 10);

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.textColor = Color.white;
            _panelStyle.normal.background = _panelTexture;

            _tabStyle = new GUIStyle(GUI.skin.button);
            _tabStyle.fontSize = 13;
            _tabStyle.normal.background = _tabTexture;
            _tabStyle.hover.background = _tabTexture;
            _tabStyle.active.background = _tabTexture;
            _tabStyle.focused.background = _tabTexture;
            _tabStyle.onNormal.background = _tabTexture;
            _tabStyle.onHover.background = _tabTexture;
            _tabStyle.onActive.background = _tabTexture;
            _tabStyle.onFocused.background = _tabTexture;
            _tabStyle.normal.textColor = Color.white;
            _tabStyle.hover.textColor = Color.white;
            _tabStyle.active.textColor = Color.white;
            _tabStyle.focused.textColor = Color.white;

            _activeTabStyle = new GUIStyle(_tabStyle);
            _activeTabStyle.normal.textColor = Color.black;
            _activeTabStyle.hover.textColor = Color.black;
            _activeTabStyle.active.textColor = Color.black;
            _activeTabStyle.focused.textColor = Color.black;
            _activeTabStyle.onNormal.textColor = Color.black;
            _activeTabStyle.onHover.textColor = Color.black;
            _activeTabStyle.onActive.textColor = Color.black;
            _activeTabStyle.onFocused.textColor = Color.black;
            _activeTabStyle.normal.background = _activeTabTexture;
            _activeTabStyle.hover.background = _activeTabTexture;
            _activeTabStyle.active.background = _activeTabTexture;
            _activeTabStyle.focused.background = _activeTabTexture;
            _activeTabStyle.onNormal.background = _activeTabTexture;
            _activeTabStyle.onHover.background = _activeTabTexture;
            _activeTabStyle.onActive.background = _activeTabTexture;
            _activeTabStyle.onFocused.background = _activeTabTexture;

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = 14;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = Color.white;

            _textStyle = new GUIStyle(GUI.skin.label);
            _textStyle.fontSize = 12;
            _textStyle.wordWrap = true;
            _textStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = 12;
        }

        private static Texture2D CreateTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        private static void ConsumeScrollWheelIfMouseOverWindow()
        {
            var currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.ScrollWheel)
                return;

            if (_windowRect.Contains(currentEvent.mousePosition))
                currentEvent.Use();
        }

        private static void ConsumePointerEvents()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (!IsMouseOverWindow())
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

        private static void CaptureHotControl()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (_hotControlId == 0)
                _hotControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (!IsMouseOverWindow())
            {
                if (GUIUtility.hotControl == _hotControlId &&
                    (currentEvent.type == EventType.MouseUp || currentEvent.rawType == EventType.MouseUp))
                {
                    GUIUtility.hotControl = 0;
                }

                return;
            }

            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                    GUIUtility.hotControl = _hotControlId;
                    currentEvent.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == _hotControlId)
                        GUIUtility.hotControl = 0;

                    currentEvent.Use();
                    break;
            }
        }

        private static bool IsMouseOverWindow()
        {
            return _windowRect.Contains(GetGuiMousePositionFromInput());
        }

        private static Vector2 GetGuiMousePositionFromInput()
        {
            var mousePosition = Input.mousePosition;
            return new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        }
    }
}
