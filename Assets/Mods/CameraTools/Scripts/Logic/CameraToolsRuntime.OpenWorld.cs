#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI.Notification;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private static readonly string[] GameplayDistanceMemberNames =
        {
            "distance",
            "_distance",
            "currentDistance",
            "_currentDistance",
            "camDistance",
            "_camDistance",
            "cameraDistance",
            "_cameraDistance"
        };

        private void ConfigureGameplayController()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            var controllerId = gameplayController.GetInstanceID();
            var currentBounds = GetVector2Member(gameplayController, "minMaxDistance");
            var desiredBounds = currentBounds;
            desiredBounds.x = Mathf.Min(desiredBounds.x, GameplayMinimumZoom);
            desiredBounds.y = settings.GameplayMaxZoom;

            var boundsDiffer =
                Mathf.Abs(currentBounds.x - desiredBounds.x) > 0.01f ||
                Mathf.Abs(currentBounds.y - desiredBounds.y) > 0.01f;
            var needsReconfigure =
                boundsDiffer ||
                lastAppliedGameplayMaxZoom != settings.GameplayMaxZoom ||
                controllerId != lastConfiguredGameplayControllerId;

            if (needsReconfigure)
            {
                SetMemberValue(gameplayController, "minMaxDistance", desiredBounds);
                SetMemberValue(gameplayController, "blockCameraZoom", false);
                lastAppliedGameplayMaxZoom = settings.GameplayMaxZoom;
                lastConfiguredGameplayControllerId = controllerId;
            }

            if (!hasManualGameplayPitch)
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, settings.GameplayMinPitch, settings.GameplayMaxPitch);

            ClampGameplayDistance(desiredBounds);
            ApplyGameplayOffset(manualGameplayPitch);
        }

        private void ApplyGameplayTweaks()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            ConfigureGameplayController();

            var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);

            if (!hasShownGameplayPitchHint && context != null)
            {
                context.Logger.Info("CameraTools: hold right mouse and move up or down to tilt the gameplay camera.");
                hasShownGameplayPitchHint = true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                isTrackingRightMousePitch = true;
                lastRightMouseY = Input.mousePosition.y;
            }

            if (Input.GetMouseButton(1) && isTrackingRightMousePitch)
            {
                var currentMouseY = Input.mousePosition.y;
                var deltaY = currentMouseY - lastRightMouseY;
                lastRightMouseY = currentMouseY;

                if (Mathf.Abs(deltaY) > Mathf.Epsilon)
                {
                    manualGameplayPitch = Mathf.Clamp(manualGameplayPitch - deltaY * PitchStepPerMousePixel, minPitch, maxPitch);
                    hasManualGameplayPitch = true;
                }
            }

            if (Input.GetMouseButtonUp(1))
                isTrackingRightMousePitch = false;

            if (Input.GetKeyDown(KeyCode.Home))
            {
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, minPitch, maxPitch);
                hasManualGameplayPitch = true;
            }

            var bounds = GetVector2Member(gameplayController, "minMaxDistance");
            ClampGameplayDistance(bounds);
            ApplyGameplayOffset(hasManualGameplayPitch ? manualGameplayPitch : settings.GameplayDefaultPitch);
        }

        private void ClampGameplayDistance(Vector2 bounds)
        {
            if (gameplayController == null)
                return;

            var foundAny = false;
            for (var i = 0; i < GameplayDistanceMemberNames.Length; i++)
            {
                var memberName = GameplayDistanceMemberNames[i];
                if (!TryGetFloatMember(gameplayController, memberName, out var currentDistance))
                    continue;

                foundAny = true;
                var clampedDistance = Mathf.Clamp(currentDistance, bounds.x, bounds.y);
                if (Mathf.Abs(clampedDistance - currentDistance) <= 0.01f)
                    continue;

                SetMemberValue(gameplayController, memberName, clampedDistance);
            }

            if (!foundAny && TryGetCameraDistanceToFollowTarget(GetLiveVirtualCameraComponent(), Camera.main, out var actualDistance))
            {
                var clampedActualDistance = Mathf.Clamp(actualDistance, bounds.x, bounds.y);
                if (Mathf.Abs(clampedActualDistance - actualDistance) > 0.01f)
                {
                    SetMemberValue(gameplayController, "distance", clampedActualDistance);
                    SetMemberValue(gameplayController, "_currentDistance", clampedActualDistance);
                }
            }
        }

        private void ApplyGameplayOffset(float pitchDegrees)
        {
            if (gameplayController == null)
                return;

            var radians = Mathf.Deg2Rad * Mathf.Clamp(pitchDegrees, 1f, 89f);
            var offset = new Vector3(0f, Mathf.Sin(radians), -Mathf.Cos(radians));
            if (!lastAppliedGameplayOffset.HasValue || lastAppliedGameplayOffset.Value != offset)
            {
                SetMemberValue(gameplayController, "offset", offset);
                lastAppliedGameplayOffset = offset;
            }
        }

        private float GetCurrentGameplayPitchForLogging()
        {
            if (settings == null)
                return manualGameplayPitch;

            var minPitch = Mathf.Min(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var maxPitch = Mathf.Max(settings.GameplayMinPitch, settings.GameplayMaxPitch);
            var currentPitch = hasManualGameplayPitch ? manualGameplayPitch : settings.GameplayDefaultPitch;
            return Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }

        private void UpdateIndoorWallsVisibility(bool cityMapOpen, bool gameplayActive)
        {
            if (cityMapOpen || !gameplayActive || !IsIndoorGameplayCamera(GetLiveVirtualCameraComponent()))
            {
                RestoreForcedIndoorWallsVisibility();
                return;
            }

            var currentWallMode = GetCurrentWallsVisibilityName();
            if (string.IsNullOrEmpty(currentWallMode))
                return;

            var currentPitch = GetCurrentGameplayPitchForLogging();
            var belowThreshold = currentPitch <= IndoorWallsPartlyHiddenPitchThreshold;

            if (belowThreshold)
            {
                if (string.Equals(currentWallMode, "AllVisible", StringComparison.Ordinal))
                {
                    hasForcedIndoorWallsPartlyHidden = false;
                    return;
                }

                if (hasForcedIndoorWallsPartlyHidden && string.Equals(currentWallMode, "PartlyHidden", StringComparison.Ordinal))
                    return;

                if (string.Equals(currentWallMode, "AllHidden", StringComparison.Ordinal) || hasForcedIndoorWallsPartlyHidden)
                {
                    if (TrySetWallsVisibility("PartlyHidden"))
                        hasForcedIndoorWallsPartlyHidden = true;
                }

                return;
            }

            RestoreForcedIndoorWallsVisibility();
        }

        private void RestoreForcedIndoorWallsVisibility()
        {
            if (!hasForcedIndoorWallsPartlyHidden)
                return;

            var currentWallMode = GetCurrentWallsVisibilityName();
            if (string.Equals(currentWallMode, "PartlyHidden", StringComparison.Ordinal))
                TrySetWallsVisibility("AllHidden");

            hasForcedIndoorWallsPartlyHidden = false;
        }

        private static string GetCurrentWallsVisibilityName()
        {
            var helperType = ResolveWallsVisibilityHelperType();
            if (helperType == null)
                return "helper-missing";

            var field = GetCachedField(helperType, "currentWallsVisibility");
            if (field == null)
                return "field-missing";

            if (field.GetValue(null) is object enumValue)
                return enumValue.ToString() ?? "value-null-name";

            return "value-null";
        }

        private static bool TrySetWallsVisibility(string enumFieldName)
        {
            var helperType = ResolveWallsVisibilityHelperType();
            var enumType = ResolveWallsVisibilityEnumType();
            if (helperType == null || enumType == null)
                return false;

            var enumField = GetCachedField(enumType, enumFieldName);
            var enumValue = enumField?.GetValue(null);
            if (enumValue == null)
                return false;

            var toggleMethod = helperType.GetMethod(
                "ToggleWalls",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { enumType },
                null);
            if (toggleMethod == null)
                return false;

            try
            {
                toggleMethod.Invoke(null, new[] { enumValue });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Type? ResolveWallsVisibilityHelperType()
        {
            return wallsVisibilityHelperType ??= FindType("Buildings.Indoors.WallsVisibilityHelper");
        }

        private static Type? ResolveWallsVisibilityEnumType()
        {
            return wallsVisibilityType ??= FindType("BigAmbitions.InteriorDesigner.WallsVisibility");
        }

        private void HandleScenicViewHotkey()
        {
            if (settings == null || !Input.GetKeyDown(settings.ScenicViewHotkey))
                return;

            scenicViewEnabled = !scenicViewEnabled;
            if (scenicViewEnabled)
            {
                ApplyScenicView();
            }
            else
            {
                RestoreScenicView();
            }
        }

        private void RefreshScenicViewState()
        {
            if (!scenicViewEnabled)
            {
                if (scenicViewRendererStates.Length > 0)
                    RestoreScenicView();

                return;
            }

            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            if (scenicViewTargetRoot == null || scenicViewTargetRoot != playerController)
            {
                ApplyScenicView();
                return;
            }

            var currentRenderers = playerController.GetComponentsInChildren<Renderer>(true);
            if (currentRenderers.Length != scenicViewRendererStates.Length)
            {
                ApplyScenicView();
                return;
            }

            foreach (var state in scenicViewRendererStates)
            {
                if (state.Renderer != null && state.Renderer.enabled)
                    state.Renderer.enabled = false;
            }
        }

        private void ApplyScenicView()
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            RestoreScenicView();

            var renderers = playerController.GetComponentsInChildren<Renderer>(true);
            var states = new List<RendererState>(renderers.Length);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                states.Add(new RendererState(renderer, renderer.enabled));
                renderer.enabled = false;
            }

            scenicViewTargetRoot = playerController;
            scenicViewRendererStates = states.ToArray();
        }

        private void RestoreScenicView()
        {
            foreach (var state in scenicViewRendererStates)
            {
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }

            scenicViewRendererStates = Array.Empty<RendererState>();
            scenicViewTargetRoot = null;
        }

        private bool IsGameplayActive()
        {
            if (gameplayController != null && gameplayController.isActiveAndEnabled)
                return true;

            if (gameManagerController != null && gameManagerController.isActiveAndEnabled)
                return true;

            return false;
        }

    }
}
