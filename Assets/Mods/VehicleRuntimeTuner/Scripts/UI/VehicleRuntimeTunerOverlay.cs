#nullable enable
using UnityEngine;
using VehicleRuntimeTuner.Runtime;
using VehicleRuntimeTuner.Vehicle;

namespace VehicleRuntimeTuner.UI
{
    public sealed class VehicleRuntimeTunerOverlay
    {
        private const string TextFieldControlPrefix = "VehicleRuntimeTunerField_";

        private Rect windowRect = new Rect(20f, 200f, 860f, 760f);
        private bool positionInitialized;
        private Vector2 scrollPosition;
        private Texture2D? blackTexture;
        private Texture2D? greyTexture;
        private GUIStyle? windowStyle;
        private GUIStyle? labelStyle;
        private GUIStyle? textFieldStyle;
        private GUIStyle? buttonStyle;

        public bool Draw(
            VehicleRuntimeTunerState state,
            ActiveVehicleInfo? activeVehicle,
            System.Action onApply,
            System.Action onSave,
            System.Action onLoad,
            System.Action onDump,
            System.Action onExport,
            System.Action onRefresh,
            System.Action onRespawnTestVehicle,
            System.Action onTeleportToGround,
            System.Action onResetVelocity)
        {
            EnsureLayoutAndStyles();
            windowRect = GUI.Window(
                641023,
                windowRect,
                _ => DrawWindow(
                    state,
                    activeVehicle,
                    onApply,
                    onSave,
                    onLoad,
                    onDump,
                    onExport,
                    onRefresh,
                    onRespawnTestVehicle,
                    onTeleportToGround,
                    onResetVelocity),
                "Vehicle Runtime Tuner",
                windowStyle);

            var focusedControl = GUI.GetNameOfFocusedControl();
            var textFieldFocused = !string.IsNullOrWhiteSpace(focusedControl) &&
                                   focusedControl.StartsWith(TextFieldControlPrefix, System.StringComparison.Ordinal);

            ConsumeScrollWheelIfMouseOverWindow();
            if (textFieldFocused)
                ConsumeGameplayInputEvent();

            return textFieldFocused;
        }

        public bool IsMouseOverWindow()
        {
            var mousePosition = Input.mousePosition;
            var guiMousePosition = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
            return windowRect.Contains(guiMousePosition);
        }

