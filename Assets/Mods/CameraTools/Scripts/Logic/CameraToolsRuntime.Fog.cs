#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime
    {
        private const float CityFogProfileScanIntervalSeconds = 2f;

        private readonly List<CityFogState> cityFogStates = new List<CityFogState>();
        private readonly HashSet<int> trackedCityFogIds = new HashSet<int>();
        private bool cityFogSuppressionApplied;
        private bool hasWarnedAboutMissingCityFogProfiles;
        private float nextCityFogProfileScanTime;

        private void UpdateCityFogSuppression()
        {
            if (settings == null || !settings.DisableCityFog)
            {
                RestoreCityFogState();
                return;
            }

            if (IsPlayerInFogPreservingInterior())
            {
                RestoreCityFogEnabledValues(clearTrackedProfiles: false);
                return;
            }

            if (!cityFogSuppressionApplied || Time.unscaledTime >= nextCityFogProfileScanTime)
            {
                CaptureNewCityFogProfiles();
                nextCityFogProfileScanTime = Time.unscaledTime + CityFogProfileScanIntervalSeconds;
            }

            foreach (var state in cityFogStates)
                state.DisableFog();

            if (cityFogSuppressionApplied)
                return;

            cityFogSuppressionApplied = true;
            context?.Logger.Info($"CameraTools: outdoor city fog disabled; trackedProfiles={cityFogStates.Count}.");
        }

        private void CaptureNewCityFogProfiles()
        {
            var addedProfiles = 0;
            foreach (var volume in Resources.FindObjectsOfTypeAll<Volume>())
            {
                if (volume == null)
                    continue;

                var profile = volume.profile;
                if (profile == null || !profile.TryGet<Fog>(out var fog) || fog == null)
                    continue;

                var fogId = fog.GetInstanceID();
                if (!trackedCityFogIds.Add(fogId))
                    continue;

                cityFogStates.Add(new CityFogState(fog));
                addedProfiles++;
            }

            if (addedProfiles > 0)
            {
                hasWarnedAboutMissingCityFogProfiles = false;
                context?.Logger.Info(
                    $"CameraTools: captured {addedProfiles} new HDRP fog profile(s); trackedProfiles={cityFogStates.Count}.");
                return;
            }

            if (cityFogStates.Count != 0 || hasWarnedAboutMissingCityFogProfiles)
                return;

            hasWarnedAboutMissingCityFogProfiles = true;
            context?.Logger.Warn("CameraTools: no HDRP fog profiles were found while disabling outdoor city fog.");
        }

        private static bool IsPlayerInFogPreservingInterior()
        {
            try
            {
                if (BuildingManager.IsInsideBuilding)
                    return true;
            }
            catch
            {
            }

            try
            {
                return Parking.UndergroundParking.UndergroundParkingManager.IsInsideParking;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreCityFogState()
        {
            RestoreCityFogEnabledValues(clearTrackedProfiles: true);
            hasWarnedAboutMissingCityFogProfiles = false;
            nextCityFogProfileScanTime = 0f;
        }

        private void RestoreCityFogEnabledValues(bool clearTrackedProfiles)
        {
            if (cityFogSuppressionApplied)
            {
                foreach (var state in cityFogStates)
                    state.RestoreFog();

                context?.Logger.Info(
                    $"CameraTools: outdoor city fog restored; trackedProfiles={cityFogStates.Count}, clearTrackedProfiles={clearTrackedProfiles}.");
            }

            cityFogSuppressionApplied = false;
            if (!clearTrackedProfiles)
                return;

            cityFogStates.Clear();
            trackedCityFogIds.Clear();
        }

        private sealed class CityFogState
        {
            private readonly Fog fog;
            private readonly bool enabledValue;
            private readonly bool enabledOverrideState;

            public CityFogState(Fog fog)
            {
                this.fog = fog;
                enabledValue = fog.enabled.value;
                enabledOverrideState = fog.enabled.overrideState;
            }

            public void DisableFog()
            {
                if (fog == null)
                    return;

                fog.enabled.overrideState = true;
                fog.enabled.value = false;
            }

            public void RestoreFog()
            {
                if (fog == null)
                    return;

                fog.enabled.value = enabledValue;
                fog.enabled.overrideState = enabledOverrideState;
            }
        }
    }
}
