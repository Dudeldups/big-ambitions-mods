#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Streets.Pedestrians;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FreelancePhotographer
{
    internal static class PhotographyShotEvaluator
    {
        private const float ViewportMargin = 0.04f;

        internal static PhotographyShotResult Evaluate(
            PhotographyContractInstance contract,
            PhotographyEquipmentSnapshot equipment)
        {
            if (equipment.CameraTier <= 0)
                return Invalid("freelancephotographer:shot_camera_required");
            if (equipment.CameraTier < contract.requiredTier)
                return Invalid("freelancephotographer:shot_better_camera_required");
            if (!equipment.HasAccessory(contract.requiredAccessory))
                return Invalid(AccessoryFailureKey(contract.requiredAccessory));

            var camera = Camera.main;
            if (camera == null || !camera.isActiveAndEnabled)
                return Invalid("freelancephotographer:shot_camera_unavailable");

            switch (contract.category)
            {
                case PhotographyCategory.Location:
                case PhotographyCategory.Business:
                    return EvaluateBuilding(camera, contract, equipment);
                case PhotographyCategory.Vehicle:
                    return EvaluateVehicle(camera, contract, equipment);
                case PhotographyCategory.Street:
                    return EvaluateStreet(camera, contract, equipment);
                default:
                    return Invalid("freelancephotographer:shot_subject_unavailable");
            }
        }

        private static PhotographyShotResult EvaluateBuilding(
            Camera camera,
            PhotographyContractInstance contract,
            PhotographyEquipmentSnapshot equipment)
        {
            var controller = Object.FindObjectsOfType<CityBuildingController>()
                .FirstOrDefault(value => value != null &&
                                         value.buildingRegistration != null &&
                                         string.Equals(value.buildingRegistration.StreetName, contract.targetStreet,
                                             StringComparison.OrdinalIgnoreCase) &&
                                         value.buildingRegistration.StreetNumber == contract.targetNumber);
            if (controller == null)
                return Invalid("freelancephotographer:shot_subject_unavailable");

            var point = GetTargetPoint(controller);
            return EvaluateSingle(camera, controller.transform, point, contract, equipment, contract.targetDisplayName);
        }

        private static PhotographyShotResult EvaluateVehicle(
            Camera camera,
            PhotographyContractInstance contract,
            PhotographyEquipmentSnapshot equipment)
        {
            var activeVehicleId = SaveGameManager.Current?.ActiveVehicleId;
            var candidates = Object.FindObjectsOfType<VehicleController>()
                .Where(value => value != null && value.gameObject.activeInHierarchy &&
                                value.vehicleInstance != null &&
                                !string.Equals(value.vehicleInstance.id, activeVehicleId, StringComparison.Ordinal))
                .Select(value => new Subject(value.transform, GetTargetPoint(value)))
                .ToList();
            if (candidates.Count == 0)
                return Invalid("freelancephotographer:shot_subject_unavailable");

            var best = FindBestFramed(camera, candidates, contract.maximumDistance);
            if (best == null)
                return Invalid("freelancephotographer:shot_subject_not_in_frame");

            return EvaluateSingle(camera, best.Root, best.Point, contract, equipment,
                "freelancephotographer:target_any_vehicle");
        }

        private static PhotographyShotResult EvaluateStreet(
            Camera camera,
            PhotographyContractInstance contract,
            PhotographyEquipmentSnapshot equipment)
        {
            var visible = new List<VisibleSubject>();
            foreach (var pedestrian in Object.FindObjectsOfType<Pedestrian>())
            {
                if (pedestrian == null || !pedestrian.gameObject.activeInHierarchy)
                    continue;

                var point = GetTargetPoint(pedestrian);
                var viewport = camera.WorldToViewportPoint(point);
                var distance = Vector3.Distance(camera.transform.position, point);
                if (!IsInsideViewport(viewport) || distance < contract.minimumDistance || distance > contract.maximumDistance)
                    continue;
                if (!HasLineOfSight(camera.transform.position, pedestrian.transform, point))
                    continue;

                visible.Add(new VisibleSubject(viewport, distance));
            }

            if (visible.Count < contract.requiredSubjectCount)
            {
                var invalid = Invalid("freelancephotographer:shot_not_enough_pedestrians");
                invalid.ActualSubjectCount = visible.Count;
                invalid.RequiredSubjectCount = contract.requiredSubjectCount;
                return invalid;
            }

            var viewportCenter = new Vector3(
                visible.Average(value => value.Viewport.x),
                visible.Average(value => value.Viewport.y),
                1f);
            var averageDistance = visible.Average(value => value.Distance);
            var framing = CalculateFraming(viewportCenter);
            var distanceScore = CalculateDistance(averageDistance, contract);
            var bonus = Mathf.Clamp((visible.Count - contract.requiredSubjectCount) * 2, 0, 5);
            if (Vector2.Distance(new Vector2(viewportCenter.x, viewportCenter.y), new Vector2(0.5f, 0.5f)) < 0.12f)
                bonus += 5;

            return Valid(contract, equipment, framing, distanceScore, 20, 15, 10, Mathf.Clamp(bonus, 0, 10),
                "freelancephotographer:target_three_pedestrians");
        }

        private static PhotographyShotResult EvaluateSingle(
            Camera camera,
            Transform root,
            Vector3 point,
            PhotographyContractInstance contract,
            PhotographyEquipmentSnapshot equipment,
            string subjectName)
        {
            var viewport = camera.WorldToViewportPoint(point);
            if (!IsInsideViewport(viewport))
                return Invalid("freelancephotographer:shot_subject_not_in_frame");

            var distance = Vector3.Distance(camera.transform.position, point);
            if (distance < contract.minimumDistance)
                return Invalid("freelancephotographer:shot_too_close");
            if (distance > contract.maximumDistance)
                return Invalid("freelancephotographer:shot_too_far");
            if (!HasLineOfSight(camera.transform.position, root, point))
                return Invalid("freelancephotographer:shot_subject_obscured");

            var framing = CalculateFraming(viewport);
            var distanceScore = CalculateDistance(distance, contract);
            var bonus = Vector2.Distance(new Vector2(viewport.x, viewport.y), new Vector2(0.5f, 0.5f)) < 0.12f ? 5 : 0;
            if (contract.category == PhotographyCategory.Vehicle && equipment.HasLens)
                bonus += 5;
            else if ((contract.category == PhotographyCategory.Location || contract.category == PhotographyCategory.Business) &&
                     equipment.HasTripod)
                bonus += 5;
            else if (equipment.HasFlash && IsLowLight())
                bonus += 5;

            return Valid(contract, equipment, framing, distanceScore, 20, 15, 10, Mathf.Clamp(bonus, 0, 10), subjectName);
        }

        private static PhotographyShotResult Valid(
            PhotographyContractInstance contract,
            PhotographyEquipmentSnapshot equipment,
            int framing,
            int distance,
            int visibility,
            int equipmentScore,
            int timing,
            int bonus,
            string subjectName)
        {
            var raw = framing + distance + visibility + equipmentScore + timing + bonus;
            return new PhotographyShotResult
            {
                IsValid = true,
                SubjectName = subjectName,
                Quality = Mathf.Clamp(Math.Min(raw, equipment.QualityCap), 0, 100),
                Framing = framing,
                Distance = distance,
                Visibility = visibility,
                Equipment = equipmentScore,
                Timing = timing,
                Bonus = bonus
            };
        }

        private static PhotographyShotResult Invalid(string key)
        {
            return new PhotographyShotResult { FailureKey = key };
        }

        private static string AccessoryFailureKey(PhotographyAccessory accessory)
        {
            switch (accessory)
            {
                case PhotographyAccessory.Lens:
                    return "freelancephotographer:shot_lens_required";
                case PhotographyAccessory.Tripod:
                    return "freelancephotographer:shot_tripod_required";
                case PhotographyAccessory.Flash:
                    return "freelancephotographer:shot_flash_required";
                default:
                    return "freelancephotographer:shot_equipment_required";
            }
        }

        private static int CalculateFraming(Vector3 viewport)
        {
            var distanceFromCenter = Vector2.Distance(
                new Vector2(viewport.x, viewport.y),
                new Vector2(0.5f, 0.5f));
            return Mathf.RoundToInt(25f * (1f - Mathf.Clamp01(distanceFromCenter / 0.68f)));
        }

        private static int CalculateDistance(float distance, PhotographyContractInstance contract)
        {
            if (distance >= contract.idealDistanceMinimum && distance <= contract.idealDistanceMaximum)
                return 20;
            if (distance < contract.idealDistanceMinimum)
            {
                var range = Mathf.Max(0.01f, contract.idealDistanceMinimum - contract.minimumDistance);
                return Mathf.RoundToInt(Mathf.Lerp(8f, 20f, (distance - contract.minimumDistance) / range));
            }

            var farRange = Mathf.Max(0.01f, contract.maximumDistance - contract.idealDistanceMaximum);
            return Mathf.RoundToInt(Mathf.Lerp(20f, 5f, (distance - contract.idealDistanceMaximum) / farRange));
        }

        private static bool IsInsideViewport(Vector3 viewport)
        {
            return viewport.z > 0f &&
                   viewport.x >= ViewportMargin && viewport.x <= 1f - ViewportMargin &&
                   viewport.y >= ViewportMargin && viewport.y <= 1f - ViewportMargin;
        }

        private static Subject? FindBestFramed(Camera camera, IEnumerable<Subject> subjects, float maximumDistance)
        {
            Subject? best = null;
            var bestScore = float.MaxValue;
            foreach (var subject in subjects)
            {
                var viewport = camera.WorldToViewportPoint(subject.Point);
                if (!IsInsideViewport(viewport))
                    continue;
                if (Vector3.Distance(camera.transform.position, subject.Point) > maximumDistance)
                    continue;

                var score = Vector2.Distance(new Vector2(viewport.x, viewport.y), new Vector2(0.5f, 0.5f));
                if (score >= bestScore)
                    continue;

                best = subject;
                bestScore = score;
            }

            return best;
        }

        private static Vector3 GetTargetPoint(Component component)
        {
            var renderers = component.GetComponentsInChildren<Renderer>();
            var found = false;
            var bounds = new Bounds(component.transform.position, Vector3.zero);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found ? bounds.center : component.transform.position + Vector3.up * 1.25f;
        }

        private static bool HasLineOfSight(Vector3 origin, Transform root, Vector3 target)
        {
            var direction = target - origin;
            var distance = direction.magnitude;
            if (distance <= 0.01f)
                return true;
            if (!Physics.Raycast(origin, direction / distance, out var hit, distance + 0.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;

            return hit.transform == root || hit.transform.IsChildOf(root) || root.IsChildOf(hit.transform);
        }

        private static bool IsLowLight()
        {
            var save = SaveGameManager.Current;
            return save != null && (save.Hour >= 18 || save.Hour < 6);
        }

        private sealed class Subject
        {
            internal readonly Transform Root;
            internal readonly Vector3 Point;

            internal Subject(Transform root, Vector3 point)
            {
                Root = root;
                Point = point;
            }
        }

        private readonly struct VisibleSubject
        {
            internal readonly Vector3 Viewport;
            internal readonly float Distance;

            internal VisibleSubject(Vector3 viewport, float distance)
            {
                Viewport = viewport;
                Distance = distance;
            }
        }
    }
}