        private void DrawWindow(
            VehicleRuntimeTunerState state,
            ActiveVehicleInfo? activeVehicle,
            System.Action onApply,
            System.Action onSave,
            System.Action onLoad,
            System.Action onDump,
            System.Action onExport,
            System.Action onRefresh,
            System.Action onRespawnTestVehicle,
            System.Action onTeleportToGround,
            System.Action onResetVelocity)
        {
            HandleOverlayHotkeys(onApply, onSave, onLoad, onDump);

            GUILayout.BeginVertical();
            GUILayout.Label($"Active vehicle: {(activeVehicle?.VehicleTypeName ?? "No active vehicle")}", labelStyle);
            GUILayout.Label($"Instance id: {(activeVehicle?.VehicleInstanceId ?? "-")}", labelStyle);
            DrawTabBar(state);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.ExpandHeight(true));
            if (state.SelectedTabIndex == 0)
            {
                DrawBody(state.FieldBuffer, state.DefaultValues);
                DrawEngine(state.FieldBuffer, state.DefaultValues);
                DrawBrakes(state.FieldBuffer, state.DefaultValues);
                DrawSuspension(state.FieldBuffer, state.DefaultValues);
                DrawWheels(state.FieldBuffer, state.DefaultValues);
            }
            else
            {
                DrawLayout(state.LayoutBuffer, state.LayoutDefaults);
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply", buttonStyle)) onApply();
            if (GUILayout.Button("Save JSON", buttonStyle)) onSave();
            if (GUILayout.Button("Load JSON", buttonStyle)) onLoad();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dump Runtime", buttonStyle)) onDump();
            if (GUILayout.Button("Export Markdown", buttonStyle)) onExport();
            if (GUILayout.Button("Refresh", buttonStyle)) onRefresh();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Respawn Test", buttonStyle)) onRespawnTestVehicle();
            if (GUILayout.Button("Snap To Ground", buttonStyle)) onTeleportToGround();
            if (GUILayout.Button("Reset Velocity", buttonStyle)) onResetVelocity();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (state.StatusMessage.HasVisibleMessage)
                GUILayout.Label(state.StatusMessage.Text, labelStyle);

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawBody(VehicleRuntimeTunerFieldBuffer buffer, VehicleRuntimeTunerDefaultValues defaults)
        {
            GUILayout.Space(4f);
            DrawSectionLabel("[Body]");
            DrawTextField("Mass", nameof(buffer.Mass), ref buffer.Mass, defaults.Mass);
            DrawTextField("Drag", nameof(buffer.Drag), ref buffer.Drag, defaults.Drag);
            DrawTextField("Angular Drag", nameof(buffer.AngularDrag), ref buffer.AngularDrag, defaults.AngularDrag);
            DrawTextField("Center Of Mass X", nameof(buffer.CenterOfMassX), ref buffer.CenterOfMassX, defaults.CenterOfMassX);
            DrawTextField("Center Of Mass Y", nameof(buffer.CenterOfMassY), ref buffer.CenterOfMassY, defaults.CenterOfMassY);
            DrawTextField("Center Of Mass Z", nameof(buffer.CenterOfMassZ), ref buffer.CenterOfMassZ, defaults.CenterOfMassZ);
        }

        private void DrawEngine(VehicleRuntimeTunerFieldBuffer buffer, VehicleRuntimeTunerDefaultValues defaults)
        {
            GUILayout.Space(4f);
            DrawSectionLabel("[Engine]");
            DrawTextField("Engine Power", nameof(buffer.EnginePower), ref buffer.EnginePower, defaults.EnginePower);
            DrawTextField("Max Speed", nameof(buffer.MaxSpeed), ref buffer.MaxSpeed, defaults.MaxSpeed);
        }

        private void DrawBrakes(VehicleRuntimeTunerFieldBuffer buffer, VehicleRuntimeTunerDefaultValues defaults)
        {
            GUILayout.Space(4f);
            DrawSectionLabel("[Brakes]");
            DrawTextField("Brake Torque", nameof(buffer.BrakeTorque), ref buffer.BrakeTorque, defaults.BrakeTorque);
        }

        private void DrawSuspension(VehicleRuntimeTunerFieldBuffer buffer, VehicleRuntimeTunerDefaultValues defaults)
        {
            GUILayout.Space(4f);
            DrawSectionLabel("[Suspension]");
            DrawTextField("Front Spring", nameof(buffer.FrontSpring), ref buffer.FrontSpring, defaults.FrontSpring);
            DrawTextField("Front Damper", nameof(buffer.FrontDamper), ref buffer.FrontDamper, defaults.FrontDamper);
            DrawTextField("Front Target", nameof(buffer.FrontTarget), ref buffer.FrontTarget, defaults.FrontTarget);
            DrawTextField("Front Distance", nameof(buffer.FrontSuspensionDistance), ref buffer.FrontSuspensionDistance, defaults.FrontSuspensionDistance);
            DrawTextField("Rear Spring", nameof(buffer.RearSpring), ref buffer.RearSpring, defaults.RearSpring);
            DrawTextField("Rear Damper", nameof(buffer.RearDamper), ref buffer.RearDamper, defaults.RearDamper);
            DrawTextField("Rear Target", nameof(buffer.RearTarget), ref buffer.RearTarget, defaults.RearTarget);
            DrawTextField("Rear Distance", nameof(buffer.RearSuspensionDistance), ref buffer.RearSuspensionDistance, defaults.RearSuspensionDistance);
        }

        private void DrawWheels(VehicleRuntimeTunerFieldBuffer buffer, VehicleRuntimeTunerDefaultValues defaults)
        {
            GUILayout.Space(4f);
            DrawSectionLabel("[Wheels]");
            DrawTextField("Front Radius", nameof(buffer.FrontRadius), ref buffer.FrontRadius, defaults.FrontRadius);
            DrawTextField("Rear Radius", nameof(buffer.RearRadius), ref buffer.RearRadius, defaults.RearRadius);
            DrawTextField("Front Width", nameof(buffer.FrontWidth), ref buffer.FrontWidth, defaults.FrontWidth);
            DrawTextField("Rear Width", nameof(buffer.RearWidth), ref buffer.RearWidth, defaults.RearWidth);
        }

        private void DrawTextField(string label, string controlName, ref string value, string defaultValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(150f));
            GUI.SetNextControlName(TextFieldControlPrefix + controlName);
            value = GUILayout.TextField(value ?? string.Empty, textFieldStyle, GUILayout.Width(180f));
            GUILayout.Label("Default", labelStyle, GUILayout.Width(50f));
            GUILayout.Label(string.IsNullOrWhiteSpace(defaultValue) ? "-" : defaultValue, labelStyle, GUILayout.Width(110f));
            GUILayout.EndHorizontal();
        }

