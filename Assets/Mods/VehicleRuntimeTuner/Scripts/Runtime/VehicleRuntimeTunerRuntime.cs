#nullable enable
using System;
using BAModAPI;
using VehicleRuntimeTuner.Applying;
using VehicleRuntimeTuner.Discovery;
using VehicleRuntimeTuner.Profiles;
using VehicleRuntimeTuner.UI;
using VehicleRuntimeTuner.Utils;
using VehicleRuntimeTuner.Vehicle;
using UnityEngine;

namespace VehicleRuntimeTuner.Runtime
{
    public sealed class VehicleRuntimeTunerRuntime : MonoBehaviour
    {
        private readonly VehicleRuntimeTunerState state = new VehicleRuntimeTunerState();
        private readonly VehicleRuntimeTunerHotkeys hotkeys = new VehicleRuntimeTunerHotkeys();
        private readonly ActiveVehicleResolver activeVehicleResolver = new ActiveVehicleResolver();
        private readonly VehicleRuntimeDiscovery vehicleRuntimeDiscovery = new VehicleRuntimeDiscovery();
        private readonly VehicleRuntimeDumpWriter dumpWriter = new VehicleRuntimeDumpWriter();
        private readonly VehicleRuntimeTunerProfileStore profileStore = new VehicleRuntimeTunerProfileStore();
        private readonly VehicleRuntimeTunerExportWriter exportWriter = new VehicleRuntimeTunerExportWriter();
        private readonly VehicleTuningApplier tuningApplier = new VehicleTuningApplier();
        private readonly VehicleDebugActions debugActions = new VehicleDebugActions();
        private readonly VehicleRuntimeTunerOverlay overlay = new VehicleRuntimeTunerOverlay();
        private readonly VehicleRuntimeTunerLogger logger = new VehicleRuntimeTunerLogger();

        public void Initialize(ModContext modContext)
        {
            logger.Initialize(modContext);
        }

        private void Update()
        {
            HandleHotkeys();

            if (state.OverlayVisible)
                state.OverlayMouseOverWindow = overlay.IsMouseOverWindow();

            if (state.OverlayVisible && (state.OverlayTextFieldFocused || state.OverlayMouseOverWindow))
                Input.ResetInputAxes();

            if (state.OverlayVisible)
                RefreshActiveVehicle(false);
        }

        private void OnGUI()
        {
            if (!state.OverlayVisible)
                return;

            try
            {
                state.OverlayTextFieldFocused = overlay.Draw(
                    state,
                    state.ActiveVehicle,
                    ApplyCurrentProfile,
                    SaveCurrentProfile,
                    LoadCurrentProfile,
                    DumpActiveVehicle,
                    ExportCurrentProfile,
                    () => RefreshActiveVehicle(true),
                    RespawnTestVehicle,
                    TeleportCurrentVehicleToGround,
                    ResetCurrentVehicleVelocity);
            }
            catch (Exception ex)
            {
                logger.Error("OnGUI failed: " + ex);
            }
        }

        private void HandleHotkeys()
        {
            if (hotkeys.ToggleOverlayPressed())
            {
                state.OverlayVisible = !state.OverlayVisible;
                state.OverlayMouseOverWindow = false;
                RefreshActiveVehicle(true);
                state.StatusMessage.Show(state.OverlayVisible ? "Overlay opened." : "Overlay closed.");
            }

            if (state.OverlayTextFieldFocused)
                return;

            if (hotkeys.ApplyPressed())
                ApplyCurrentProfile();

            if (hotkeys.DumpPressed())
                DumpActiveVehicle();

            if (hotkeys.SavePressed())
                SaveCurrentProfile();

            if (hotkeys.LoadPressed())
                LoadCurrentProfile();
        }

