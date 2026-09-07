#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime
    {
        private const float MapFogOverridePriority = 10000f;

        private GameObject? mapFogOverrideObject;
        private Volume? mapFogOverrideVolume;
        private VolumeProfile? mapFogOverrideProfile;
        private bool mapFogOverrideActive;
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
            var cameraData = renderCamera == null ? null : renderCamera.GetComponent<HDAdditionalCameraData>();
            EnsureCityMapFogOverride(cameraData);
            if (mapFogOverrideVolume != null)
            {
                mapFogOverrideVolume.enabled = true;
                mapFogOverrideVolume.weight = 1f;
            }

            if (!mapFogOverrideActive)
            {
                mapFogOverrideActive = true;
                context?.Logger.Info(
                    $"CameraTools: city-map fog override activated; priority={MapFogOverridePriority:0}, layer={(mapFogOverrideObject == null ? -1 : mapFogOverrideObject.layer)}.");
            }

            if (renderCamera == null)
                return;

            if (cameraData == null)
            {
                var cameraId = renderCamera.GetInstanceID();
                if (cameraId != lastMissingMapFogCameraId)
                {
                    lastMissingMapFogCameraId = cameraId;
                    context?.Logger.Warn($"CameraTools: map camera '{renderCamera.name}' has no HDRP camera data; the global fog override remains active.");
                }
                return;
            }

            lastMissingMapFogCameraId = 0;
            ConfigureCityMapFogOverrideLayer(cameraData);
            if (mapFogCameraData != cameraData)
            {
                RestoreCityMapFogCameraState();
                CaptureCityMapFogCameraState(cameraData);
            }

            ApplyCityMapFogCameraState(cameraData);
        }

        private void EnsureCityMapFogOverride(HDAdditionalCameraData? cameraData)
        {
            if (mapFogOverrideObject != null && mapFogOverrideVolume != null && mapFogOverrideProfile != null)
                return;

            mapFogOverrideObject = new GameObject("CameraTools City Map Fog Override")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            mapFogOverrideObject.transform.SetParent(transform, false);

            mapFogOverrideProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            mapFogOverrideProfile.name = "CameraTools City Map Fog Override";
            mapFogOverrideProfile.hideFlags = HideFlags.HideAndDontSave;
            var fog = mapFogOverrideProfile.Add<Fog>(true);
            fog.enabled.Override(false);
            fog.enableVolumetricFog.Override(false);

            mapFogOverrideVolume = mapFogOverrideObject.AddComponent<Volume>();
            mapFogOverrideVolume.isGlobal = true;
            mapFogOverrideVolume.priority = MapFogOverridePriority;
            mapFogOverrideVolume.blendDistance = 0f;
            mapFogOverrideVolume.weight = 1f;
            mapFogOverrideVolume.sharedProfile = mapFogOverrideProfile;
            ConfigureCityMapFogOverrideLayer(cameraData);
        }

        private void ConfigureCityMapFogOverrideLayer(HDAdditionalCameraData? cameraData)
        {
            if (cameraData == null || mapFogOverrideObject == null)
                return;

            var volumeLayerMask = cameraData.volumeLayerMask.value;
            if ((volumeLayerMask & (1 << mapFogOverrideObject.layer)) != 0)
                return;

            for (var layer = 0; layer < 32; layer++)
            {
                if ((volumeLayerMask & (1 << layer)) == 0)
                    continue;

                mapFogOverrideObject.layer = layer;
                return;
            }

            context?.Logger.Warn("CameraTools: map camera volume layer mask contains no layers; the fog override cannot be evaluated.");
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
            context?.Logger.Info($"CameraTools: city-map atmospheric rendering disabled for camera '{cameraData.name}'.");
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
            if (mapFogOverrideVolume != null)
                mapFogOverrideVolume.enabled = false;

            if (!mapFogOverrideActive)
                return;

            mapFogOverrideActive = false;
            context?.Logger.Info("CameraTools: city-map fog override deactivated and normal fog restored.");
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
    }
}
