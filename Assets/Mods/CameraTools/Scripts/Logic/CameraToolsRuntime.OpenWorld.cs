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
        private void ConfigureGameplayController()
        {
            if (settings == null || gameplayController == null || !settings.EnableGameplayTweaks)
                return;

            var controllerId = gameplayController.GetInstanceID();
            if (lastAppliedGameplayMaxZoom == settings.GameplayMaxZoom &&
                controllerId == lastConfiguredGameplayControllerId)
                return;

            var bounds = GetVector2Member(gameplayController, "minMaxDistance");
            bounds.x = Mathf.Min(bounds.x, GameplayMinimumZoom);
            bounds.y = Mathf.Max(bounds.y, settings.GameplayMaxZoom);
            SetMemberValue(gameplayController, "minMaxDistance", bounds);
            SetMemberValue(gameplayController, "blockCameraZoom", false);
            lastAppliedGameplayMaxZoom = settings.GameplayMaxZoom;
            lastConfiguredGameplayControllerId = controllerId;

            if (!hasManualGameplayPitch)
                manualGameplayPitch = Mathf.Clamp(settings.GameplayDefaultPitch, settings.GameplayMinPitch, settings.GameplayMaxPitch);

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

            ApplyGameplayOffset(hasManualGameplayPitch ? manualGameplayPitch : settings.GameplayDefaultPitch);
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
