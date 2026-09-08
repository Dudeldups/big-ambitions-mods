#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

namespace MootorVehicle
{
    /// <summary>
    /// Handles cow impacts through Unity collision callbacks. There is no Update or FixedUpdate loop.
    /// </summary>
    internal sealed class MootorVehicleImpactController : MonoBehaviour
    {
        private const float StrongHorizontalDeltaVelocity = 3.5f;
        private const float MinimumHorizontalImpactSpeed = 4f;
        private const float CrashMooVolume = 0.65f;

        private VehicleController? vehicle;
        private PhysicsVehicle? physicsVehicle;
        private Rigidbody? vehicleBody;
        private ModContext? context;
        private Coroutine? forcedDismountCoroutine;
        private bool crashAudioConfigured;
        private bool crashAudioFailureLogged;

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
                crash.clips ??= new List<AudioClip>();
                crash.clips.Clear();
                crash.clips.Add(mooClip);
                crash.baseVolume = CrashMooVolume;
                mooClip.LoadAudioData();
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
            forcedDismountCoroutine = StartCoroutine(ForceDismountAfterCollision());
        }

        private IEnumerator ForceDismountAfterCollision()
        {
            // Let the physics step and native crash handlers finish before running the standard
            // vehicle-exit path. This preserves the game's normal player and held-item cleanup.
            yield return null;

            try
            {
                if (vehicle != null && vehicle.controlledByPlayer)
                    vehicle.ExitVehicle();
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle impact vehicle={vehicle?.GetInstanceID()} could not dismount rider: " +
                    exception.GetBaseException().Message);
            }
            finally
            {
                forcedDismountCoroutine = null;
            }
        }

        private void OnDisable()
        {
            if (forcedDismountCoroutine == null)
                return;

            StopCoroutine(forcedDismountCoroutine);
            forcedDismountCoroutine = null;
        }
    }
}
