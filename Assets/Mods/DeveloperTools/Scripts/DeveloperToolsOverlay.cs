#nullable enable
using System;
using System.Globalization;
using System.Linq;
using BAModAPI;
using UnityEngine;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsOverlay
    {
        private const int WindowId = 734921;
        private const float WindowWidth = 760f;
        private const float WindowHeight = 820f;
        private readonly ModContext context;
        private readonly DeveloperToolsVehicleService vehicles;
        private readonly DeveloperToolsItemService items;
        private readonly DeveloperToolsPlayerService player;
        private readonly DeveloperToolsTimeService time;
        private Rect windowRect = new Rect(40f, 30f, WindowWidth, WindowHeight);
        private Vector2 mainScroll;
        private Vector2 vehicleScroll;
        private Vector2 itemScroll;
        private bool vehicleDropdownOpen;
        private bool itemDropdownOpen;
        private bool visible;
        private bool cursorWasVisible;
        private CursorLockMode previousCursorLock;
        private string selectedVehicleId = string.Empty;
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
            DeveloperToolsTimeService time)
        {
            this.context = context;
            this.vehicles = vehicles;
            this.items = items;
            this.player = player;
            this.time = time;
        }

        public bool IsVisible => visible;
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
            cursorWasVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            vehicles.Refresh();
            items.EnsurePopulated();
            if (vehicles.Entries.Count > 0 && vehicles.Entries.All(entry => entry.Id != selectedVehicleId))
                selectedVehicleId = vehicles.Entries[0].Id;
            if (items.Entries.Count > 0 && items.Entries.All(entry => entry.Id != selectedItemId))
                selectedItemId = items.Entries[0].Id;
            SetCoordinatesFromPlayer();
            status = "Catalogs ready. City-map double-click teleport is active whenever the map is open.";
            context.Logger.Info("DeveloperTools: testing UI opened; vehicles=" + vehicles.Entries.Count + ", vanillaItems=" + items.Entries.Count + ".");
        }

        public void Hide()
        {
            if (!visible)
                return;
            visible = false;
            vehicleDropdownOpen = false;
            itemDropdownOpen = false;
            Cursor.visible = cursorWasVisible;
            Cursor.lockState = previousCursorLock;
            context.Logger.Info("DeveloperTools: testing UI closed.");
        }

        public void OnGui()
        {
            if (!visible)
                return;

            windowRect.width = Mathf.Min(WindowWidth, Screen.width - 20f);
            windowRect.height = Mathf.Min(WindowHeight, Screen.height - 20f);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Developer Tools", GUILayout.Width(windowRect.width), GUILayout.Height(windowRect.height));
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
            var selected = vehicles.Entries.FirstOrDefault(entry => entry.Id == selectedVehicleId);
            if (GUILayout.Button(selected == null ? "Select vehicle..." : selected.DisplayName + "  ▼"))
                vehicleDropdownOpen = !vehicleDropdownOpen;
            if (vehicleDropdownOpen)
            {
                vehicleScroll = GUILayout.BeginScrollView(vehicleScroll, GUI.skin.box, GUILayout.Height(150f));
                foreach (var entry in vehicles.Entries)
                {
                    if (!GUILayout.Button(entry.DisplayName + "  [" + entry.Id + "]")) continue;
                    selectedVehicleId = entry.Id;
                    vehicleDropdownOpen = false;
                }
                GUILayout.EndScrollView();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn")) vehicles.Spawn(selectedVehicleId, out status);
            if (GUILayout.Button("Despawn Last Spawned Vehicle")) vehicles.DespawnLast(out status);
            GUILayout.EndHorizontal();
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
            GUILayout.Label("Time Advancer (game simulation)", GUI.skin.box);
            GUILayout.BeginHorizontal();
            TimeButton("1 Hour", 1);
            TimeButton("12 Hours", 12);
            TimeButton("1 Day", 24);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TimeButton("2 Days", 48);
            TimeButton("3 Days", 72);
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

        private static void Divider()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUILayout.Space(5f);
        }
    }
}
