#nullable enable
using System;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.InputSystem;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsOverlay
    {
        private const int WindowId = 734921;
        private const float WindowWidth = 760f;
        private const float WindowHeight = 820f;
        private const float VehicleDropdownHeight = 270f;
        private readonly ModContext context;
        private readonly DeveloperToolsVehicleService vehicles;
        private readonly DeveloperToolsItemService items;
        private readonly DeveloperToolsPlayerService player;
        private readonly DeveloperToolsTimeService time;
        private readonly DeveloperToolsTrafficService traffic;
        private readonly DeveloperToolsPauseService pause;
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private readonly List<object> suspendedGameplayActions = new List<object>();
        private readonly FieldInfo? playerActionMapField = typeof(InputActionHelper).GetField("PlayerInputActionMap", BindingFlags.Public | BindingFlags.Static);
        private readonly FieldInfo? designerActionMapField = typeof(InputActionHelper).GetField("InteriorDesignerInputActionMap", BindingFlags.Public | BindingFlags.Static);
        private readonly Type? graphicRaycasterType = Type.GetType("UnityEngine.UI.GraphicRaycaster, UnityEngine.UI");
        private readonly Type? imageType = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
        private PropertyInfo? actionEnabledProperty;
        private MethodInfo? actionDisableMethod;
        private MethodInfo? actionEnableMethod;
        private GameObject? uiInputBlocker;
        private GUISkin? customSkin;
        private Rect windowRect = new Rect(40f, 30f, WindowWidth, WindowHeight);
        private Vector2 mainScroll;
        private Vector2 vanillaVehicleScroll;
        private Vector2 moddedVehicleScroll;
        private Vector2 vehicleColorScroll;
        private Vector2 itemScroll;
        private bool vanillaVehicleDropdownOpen;
        private bool moddedVehicleDropdownOpen;
        private bool vehicleColorDropdownOpen;
        private bool itemDropdownOpen;
        private bool visible;
        private int inputReleaseBlockFrames;
        private bool cursorRestorePending;
        private bool cursorWasVisible;
        private CursorLockMode previousCursorLock;
        private string selectedVanillaVehicleId = string.Empty;
        private string selectedModdedVehicleId = string.Empty;
        private string selectedVehicleColorName = string.Empty;
        private string selectedItemId = string.Empty;
        private string itemSearch = string.Empty;
        private string itemAmount = "1";
        private string customMoney = "10000000";
        private string x = "0";
        private string y = "0";
        private string z = "0";
        private string status = "Ready.";

        public DeveloperToolsOverlay(
            ModContext context,
            DeveloperToolsVehicleService vehicles,
            DeveloperToolsItemService items,
            DeveloperToolsPlayerService player,
            DeveloperToolsTimeService time,
            DeveloperToolsTrafficService traffic,
            DeveloperToolsPauseService pause)
        {
            this.context = context;
            this.vehicles = vehicles;
            this.items = items;
            this.player = player;
            this.time = time;
            this.traffic = traffic;
            this.pause = pause;
        }

        public bool IsVisible => visible;
        public bool ShouldConsumeGameplayInput => visible || inputReleaseBlockFrames > 0;
        public bool IsPointerInsideWindow
        {
            get
            {
                var guiMousePosition = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return visible && windowRect.Contains(guiMousePosition);
            }
        }

        public void Toggle()
        {
            if (visible) Hide(); else Show();
        }

        private void Show()
        {
            visible = true;
            if (!cursorRestorePending)
            {
                cursorWasVisible = Cursor.visible;
                previousCursorLock = Cursor.lockState;
            }
            cursorRestorePending = false;
            inputReleaseBlockFrames = 0;
            SetUiInputBlockerActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            SuspendGameplayActions();
            pause.PauseForOverlay();
            vehicles.Refresh();
            items.EnsurePopulated();
            SelectFirstAvailable(vehicles.VanillaEntries, ref selectedVanillaVehicleId);
            SelectFirstAvailable(vehicles.ModdedEntries, ref selectedModdedVehicleId);
            if (vehicles.ColorEntries.All(entry => entry.Name != selectedVehicleColorName))
                selectedVehicleColorName = vehicles.GetDefaultRedColorName();
            if (items.Entries.Count > 0 && items.Entries.All(entry => entry.Id != selectedItemId))
                selectedItemId = items.Entries[0].Id;
            SetCoordinatesFromPlayer();
            status = "Catalogs ready. City-map double-click teleport is active whenever the map is open.";
        }

        public void Hide()
        {
            if (!visible)
                return;
            visible = false;
            inputReleaseBlockFrames = Math.Max(inputReleaseBlockFrames, 3);
            cursorRestorePending = true;
            vanillaVehicleDropdownOpen = false;
            moddedVehicleDropdownOpen = false;
            vehicleColorDropdownOpen = false;
            itemDropdownOpen = false;
        }

        public void ConsumeGameplayInput()
        {
            if (!ShouldConsumeGameplayInput)
                return;

            // The game's action set remains disabled until the close gesture has
            // drained. IMGUI text entry reads keyboard events independently.
            Input.ResetInputAxes();
            if (!visible)
            {
                inputReleaseBlockFrames--;
                if (inputReleaseBlockFrames <= 0)
                    RestoreCursor();
            }
        }

        public void Shutdown()
        {
            Hide();
            RestoreCursor();
            if (customSkin != null)
                UnityEngine.Object.Destroy(customSkin);
            customSkin = null;
            if (uiInputBlocker != null)
                UnityEngine.Object.Destroy(uiInputBlocker);
            uiInputBlocker = null;
            foreach (var texture in ownedTextures)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            ownedTextures.Clear();
        }

        private void RestoreCursor()
        {
            if (!cursorRestorePending)
                return;

            cursorRestorePending = false;
            SetUiInputBlockerActive(false);
            pause.ResumeAfterOverlay();
            RestoreGameplayActions();
            Cursor.visible = cursorWasVisible;
            Cursor.lockState = previousCursorLock;
        }

        private void SetUiInputBlockerActive(bool active)
        {
            if (uiInputBlocker == null && active)
            {
                if (graphicRaycasterType == null || imageType == null)
                {
                    context.Logger.Warn("DeveloperTools: could not create the uGUI input blocker because Unity UI types were unavailable.");
                    return;
                }

                uiInputBlocker = new GameObject(
                    "DeveloperToolsUiInputBlocker",
                    typeof(RectTransform),
                    typeof(Canvas));
                UnityEngine.Object.DontDestroyOnLoad(uiInputBlocker);
                uiInputBlocker.AddComponent(graphicRaycasterType);

                var canvas = uiInputBlocker.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = short.MaxValue;

                var image = uiInputBlocker.AddComponent(imageType);
                imageType.GetProperty("color", BindingFlags.Public | BindingFlags.Instance)?.SetValue(image, Color.clear);
                imageType.GetProperty("raycastTarget", BindingFlags.Public | BindingFlags.Instance)?.SetValue(image, true);

                var rect = uiInputBlocker.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            if (uiInputBlocker != null)
                uiInputBlocker.SetActive(active);
        }

        private void SuspendGameplayActions()
        {
            if (suspendedGameplayActions.Count > 0)
                return;

            SuspendActionMap(playerActionMapField?.GetValue(null) as IDictionary);
            SuspendActionMap(designerActionMapField?.GetValue(null) as IDictionary);
        }

        private void SuspendActionMap(IDictionary? actionMap)
        {
            if (actionMap == null)
                return;

            foreach (DictionaryEntry entry in actionMap)
                SuspendAction(entry.Value);
        }

        private void SuspendAction(object? action)
        {
            if (action == null || suspendedGameplayActions.Contains(action))
                return;

            CacheActionMembers(action.GetType());
            if (actionEnabledProperty?.GetValue(action) is not bool enabled || !enabled)
                return;

            actionDisableMethod?.Invoke(action, null);
            suspendedGameplayActions.Add(action);
        }

        private void RestoreGameplayActions()
        {
            foreach (var action in suspendedGameplayActions)
                actionEnableMethod?.Invoke(action, null);
            suspendedGameplayActions.Clear();
        }

        private void CacheActionMembers(Type actionType)
        {
            actionEnabledProperty ??= actionType.GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance);
            actionDisableMethod ??= actionType.GetMethod("Disable", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            actionEnableMethod ??= actionType.GetMethod("Enable", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        }

        public void OnGui()
        {
            if (!visible)
                return;

            EnsureSkin();

            windowRect.width = Mathf.Min(WindowWidth, Screen.width - 20f);
            windowRect.height = Mathf.Min(WindowHeight, Screen.height - 20f);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
            var previousSkin = GUI.skin;
            try
            {
                GUI.skin = customSkin!;
                windowRect = GUILayout.Window(
                    WindowId,
                    windowRect,
                    DrawWindow,
                    "Developer Tools",
                    customSkin!.window,
                    GUILayout.Width(windowRect.width),
                    GUILayout.Height(windowRect.height));
            }
            finally
            {
                GUI.skin = previousSkin;
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Fast mod-testing utilities", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Close", GUILayout.Width(72f))) Hide();
            GUILayout.EndHorizontal();
            Divider();
            mainScroll = GUILayout.BeginScrollView(mainScroll);
            DrawVehicleSpawner();
            Divider();
            DrawTraffic();
            Divider();
            DrawMoney();
            Divider();
            DrawTimeAdvancer();
            Divider();
            DrawItemSpawner();
            Divider();
            DrawCoordinateTeleport();
            Divider();
            DrawPlayerPosition();
            Divider();
            DrawPlayerNeeds();
            Divider();
            GUILayout.Label("City Map Teleport", GUI.skin.box);
            GUILayout.Label("Double-click a non-UI location while the city map is open. Input processing is dormant while the map is closed, and the landing point is snapped to nearby navigation/ground geometry.");
            GUILayout.EndScrollView();
            GUILayout.Space(4f);
            GUILayout.Label("Status: " + status, GUI.skin.box);
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 85f, 28f));
        }

        private void DrawVehicleSpawner()
        {
            GUILayout.Label("Vehicle Spawner", GUI.skin.box);
            DrawVehicleColorPicker();
            GUILayout.Space(5f);
            DrawVehicleCatalog(
                "Vanilla Vehicles",
                vehicles.VanillaEntries,
                ref selectedVanillaVehicleId,
                ref vanillaVehicleDropdownOpen,
                ref vanillaVehicleScroll);
            GUILayout.Space(5f);
            DrawVehicleCatalog(
                "Modded Vehicles",
                vehicles.ModdedEntries,
                ref selectedModdedVehicleId,
                ref moddedVehicleDropdownOpen,
                ref moddedVehicleScroll);
            var previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.38f, 0.72f, 0.42f, 1f);
            if (GUILayout.Button("Repair Vehicle"))
                vehicles.RepairVehicle(out status);
            GUI.backgroundColor = previousBackgroundColor;
            if (GUILayout.Button("Despawn Last Spawned Vehicle"))
                vehicles.DespawnLast(out status);
        }

        private void DrawTraffic()
        {
            GUILayout.Label("Traffic", GUI.skin.box);
            GUILayout.Label("NPC Vehicle Traffic");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Vanilla (1x)"))
                traffic.SetTrafficMultiplier(1f, out status);
            if (GUILayout.Button("NPC Traffic 2x"))
                traffic.SetTrafficMultiplier(2f, out status);
            if (GUILayout.Button("NPC Traffic 5x"))
                traffic.SetTrafficMultiplier(5f, out status);
            GUILayout.EndHorizontal();

            var previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.82f, 0.38f, 0.32f, 1f);
            if (GUILayout.Button("Disable Traffic"))
                traffic.DisableTraffic(out status);

            GUILayout.Space(5f);
            GUILayout.Label("Parked Cars");
            GUI.backgroundColor = traffic.ParkedCarsEnabled
                ? new Color(0.86f, 0.55f, 0.24f, 1f)
                : new Color(0.38f, 0.72f, 0.42f, 1f);
            if (GUILayout.Button(traffic.ParkedCarsEnabled ? "Disable Parked Cars" : "Enable Parked Cars"))
                traffic.ToggleParkedCars(out status);
            GUI.backgroundColor = previousBackgroundColor;
        }

        private void DrawVehicleCatalog(
            string label,
            IReadOnlyList<CatalogEntry> entries,
            ref string selectedId,
            ref bool dropdownOpen,
            ref Vector2 scroll)
        {
            GUILayout.Label(label);
            if (entries.Count == 0)
            {
                GUILayout.Label("No vehicles available in this category.", GUI.skin.box);
                return;
            }

            CatalogEntry? selected = null;
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Id != selectedId) continue;
                selected = entries[index];
                break;
            }
            if (GUILayout.Button(selected == null ? "Select vehicle..." : selected.DisplayName + "  ▼"))
                dropdownOpen = !dropdownOpen;
            if (dropdownOpen)
            {
                scroll = GUILayout.BeginScrollView(scroll, GUI.skin.box, GUILayout.Height(VehicleDropdownHeight));
                foreach (var entry in entries)
                {
                    if (!GUILayout.Button(entry.DisplayName + "  [" + entry.Id + "]")) continue;
                    selectedId = entry.Id;
                    dropdownOpen = false;
                }
                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("Spawn " + label.TrimEnd('s')))
                vehicles.Spawn(selectedId, selectedVehicleColorName, out status);
        }

        private void DrawVehicleColorPicker()
        {
            GUILayout.Label("Vehicle Color");
            if (vehicles.ColorEntries.Count == 0)
            {
                GUILayout.Label("No registered vehicle colors are available.", GUI.skin.box);
                return;
            }
            var selected = vehicles.ColorEntries.FirstOrDefault(entry => entry.Name == selectedVehicleColorName);
            var previousBackgroundColor = GUI.backgroundColor;
            if (selected != null)
                GUI.backgroundColor = selected.Tint;
            if (GUILayout.Button(selected == null ? "Select color..." : selected.Name + "  ▼"))
                vehicleColorDropdownOpen = !vehicleColorDropdownOpen;
            GUI.backgroundColor = previousBackgroundColor;

            if (!vehicleColorDropdownOpen)
                return;

            vehicleColorScroll = GUILayout.BeginScrollView(
                vehicleColorScroll,
                GUI.skin.box,
                GUILayout.Height(VehicleDropdownHeight));
            foreach (var entry in vehicles.ColorEntries)
            {
                GUI.backgroundColor = entry.Tint;
                if (!GUILayout.Button(entry.Name))
                    continue;
                selectedVehicleColorName = entry.Name;
                vehicleColorDropdownOpen = false;
            }
            GUI.backgroundColor = previousBackgroundColor;
            GUILayout.EndScrollView();
        }

        private void DrawMoney()
        {
            GUILayout.Label("Money", GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add $10,000,000")) player.AddMoney(10000000f, out status);
            GUILayout.Label("Custom:", GUILayout.Width(58f));
            customMoney = GUILayout.TextField(customMoney, GUILayout.Width(140f));
            if (GUILayout.Button("Add Custom", GUILayout.Width(110f)))
            {
                if (TryParseFloat(customMoney, out var amount) && amount > 0f) player.AddMoney(amount, out status);
                else status = "Custom money amount must be a positive number.";
            }
            GUILayout.EndHorizontal();
        }

        private void DrawTimeAdvancer()
        {
            GUILayout.Label("Time Advancer (6x game simulation)", GUI.skin.box);
            GUILayout.BeginHorizontal();
            TimeButton("1 Hour", 1);
            TimeButton("6 Hours", 6);
            TimeButton("12 Hours", 12);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TimeButton("1 Day", 24);
            TimeButton("2 Days", 48);
            TimeButton("3 Days", 72);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TimeButton("7 Days", 168);
            GUILayout.EndHorizontal();
        }

        private void TimeButton(string label, int hours)
        {
            if (!GUILayout.Button(label)) return;
            if (time.AdvanceHours(hours, out status)) Hide();
        }

        private void DrawItemSpawner()
        {
            GUILayout.Label("Vanilla Item Spawner", GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(55f));
            itemSearch = GUILayout.TextField(itemSearch);
            GUILayout.Label("Amount:", GUILayout.Width(58f));
            itemAmount = GUILayout.TextField(itemAmount, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            var selected = items.Entries.FirstOrDefault(entry => entry.Id == selectedItemId);
            if (GUILayout.Button(selected == null ? "Select item..." : selected.DisplayName + "  ▼"))
                itemDropdownOpen = !itemDropdownOpen;
            if (itemDropdownOpen)
            {
                itemScroll = GUILayout.BeginScrollView(itemScroll, GUI.skin.box, GUILayout.Height(180f));
                var filter = itemSearch.Trim();
                var shown = 0;
                foreach (var entry in items.Entries)
                {
                    if (filter.Length > 0 && entry.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        entry.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    shown++;
                    if (!GUILayout.Button(entry.DisplayName + "  [" + entry.Id + "]")) continue;
                    selectedItemId = entry.Id;
                    itemDropdownOpen = false;
                }
                if (shown == 0) GUILayout.Label("No matching vanilla items.");
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("Spawn Into Held / Player Inventory"))
            {
                if (int.TryParse(itemAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                    items.Spawn(selectedItemId, amount, out status);
                else status = "Item amount must be a whole number.";
            }
        }

        private void DrawCoordinateTeleport()
        {
            GUILayout.Label("Coordinate Teleport", GUI.skin.box);
            GUILayout.BeginHorizontal();
            CoordinateField("X", ref x);
            CoordinateField("Y", ref y);
            CoordinateField("Z", ref z);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Current Position")) SetCoordinatesFromPlayer();
            if (GUILayout.Button("Teleport"))
            {
                if (TryParseFloat(x, out var px) && TryParseFloat(y, out var py) && TryParseFloat(z, out var pz))
                {
                    if (player.Teleport(new Vector3(px, py, pz), false, out var final, out status))
                    {
                        x = FormatFloat(final.x); y = FormatFloat(final.y); z = FormatFloat(final.z);
                    }
                }
                else status = "X, Y and Z must all be valid numbers.";
            }
            GUILayout.EndHorizontal();
        }

        private static void CoordinateField(string label, ref string value)
        {
            GUILayout.Label(label + ":", GUILayout.Width(18f));
            value = GUILayout.TextField(value, GUILayout.MinWidth(90f));
        }

        private void DrawPlayerPosition()
        {
            GUILayout.Label("Player Position Tools", GUI.skin.box);
            var transform = player.PlayerTransform;
            if (transform == null)
            {
                GUILayout.Label("Player is not available.");
                return;
            }
            var position = transform.position;
            var forward = transform.forward;
            var yaw = transform.eulerAngles.y;
            GUILayout.Label("Position: " + DeveloperToolsPlayerService.FormatVector(position));
            DrawCopyRow("Copy Position", DeveloperToolsPlayerService.FormatVector(position), "Copy Position JSON", DeveloperToolsPlayerService.FormatJson(position));
            GUILayout.Label("Forward: " + DeveloperToolsPlayerService.FormatVector(forward));
            DrawCopyRow("Copy Forward", DeveloperToolsPlayerService.FormatVector(forward), "Copy Forward JSON", DeveloperToolsPlayerService.FormatJson(forward));
            GUILayout.Label("Yaw: " + yaw.ToString("0.###", CultureInfo.InvariantCulture));
            if (GUILayout.Button("Copy Yaw")) Copy(yaw.ToString("0.###", CultureInfo.InvariantCulture), "Yaw copied.");
        }

        private void DrawCopyRow(string firstLabel, string firstValue, string secondLabel, string secondValue)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(firstLabel)) Copy(firstValue, firstLabel + ".");
            if (GUILayout.Button(secondLabel)) Copy(secondValue, secondLabel + ".");
            GUILayout.EndHorizontal();
        }

        private void DrawPlayerNeeds()
        {
            GUILayout.Label("Player Needs", GUI.skin.box);
            if (GUILayout.Button("Reset Hunger, Energy and Happiness to Full"))
                player.ResetNeeds(out status);
        }

        private void SetCoordinatesFromPlayer()
        {
            var transform = player.PlayerTransform;
            if (transform == null) return;
            x = FormatFloat(transform.position.x);
            y = FormatFloat(transform.position.y);
            z = FormatFloat(transform.position.z);
        }

        private void Copy(string value, string confirmation)
        {
            GUIUtility.systemCopyBuffer = value;
            status = confirmation;
        }

        private static bool TryParseFloat(string value, out float result) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
            float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);

        private static string FormatFloat(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static void SelectFirstAvailable(IReadOnlyList<CatalogEntry> entries, ref string selectedId)
        {
            if (entries.Count == 0)
            {
                selectedId = string.Empty;
                return;
            }

            for (var index = 0; index < entries.Count; index++)
                if (entries[index].Id == selectedId) return;
            selectedId = entries[0].Id;
        }

        private static void Divider()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUILayout.Space(5f);
        }

        private void EnsureSkin()
        {
            if (customSkin != null)
                return;

            customSkin = UnityEngine.Object.Instantiate(GUI.skin);
            customSkin.name = "DeveloperToolsOpaqueSkin";
            customSkin.window = CreateStyle(
                GUI.skin.window,
                new Color(0.075f, 0.09f, 0.12f, 1f),
                new Color(0.96f, 0.97f, 0.99f, 1f),
                14,
                FontStyle.Bold);
            customSkin.window.padding = new RectOffset(18, 18, 24, 16);
            customSkin.window.border = new RectOffset(0, 0, 0, 0);
            customSkin.label = CreateTextStyle(GUI.skin.label, new Color(0.94f, 0.95f, 0.97f, 1f), 14);
            customSkin.box = CreateStyle(
                GUI.skin.box,
                new Color(0.14f, 0.17f, 0.22f, 1f),
                new Color(0.96f, 0.97f, 0.99f, 1f),
                15,
                FontStyle.Bold);
            customSkin.box.padding = new RectOffset(8, 8, 6, 6);
            customSkin.button = CreateInteractiveStyle(
                GUI.skin.button,
                new Color(0.20f, 0.47f, 0.78f, 1f),
                new Color(0.25f, 0.56f, 0.90f, 1f),
                new Color(0.14f, 0.36f, 0.64f, 1f));
            customSkin.button.fixedHeight = 30f;
            customSkin.textField = CreateInteractiveStyle(
                GUI.skin.textField,
                new Color(0.055f, 0.065f, 0.085f, 1f),
                new Color(0.08f, 0.10f, 0.14f, 1f),
                new Color(0.04f, 0.05f, 0.07f, 1f));
            customSkin.textField.fixedHeight = 28f;
            customSkin.textField.padding = new RectOffset(7, 7, 4, 4);
            customSkin.scrollView = new GUIStyle(GUI.skin.scrollView)
            {
                normal = { background = MakeSolidTexture(new Color(0.095f, 0.115f, 0.15f, 1f)) }
            };
        }

        private GUIStyle CreateStyle(GUIStyle source, Color background, Color text, int fontSize, FontStyle fontStyle)
        {
            var texture = MakeSolidTexture(background);
            var style = new GUIStyle(source)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                normal = { background = texture, textColor = text },
                hover = { background = texture, textColor = text },
                active = { background = texture, textColor = text },
                focused = { background = texture, textColor = text },
                onNormal = { background = texture, textColor = text },
                onHover = { background = texture, textColor = text },
                onActive = { background = texture, textColor = text },
                onFocused = { background = texture, textColor = text }
            };
            return style;
        }

        private static GUIStyle CreateTextStyle(GUIStyle source, Color text, int fontSize)
        {
            return new GUIStyle(source)
            {
                fontSize = fontSize,
                normal = { textColor = text },
                hover = { textColor = text },
                active = { textColor = text },
                focused = { textColor = text },
                onNormal = { textColor = text },
                onHover = { textColor = text },
                onActive = { textColor = text },
                onFocused = { textColor = text }
            };
        }

        private GUIStyle CreateInteractiveStyle(GUIStyle source, Color normal, Color hover, Color active)
        {
            var normalTexture = MakeSolidTexture(normal);
            var hoverTexture = MakeSolidTexture(hover);
            var activeTexture = MakeSolidTexture(active);
            var text = new Color(0.98f, 0.99f, 1f, 1f);
            return new GUIStyle(source)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { background = normalTexture, textColor = text },
                hover = { background = hoverTexture, textColor = text },
                active = { background = activeTexture, textColor = text },
                focused = { background = hoverTexture, textColor = text },
                onNormal = { background = activeTexture, textColor = text },
                onHover = { background = hoverTexture, textColor = text },
                onActive = { background = activeTexture, textColor = text },
                onFocused = { background = hoverTexture, textColor = text }
            };
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "DeveloperToolsUiColor",
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            ownedTextures.Add(texture);
            return texture;
        }
    }
}
