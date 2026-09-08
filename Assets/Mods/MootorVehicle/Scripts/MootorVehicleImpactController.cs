#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace MootorVehicle
{
    /// <summary>
    /// Handles cow impacts through Unity collision callbacks. There is no Update or FixedUpdate loop.
    /// </summary>
    internal sealed class MootorVehicleImpactController : MonoBehaviour
    {
        private const float StrongHorizontalDeltaVelocity = 3f;
        private const float MinimumHorizontalImpactSpeed = 3.5f;
        private const float CrashMooVolume = 0.65f;
        private const string FaintAnimationName = "Faint";
        private const float LyingDuration = 1f;
        private const float GetUpDuration = 1.5f;
        private const NavigationBlocker CrashFallNavigationBlocker = (NavigationBlocker)1000;

        private VehicleController? vehicle;
        private PhysicsVehicle? physicsVehicle;
        private Rigidbody? vehicleBody;
        private ModContext? context;
        private Coroutine? forcedDismountCoroutine;
        private bool crashAudioConfigured;
        private bool crashAudioFailureLogged;
        private bool crashMooPlaybackFailureLogged;
        private AudioClip? crashMooClip;
        private AudioSource? crashMooSource;
        private PlayerController? fallenPlayer;
        private Animator? fallenPlayerAnimator;
        private float fallenPlayerAnimatorSpeed = 1f;
        private bool fallNavigationBlocked;

        internal void Initialize(VehicleController controller, ModContext? modContext)
        {
            vehicle = controller;
            physicsVehicle = controller.GetComponent<PhysicsVehicle>();
            vehicleBody = controller.GetComponent<Rigidbody>();
            context = modContext;
            ConfigureCrashMoo();
        }

        private void ConfigureCrashMoo()
        {
            if (crashAudioConfigured || physicsVehicle == null)
                return;

            try
            {
                var horn = physicsVehicle.soundManager.hornComponent;
                var crash = physicsVehicle.soundManager.crashComponent;
                if (horn?.clips == null || horn.clips.Count == 0 || horn.clips[0] == null)
                    throw new InvalidOperationException("bundled moo clip is unavailable");
                if (crash == null)
                    throw new InvalidOperationException("native crash sound component is unavailable");

                var mooClip = horn.clips[0];
                crashMooClip = mooClip;
                crash.clips ??= new List<AudioClip>();
                crash.clips.Clear();
                crash.clips.Add(mooClip);
                crash.baseVolume = CrashMooVolume;
                mooClip.LoadAudioData();

                var sourceObject = new GameObject("MootorVehicle_StrongImpactMoo");
                sourceObject.transform.SetParent(transform, false);
                crashMooSource = sourceObject.AddComponent<AudioSource>();
                crashMooSource.playOnAwake = false;
                crashMooSource.loop = false;
                crashMooSource.clip = mooClip;
                crashMooSource.volume = CrashMooVolume;
                crashMooSource.spatialBlend = 1f;
                crashMooSource.rolloffMode = AudioRolloffMode.Linear;
                crashMooSource.minDistance = 3f;
                crashMooSource.maxDistance = 35f;
                crashAudioConfigured = true;

                context?.Logger.Info(
                    $"Moo-tor Vehicle impact audio vehicle={vehicle?.GetInstanceID()} configured " +
                    $"clip='{mooClip.name}' volume={CrashMooVolume:F2}.");
            }
            catch (Exception exception)
            {
                if (crashAudioFailureLogged)
                    return;

                crashAudioFailureLogged = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact audio vehicle={vehicle?.GetInstanceID()} could not replace " +
                    $"the native crash sound: {exception.GetBaseException().Message}");
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || vehicle == null || vehicleBody == null ||
                !vehicle.controlledByPlayer || forcedDismountCoroutine != null)
            {
                return;
            }

            var horizontalImpulse = Vector3.ProjectOnPlane(collision.impulse, Vector3.up).magnitude;
            var horizontalDeltaVelocity = horizontalImpulse / Mathf.Max(vehicleBody.mass, 1f);
            var horizontalImpactSpeed = Vector3.ProjectOnPlane(collision.relativeVelocity, Vector3.up).magnitude;
            if (horizontalDeltaVelocity < StrongHorizontalDeltaVelocity ||
                horizontalImpactSpeed < MinimumHorizontalImpactSpeed)
            {
                return;
            }

            context?.Logger.Info(
                $"Moo-tor Vehicle strong impact vehicle={vehicle.GetInstanceID()} " +
                $"horizontalDeltaV={horizontalDeltaVelocity:F2}m/s " +
                $"relativeSpeed={horizontalImpactSpeed:F2}m/s; forcing rider dismount.");
            PlayStrongImpactMoo();
            forcedDismountCoroutine = StartCoroutine(ForceDismountAfterCollision());
        }

        private void PlayStrongImpactMoo()
        {
            try
            {
                if (crashMooSource == null || crashMooClip == null)
                    throw new InvalidOperationException("dedicated strong-impact moo source is unavailable");

                crashMooSource.Stop();
                crashMooSource.PlayOneShot(crashMooClip);
                context?.Logger.Info(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} played explicit " +
                    $"strong-impact moo clip='{crashMooClip.name}'.");
            }
            catch (Exception exception)
            {
                if (crashMooPlaybackFailureLogged)
                    return;

                crashMooPlaybackFailureLogged = true;
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not play " +
                    $"the strong-impact moo: {exception.GetBaseException().Message}");
            }
        }

        private IEnumerator ForceDismountAfterCollision()
        {
            // Let the physics step and native crash handlers finish before running the standard
            // vehicle-exit path. This preserves the game's normal player and held-item cleanup.
            yield return null;

            var riderExited = false;
            try
            {
                if (vehicle != null && vehicle.controlledByPlayer)
                {
                    var engine = physicsVehicle?.powertrain?.engine;
                    if (engine != null)
                    {
                        engine.StopEngine();
                        context?.Logger.Info(
                            $"Moo-tor Vehicle impact vehicle={vehicle.GetInstanceID()} reset drivetrain " +
                            "before forced exit.");
                    }

                    var transmission = physicsVehicle?.powertrain?.transmission;
                    if (transmission != null)
                    {
                        transmission.ShiftInto(0, true);
                        transmission.currentGearRatio = 0f;
                    }

                    vehicle.ExitVehicle();
                    riderExited = true;
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not dismount rider: " +
                    exception.GetBaseException().Message);
            }

            try
            {
                if (riderExited)
                {
                    yield return PlayPlayerFallAndRecovery();
                }
            }
            finally
            {
                forcedDismountCoroutine = null;
            }
        }

        private IEnumerator PlayPlayerFallAndRecovery()
        {
            fallenPlayer = PlayerHelper.PlayerController;
            var character = fallenPlayer?.Character;
            var appearance = character?.appearanceSetter;
            fallenPlayerAnimator = appearance == null
                ? null
                : typeof(AppearanceSetter).GetField(
                        "animator",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(appearance) as Animator;

            if (fallenPlayer == null || character == null || fallenPlayerAnimator == null ||
                fallenPlayerAnimator.runtimeAnimatorController == null)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} dismounted the rider, " +
                    "but the native player animator was unavailable for the fall animation.");
                ClearFallState(false);
                yield break;
            }

            AnimationClip? faintClip = null;
            foreach (var clip in fallenPlayerAnimator.runtimeAnimatorController.animationClips)
                if (clip != null && string.Equals(clip.name, FaintAnimationName, StringComparison.Ordinal))
                {
                    faintClip = clip;
                    break;
                }

            var faintLayer = -1;
            var faintState = 0;
            for (var layer = 0; layer < fallenPlayerAnimator.layerCount; layer++)
            {
                var fullPath = Animator.StringToHash(
                    $"{fallenPlayerAnimator.GetLayerName(layer)}.{FaintAnimationName}");
                if (fallenPlayerAnimator.HasState(layer, fullPath))
                {
                    faintLayer = layer;
                    faintState = fullPath;
                    break;
                }

                var shortName = Animator.StringToHash(FaintAnimationName);
                if (fallenPlayerAnimator.HasState(layer, shortName))
                {
                    faintLayer = layer;
                    faintState = shortName;
                    break;
                }
            }

            if (faintClip == null || faintClip.length <= 0f ||
                faintLayer < 0)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} dismounted the rider, " +
                    $"but native animation '{FaintAnimationName}' was unavailable.");
                ClearFallState(false);
                yield break;
            }

            try
            {
                fallenPlayer.RemoveGoal();
                fallenPlayer.SetNavigationBlocker(CrashFallNavigationBlocker);
                fallNavigationBlocked = true;
                fallenPlayerAnimatorSpeed = fallenPlayerAnimator.speed;
                fallenPlayerAnimator.speed = 0f;
                fallenPlayerAnimator.Play(faintState, faintLayer, 1f);
                fallenPlayerAnimator.Update(0f);

                context?.Logger.Info(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} positioned rider " +
                    $"prone with native clip='{faintClip.name}' layer={faintLayer} " +
                    $"length={faintClip.length:F2}s.");

                var elapsed = 0f;
                while (elapsed < LyingDuration && fallenPlayerAnimator != null)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                elapsed = 0f;
                while (elapsed < GetUpDuration && fallenPlayerAnimator != null)
                {
                    elapsed += Time.deltaTime;
                    var normalizedTime = 1f - Mathf.Clamp01(elapsed / GetUpDuration);
                    fallenPlayerAnimator.Play(faintState, faintLayer, normalizedTime);
                    yield return null;
                }

                context?.Logger.Info(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} completed rider get-up.");
            }
            finally
            {
                ClearFallState(true);
            }
        }

        private void ClearFallState(bool resetAnimation)
        {
            try
            {
                if (fallenPlayerAnimator != null)
                {
                    fallenPlayerAnimator.speed = fallenPlayerAnimatorSpeed;
                    if (resetAnimation && fallenPlayerAnimator.isActiveAndEnabled)
                    {
                        // Faint intentionally has no native exit transition. Rebinding after the
                        // reversed get-up returns the Base Actions layer to its normal default.
                        fallenPlayerAnimator.Rebind();
                        fallenPlayerAnimator.Update(0f);
                    }
                }

                if (fallenPlayer != null)
                {
                    if (resetAnimation)
                        fallenPlayer.ResetWalkingAnimation();
                    if (fallNavigationBlocked)
                        fallenPlayer.UnsetNavigationBlocker(CrashFallNavigationBlocker);
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not fully " +
                    $"restore the rider after a fall: {exception.GetBaseException().Message}");
            }
            finally
            {
                fallenPlayer = null;
                fallenPlayerAnimator = null;
                fallenPlayerAnimatorSpeed = 1f;
                fallNavigationBlocked = false;
            }
        }

        private void OnDisable()
        {
            if (forcedDismountCoroutine != null)
            {
                StopCoroutine(forcedDismountCoroutine);
                forcedDismountCoroutine = null;
            }

            ClearFallState(true);
        }
    }
}
