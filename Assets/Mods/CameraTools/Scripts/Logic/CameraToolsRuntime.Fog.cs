#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime
    {
        private readonly List<MapFogVolumeState> mapFogVolumeStates = new List<MapFogVolumeState>();
        private HDAdditionalCameraData? mapFogCameraData;
        private bool savedMapCustomRenderingSettings;
        private bool savedMapAtmosphericScattering;
        private bool savedMapVolumetrics;
        private bool savedMapVolumetricReprojection;
        private bool savedMapAtmosphericScatteringOverride;
        private bool savedMapVolumetricsOverride;
        private bool savedMapVolumetricReprojectionOverride;
        private bool hasSavedMapFogCameraState;
        private bool hasCapturedMapFogVolumes;
        private int lastMissingMapFogCameraId;

        private void UpdateCityMapFogSuppression(bool cityMapOpen)
        {
            if (settings == null || !settings.DisableCityMapFog || !cityMapOpen)
            {
                RestoreCityMapFogState();
                return;
            }

            if (!hasCapturedMapFogVolumes)
                CaptureCityMapFogVolumes();
            ApplyCityMapFogVolumes();

            var renderCamera = activeMapRenderCamera ?? GetLiveMainCamera();
            if (renderCamera == null)
                return;

            var cameraData = renderCamera.GetComponent<HDAdditionalCameraData>();
            if (cameraData == null)
            {
                var cameraId = renderCamera.GetInstanceID();
                if (cameraId != lastMissingMapFogCameraId)
                {
                    lastMissingMapFogCameraId = cameraId;
                    context?.Logger.Warn($"CameraTools: map camera '{renderCamera.name}' has no HDRP camera data; volume fog suppression remains active.");
                }
                return;
            }

            lastMissingMapFogCameraId = 0;
            if (mapFogCameraData != cameraData)
            {
                RestoreCityMapFogCameraState();
                CaptureCityMapFogCameraState(cameraData);
            }

            ApplyCityMapFogCameraState(cameraData);
        }

        private void CaptureCityMapFogVolumes()
        {
            hasCapturedMapFogVolumes = true;
            mapFogVolumeStates.Clear();
            var seenFogComponents = new HashSet<int>();
            var activeVolumeCount = 0;

            foreach (var volume in Resources.FindObjectsOfTypeAll<Volume>())
            {
                if (volume == null || !volume.isActiveAndEnabled || !volume.gameObject.activeInHierarchy)
                    continue;

                activeVolumeCount++;
                var profile = volume.profile;
                if (profile == null || !profile.TryGet<Fog>(out var fog) || fog == null)
                    continue;

                var fogId = fog.GetInstanceID();
                if (!seenFogComponents.Add(fogId))
                    continue;

                mapFogVolumeStates.Add(new MapFogVolumeState(
                    fog,
                    fog.enabled.value,
                    fog.enabled.overrideState));
            }

            context?.Logger.Info(
                $"CameraTools: city-map fog volume scan completed; activeVolumes={activeVolumeCount}, fogComponents={mapFogVolumeStates.Count}.");
            if (mapFogVolumeStates.Count == 0)
                context?.Logger.Warn("CameraTools: no active HDRP fog volume was found while opening the city map.");
        }

        private void ApplyCityMapFogVolumes()
        {
            foreach (var state in mapFogVolumeStates)
            {
                if (state.Fog == null)
                    continue;

                state.Fog.enabled.overrideState = true;
                state.Fog.enabled.value = false;
            }
        }

        private void CaptureCityMapFogCameraState(HDAdditionalCameraData cameraData)
        {
            mapFogCameraData = cameraData;
            savedMapCustomRenderingSettings = cameraData.customRenderingSettings;
            ref var frameSettings = ref cameraData.renderingPathCustomFrameSettings;
            savedMapAtmosphericScattering = frameSettings.IsEnabled(FrameSettingsField.AtmosphericScattering);
            savedMapVolumetrics = frameSettings.IsEnabled(FrameSettingsField.Volumetrics);
            savedMapVolumetricReprojection = frameSettings.IsEnabled(FrameSettingsField.ReprojectionForVolumetrics);

            var overrideMask = cameraData.renderingPathCustomFrameSettingsOverrideMask;
            savedMapAtmosphericScatteringOverride = overrideMask.mask[(uint)FrameSettingsField.AtmosphericScattering];
            savedMapVolumetricsOverride = overrideMask.mask[(uint)FrameSettingsField.Volumetrics];
            savedMapVolumetricReprojectionOverride = overrideMask.mask[(uint)FrameSettingsField.ReprojectionForVolumetrics];
            hasSavedMapFogCameraState = true;
            context?.Logger.Info($"CameraTools: city-map fog rendering disabled for camera '{cameraData.name}'.");
        }

        private static void ApplyCityMapFogCameraState(HDAdditionalCameraData cameraData)
        {
            cameraData.customRenderingSettings = true;

            ref var frameSettings = ref cameraData.renderingPathCustomFrameSettings;
            frameSettings.SetEnabled(FrameSettingsField.AtmosphericScattering, false);
            frameSettings.SetEnabled(FrameSettingsField.Volumetrics, false);
            frameSettings.SetEnabled(FrameSettingsField.ReprojectionForVolumetrics, false);

            var overrideMask = cameraData.renderingPathCustomFrameSettingsOverrideMask;
            overrideMask.mask[(uint)FrameSettingsField.AtmosphericScattering] = true;
            overrideMask.mask[(uint)FrameSettingsField.Volumetrics] = true;
            overrideMask.mask[(uint)FrameSettingsField.ReprojectionForVolumetrics] = true;
            cameraData.renderingPathCustomFrameSettingsOverrideMask = overrideMask;
        }

        private void RestoreCityMapFogState()
        {
            RestoreCityMapFogCameraState();

            var restoredVolumeCount = 0;
            foreach (var state in mapFogVolumeStates)
            {
                if (state.Fog == null)
                    continue;

                state.Fog.enabled.value = state.EnabledValue;
                state.Fog.enabled.overrideState = state.EnabledOverrideState;
                restoredVolumeCount++;
            }

            mapFogVolumeStates.Clear();
            if (hasCapturedMapFogVolumes)
                context?.Logger.Info($"CameraTools: city-map fog restored; fogComponents={restoredVolumeCount}.");
            hasCapturedMapFogVolumes = false;
        }

        private void RestoreCityMapFogCameraState()
        {
            if (!hasSavedMapFogCameraState)
                return;

            if (mapFogCameraData != null)
            {
                mapFogCameraData.customRenderingSettings = savedMapCustomRenderingSettings;

                ref var frameSettings = ref mapFogCameraData.renderingPathCustomFrameSettings;
                frameSettings.SetEnabled(FrameSettingsField.AtmosphericScattering, savedMapAtmosphericScattering);
                frameSettings.SetEnabled(FrameSettingsField.Volumetrics, savedMapVolumetrics);
                frameSettings.SetEnabled(FrameSettingsField.ReprojectionForVolumetrics, savedMapVolumetricReprojection);

                var overrideMask = mapFogCameraData.renderingPathCustomFrameSettingsOverrideMask;
                overrideMask.mask[(uint)FrameSettingsField.AtmosphericScattering] = savedMapAtmosphericScatteringOverride;
                overrideMask.mask[(uint)FrameSettingsField.Volumetrics] = savedMapVolumetricsOverride;
                overrideMask.mask[(uint)FrameSettingsField.ReprojectionForVolumetrics] = savedMapVolumetricReprojectionOverride;
                mapFogCameraData.renderingPathCustomFrameSettingsOverrideMask = overrideMask;
            }

            mapFogCameraData = null;
            hasSavedMapFogCameraState = false;
        }

        private sealed class MapFogVolumeState
        {
            public MapFogVolumeState(Fog fog, bool enabledValue, bool enabledOverrideState)
            {
                Fog = fog;
                EnabledValue = enabledValue;
                EnabledOverrideState = enabledOverrideState;
            }

            public Fog Fog { get; }
            public bool EnabledValue { get; }
            public bool EnabledOverrideState { get; }
        }
    }
}