        private void DrawSectionLabel(string label)
        {
            GUILayout.Label(label, labelStyle);
        }

        private void DrawTabBar(VehicleRuntimeTunerState state)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tuning", buttonStyle))
                state.SelectedTabIndex = 0;
            if (GUILayout.Button("Layout", buttonStyle))
                state.SelectedTabIndex = 1;
            GUILayout.EndHorizontal();
        }

        private void DrawLayout(VehicleRuntimeTunerLayoutBuffer buffer, VehicleRuntimeTunerLayoutDefaults defaults)
        {
            GUILayout.Space(4f);
            DrawSectionLabel("[Wheel Visuals]");
            DrawVector3Fields("Front Left Wheel", nameof(buffer.FrontLeftWheelX), ref buffer.FrontLeftWheelX, ref buffer.FrontLeftWheelY, ref buffer.FrontLeftWheelZ, defaults.FrontLeftWheelX, defaults.FrontLeftWheelY, defaults.FrontLeftWheelZ);
            DrawVector3Fields("Front Right Wheel", nameof(buffer.FrontRightWheelX), ref buffer.FrontRightWheelX, ref buffer.FrontRightWheelY, ref buffer.FrontRightWheelZ, defaults.FrontRightWheelX, defaults.FrontRightWheelY, defaults.FrontRightWheelZ);
            DrawVector3Fields("Rear Left Wheel", nameof(buffer.RearLeftWheelX), ref buffer.RearLeftWheelX, ref buffer.RearLeftWheelY, ref buffer.RearLeftWheelZ, defaults.RearLeftWheelX, defaults.RearLeftWheelY, defaults.RearLeftWheelZ);
            DrawVector3Fields("Rear Right Wheel", nameof(buffer.RearRightWheelX), ref buffer.RearRightWheelX, ref buffer.RearRightWheelY, ref buffer.RearRightWheelZ, defaults.RearRightWheelX, defaults.RearRightWheelY, defaults.RearRightWheelZ);

            DrawSectionLabel("[Wheel Controllers]");
            DrawVector3Fields("Front Left Ctrl", nameof(buffer.FrontLeftControllerX), ref buffer.FrontLeftControllerX, ref buffer.FrontLeftControllerY, ref buffer.FrontLeftControllerZ, defaults.FrontLeftControllerX, defaults.FrontLeftControllerY, defaults.FrontLeftControllerZ);
            DrawVector3Fields("Front Right Ctrl", nameof(buffer.FrontRightControllerX), ref buffer.FrontRightControllerX, ref buffer.FrontRightControllerY, ref buffer.FrontRightControllerZ, defaults.FrontRightControllerX, defaults.FrontRightControllerY, defaults.FrontRightControllerZ);
            DrawVector3Fields("Rear Left Ctrl", nameof(buffer.RearLeftControllerX), ref buffer.RearLeftControllerX, ref buffer.RearLeftControllerY, ref buffer.RearLeftControllerZ, defaults.RearLeftControllerX, defaults.RearLeftControllerY, defaults.RearLeftControllerZ);
            DrawVector3Fields("Rear Right Ctrl", nameof(buffer.RearRightControllerX), ref buffer.RearRightControllerX, ref buffer.RearRightControllerY, ref buffer.RearRightControllerZ, defaults.RearRightControllerX, defaults.RearRightControllerY, defaults.RearRightControllerZ);

            DrawSectionLabel("[Body]");
            DrawVector3Fields("Body Collider", nameof(buffer.BodyColliderX), ref buffer.BodyColliderX, ref buffer.BodyColliderY, ref buffer.BodyColliderZ, defaults.BodyColliderX, defaults.BodyColliderY, defaults.BodyColliderZ);
            DrawVector3Fields("CarHolder Body", nameof(buffer.BodyX), ref buffer.BodyX, ref buffer.BodyY, ref buffer.BodyZ, defaults.BodyX, defaults.BodyY, defaults.BodyZ);
            DrawVector3Fields("CarHolder Paint", nameof(buffer.PaintX), ref buffer.PaintX, ref buffer.PaintY, ref buffer.PaintZ, defaults.PaintX, defaults.PaintY, defaults.PaintZ);
        }

