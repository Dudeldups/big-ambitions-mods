#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleRuntimeTuner.Vehicle
{
    public enum WheelGroup
    {
        Unknown,
        Front,
        Rear
    }

    public static class VehicleWheelClassifier
    {
        public static WheelGroup Classify(Transform? transform)
        {
            if (transform == null)
                return WheelGroup.Unknown;

            var name = transform.name ?? string.Empty;
            if (ContainsAny(name, "front", "fl", "fr"))
                return WheelGroup.Front;
            if (ContainsAny(name, "rear", "rl", "rr", "back"))
                return WheelGroup.Rear;

            return transform.localPosition.z >= 0f ? WheelGroup.Front : WheelGroup.Rear;
        }

        public static void SplitWheelColliders(
            IReadOnlyList<WheelCollider> wheelColliders,
            out List<WheelCollider> front,
            out List<WheelCollider> rear)
        {
            front = new List<WheelCollider>();
            rear = new List<WheelCollider>();

            foreach (var wheelCollider in wheelColliders)
            {
                if (wheelCollider == null)
                    continue;

                switch (Classify(wheelCollider.transform))
                {
                    case WheelGroup.Front:
                        front.Add(wheelCollider);
                        break;
                    case WheelGroup.Rear:
                        rear.Add(wheelCollider);
                        break;
                }
            }
        }

        public static void SplitWheelControllers(
            IEnumerable<MonoBehaviour> wheelControllers,
            out List<MonoBehaviour> front,
            out List<MonoBehaviour> rear)
        {
            front = new List<MonoBehaviour>();
            rear = new List<MonoBehaviour>();

            foreach (var wheelController in wheelControllers)
            {
                if (wheelController == null)
                    continue;

                switch (Classify(wheelController.transform))
                {
                    case WheelGroup.Front:
                        front.Add(wheelController);
                        break;
                    case WheelGroup.Rear:
                        rear.Add(wheelController);
                        break;
                }
            }
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
