#nullable enable
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime
    {
        private HDAdditionalCameraData? mapFogCameraData;
        private bool savedMapCustomRenderingSettings;
        private bool savedMapAtmosphericScattering;
        private bool savedMapVolumetrics;
        private bool savedMapVolumetricReprojection;
        private bool savedMapAtmosphericScatteringOverride;
        private bool savedMapVolumetricsOverride;
        private bool savedMapVolumetricReprojectionOverride;
        private bool hasSavedMapFogCameraState;
        private int lastMissingMapFogCameraId;

        private void UpdateCityMapFogSuppression(bool cityMapOpen)
        {
            if (settings == null || !settings.DisableCityMapFog || !cityMapOpen)
            {
                RestoreCityMapFogState();
                return;
            }

            var renderCamera = activeMapRenderCamera ?? GetLiveMainCamera();
            if (renderCamera == null)
            {
                RestoreCityMapFogState();
                return;
            }

            var cameraData = renderCamera.GetComponent<HDAdditionalCameraData>();
            if (cameraData == null)
            {
                RestoreCityMapFogState();
                var cameraId = renderCamera.GetInstanceID();
                if (cameraId != lastMissingMapFogCameraId)
                {
                    lastMissingMapFogCameraId = cameraId;
                    context?.Logger.Warn($"CameraTools: cannot disable city-map fog because camera '{renderCamera.name}' has no HDRP camera data.");
                }
                return;
            }

            lastMissingMapFogCameraId = 0;
            if (mapFogCameraData != cameraData)
            {
                RestoreCityMapFogState();
                CaptureCityMapFogState(cameraData);
            }

            ApplyCityMapFogState(cameraData);
        }

        private void CaptureCityMapFogState(HDAdditionalCameraData cameraData)
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
            context?.Logger.Info($"CameraTools: city-map fog disabled for camera '{cameraData.name}'.");
        }

        private static void ApplyCityMapFogState(HDAdditionalCameraData cameraData)
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
            if (!hasSavedMapFogCameraState)
                return;

            var restoredCameraName = "destroyed-camera";
            if (mapFogCameraData != null)
            {
                restoredCameraName = mapFogCameraData.name;
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
            context?.Logger.Info($"CameraTools: city-map fog state restored for camera '{restoredCameraName}'.");
        }
    }
}