        private void RefreshActiveVehicle(bool forceRefresh)
        {
            var previousVehicleInstanceId = state.ActiveVehicle?.VehicleInstanceId ?? string.Empty;
            state.ActiveVehicle = activeVehicleResolver.Resolve(forceRefresh);

            if (state.ActiveVehicle != null &&
                (forceRefresh ||
                 !string.Equals(state.DefaultValues.VehicleInstanceId, state.ActiveVehicle.VehicleInstanceId, StringComparison.Ordinal) ||
                 !string.Equals(previousVehicleInstanceId, state.ActiveVehicle.VehicleInstanceId, StringComparison.Ordinal)))
            {
                state.DefaultValues.Capture(state.ActiveVehicle);
                state.LayoutDefaults.Capture(state.ActiveVehicle);
                state.LayoutBuffer.SyncFromVehicle(state.ActiveVehicle);
            }

            if (state.ActiveVehicle?.VehicleTypeName != null && !string.IsNullOrWhiteSpace(state.ActiveVehicle.VehicleTypeName))
            {
                state.CurrentProfile.vehicleTypeName = state.ActiveVehicle.VehicleTypeName;
                if (string.IsNullOrWhiteSpace(state.CurrentProfile.profileName))
                    state.CurrentProfile.profileName = state.ActiveVehicle.VehicleTypeName;
            }
        }

        private void ApplyCurrentProfile()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            state.FieldBuffer.FillEmptyFieldsFromDefaults(state.DefaultValues);
            state.CurrentProfile = state.FieldBuffer.ToProfile(state.CurrentProfile);
            if (string.IsNullOrWhiteSpace(state.CurrentProfile.vehicleTypeName))
                state.CurrentProfile.vehicleTypeName = state.ActiveVehicle.VehicleTypeName;

            tuningApplier.Apply(state.ActiveVehicle, state.CurrentProfile);
            state.LayoutBuffer.FillEmptyFieldsFromDefaults(state.LayoutDefaults);
            state.LayoutBuffer.Apply(state.ActiveVehicle);
            ShowStatus("Applied current profile.");
        }

        private void SaveCurrentProfile()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            state.CurrentProfile = state.FieldBuffer.ToProfile(state.CurrentProfile);
            if (string.IsNullOrWhiteSpace(state.CurrentProfile.vehicleTypeName))
                state.CurrentProfile.vehicleTypeName = state.ActiveVehicle.VehicleTypeName;
            if (string.IsNullOrWhiteSpace(state.CurrentProfile.profileName))
                state.CurrentProfile.profileName = state.ActiveVehicle.VehicleTypeName;

            var path = profileStore.Save(state.CurrentProfile);
            ShowStatus($"Saved profile: {path}");
        }

        private void LoadCurrentProfile()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            var loadedProfile = profileStore.Load(state.ActiveVehicle.VehicleTypeName);
            if (loadedProfile == null)
            {
                ShowStatus("No saved profile found.");
                return;
            }

            state.CurrentProfile = loadedProfile;
            state.FieldBuffer.SyncFromProfile(loadedProfile);
            ShowStatus("Loaded profile.");
        }

        private void DumpActiveVehicle()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            var snapshots = vehicleRuntimeDiscovery.Capture(state.ActiveVehicle);
            var path = dumpWriter.WriteDump(state.ActiveVehicle, snapshots);
            ShowStatus($"Runtime dump written: {path}");
        }

        private void ExportCurrentProfile()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle != null && string.IsNullOrWhiteSpace(state.CurrentProfile.vehicleTypeName))
                state.CurrentProfile.vehicleTypeName = state.ActiveVehicle.VehicleTypeName;

            state.CurrentProfile = state.FieldBuffer.ToProfile(state.CurrentProfile);
            var path = exportWriter.Write(state.CurrentProfile);
            ShowStatus($"Export written: {path}");
        }

        private void RespawnTestVehicle()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            state.FieldBuffer.FillEmptyFieldsFromDefaults(state.DefaultValues);
            state.CurrentProfile = state.FieldBuffer.ToProfile(state.CurrentProfile);
            debugActions.TryRespawnTestVehicle(state.ActiveVehicle, state.CurrentProfile, tuningApplier, out var message);
            ShowStatus(message);
        }

        private void TeleportCurrentVehicleToGround()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            debugActions.TryTeleportCurrentVehicleToGround(state.ActiveVehicle, out var message);
            ShowStatus(message);
        }

        private void ResetCurrentVehicleVelocity()
        {
            RefreshActiveVehicle(false);
            if (state.ActiveVehicle == null)
            {
                ShowStatus("No active vehicle.");
                return;
            }

            debugActions.TryResetRigidbodyVelocity(state.ActiveVehicle, out var message);
            ShowStatus(message);
        }

        private void ShowStatus(string message)
        {
            state.StatusMessage.Show(message);
            logger.Info(message);
        }

    }
}