        private void DrawVector3Fields(
            string label,
            string controlPrefix,
            ref string xValue,
            ref string yValue,
            ref string zValue,
            string defaultX,
            string defaultY,
            string defaultZ)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(150f));
            GUI.SetNextControlName(TextFieldControlPrefix + controlPrefix + "_X");
            xValue = GUILayout.TextField(xValue ?? string.Empty, textFieldStyle, GUILayout.Width(55f));
            GUI.SetNextControlName(TextFieldControlPrefix + controlPrefix + "_Y");
            yValue = GUILayout.TextField(yValue ?? string.Empty, textFieldStyle, GUILayout.Width(55f));
            GUI.SetNextControlName(TextFieldControlPrefix + controlPrefix + "_Z");
            zValue = GUILayout.TextField(zValue ?? string.Empty, textFieldStyle, GUILayout.Width(55f));
            GUILayout.Label("Default", labelStyle, GUILayout.Width(50f));
            GUILayout.Label($"{FormatDefault(defaultX)}, {FormatDefault(defaultY)}, {FormatDefault(defaultZ)}", labelStyle, GUILayout.Width(180f));
            GUILayout.EndHorizontal();
        }

        private static string FormatDefault(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private void EnsureLayoutAndStyles()
        {
            if (!positionInitialized)
            {
                windowRect.x = 20f;
                windowRect.y = Mathf.Max(20f, (Screen.height - windowRect.height) * 0.5f);
                positionInitialized = true;
            }

            if (blackTexture == null)
                blackTexture = CreateTexture(new Color(0f, 0f, 0f, 1f));
            if (greyTexture == null)
                greyTexture = CreateTexture(new Color(0.28f, 0.28f, 0.28f, 1f));

            if (windowStyle == null)
            {
                windowStyle = new GUIStyle(GUI.skin.window);
                windowStyle.normal.background = blackTexture;
                windowStyle.onNormal.background = blackTexture;
                windowStyle.normal.textColor = Color.white;
                windowStyle.padding = new RectOffset(10, 10, 24, 10);
            }

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.normal.textColor = Color.white;
            }

            if (textFieldStyle == null)
            {
                textFieldStyle = new GUIStyle(GUI.skin.textField);
                textFieldStyle.normal.background = greyTexture;
                textFieldStyle.focused.background = greyTexture;
                textFieldStyle.hover.background = greyTexture;
                textFieldStyle.active.background = greyTexture;
                textFieldStyle.normal.textColor = Color.white;
                textFieldStyle.focused.textColor = Color.white;
                textFieldStyle.active.textColor = Color.white;
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.normal.textColor = Color.white;
            }
        }

        private static Texture2D CreateTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void ConsumeGameplayInputEvent()
        {
            var currentEvent = Event.current;
            if (currentEvent == null)
                return;

            switch (currentEvent.type)
            {
                case EventType.KeyDown:
                case EventType.KeyUp:
                case EventType.ScrollWheel:
                    currentEvent.Use();
                    break;
            }
        }

        private void ConsumeScrollWheelIfMouseOverWindow()
        {
            var currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.ScrollWheel)
                return;

            if (windowRect.Contains(currentEvent.mousePosition))
                currentEvent.Use();
        }

        private static void HandleOverlayHotkeys(
            System.Action onApply,
            System.Action onSave,
            System.Action onLoad,
            System.Action onDump)
        {
            var currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
                return;

            switch (currentEvent.keyCode)
            {
                case KeyCode.F9:
                    onApply();
                    currentEvent.Use();
                    break;
                case KeyCode.F8:
                    onDump();
                    currentEvent.Use();
                    break;
                case KeyCode.F7:
                    onSave();
                    currentEvent.Use();
                    break;
                case KeyCode.F6:
                    onLoad();
                    currentEvent.Use();
                    break;
            }
        }
    }
}
