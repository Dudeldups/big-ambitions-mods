using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Helpers;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestDebugBuildingLogger : MonoBehaviour
    {
        private const float PollIntervalSeconds = 0.75f;
        private const float NearestBuildingMaxDistance = 60f;
        private const float NearbyColliderRadius = 8f;

        private static readonly string[] InterestingKeywords =
        {
            "active",
            "building",
            "address",
            "current",
            "entered",
            "entry",
            "exit",
            "inside",
            "owner",
            "occup",
            "tenant",
            "store",
            "shop",
            "business",
            "company",
            "customer",
            "interior",
            "indoor",
            "room",
            "unit",
            "apartment",
            "retail",
            "registration"
        };

        private float _nextPollAt;
        private string _lastSignature = string.Empty;

        private void Update()
        {
            if (!StreetQuestDebugSettings.Enabled ||
                !StreetQuestDebugSettings.VerboseBuildingContextLogging ||
                !IsInActiveGameSession())
                return;

            if (Time.unscaledTime < _nextPollAt)
                return;

            _nextPollAt = Time.unscaledTime + PollIntervalSeconds;
            TryLogPlayerBuildingContext();
        }

        private static bool IsInActiveGameSession()
        {
            return SaveGameManager.Current != null && PlayerHelper.PlayerController != null;
        }

        private void TryLogPlayerBuildingContext()
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
                return;

            var playerPosition = playerController.transform.position;
            var nearestRegistration = FindNearestBuildingRegistration(playerPosition, NearestBuildingMaxDistance, out var nearestDistance);
            var nearestAddressText = FormatAddress(nearestRegistration?.Address);
            var playerContextLines = CollectInterestingMembers(playerController, "player", 0);
            var playerComponentLines = CollectComponentContextLines(playerController.gameObject, "playerObject");
            var nearestRegistrationLines = CollectInterestingMembers(nearestRegistration, "nearestRegistration", 0);
            var nearestBuilding = nearestRegistration?.Address != null ? SafeGetBuilding(nearestRegistration.Address) : null;
            var nearestBuildingLines = CollectInterestingMembers(nearestBuilding, "nearestBuilding", 0);
            var nearbyColliderLines = CollectNearbyColliderLines(playerPosition);
            var nearbyTargetLines = CollectNearbyTargetContextLines(playerPosition);
            var playerHierarchyPath = GetHierarchyPath(playerController.transform);
            var activeScene = SceneManager.GetActiveScene();
            var gameManagerObject = GameObject.Find("GameManager");
            var gameManagerLines = CollectComponentContextLines(gameManagerObject, "gameManager");

            var signature = string.Join("|", new[]
            {
                playerController.GetType().FullName ?? "<null>",
                nearestAddressText,
                nearestRegistration?.GetType().FullName ?? "<null>",
                nearestDistance.ToString("F2"),
                activeScene.name ?? "<null>",
                playerHierarchyPath,
                string.Join(";", playerContextLines.Take(8)),
                string.Join(";", playerComponentLines.Take(8)),
                string.Join(";", gameManagerLines.Take(8)),
                string.Join(";", nearestRegistrationLines.Take(8)),
                string.Join(";", nearbyColliderLines.Take(8)),
                string.Join(";", nearbyTargetLines.Take(8))
            });

            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
                return;

            _lastSignature = signature;
            StreetQuestShared.LogDebug("=== PlayerBuildingContext start ===");
            StreetQuestShared.LogDebug($"PlayerBuildingContext playerPosition={FormatVector3(playerPosition)} playerType={playerController.GetType().FullName}");
            StreetQuestShared.LogDebug($"PlayerBuildingContext scene={activeScene.name} buildIndex={activeScene.buildIndex} playerPath={playerHierarchyPath}");
            StreetQuestShared.LogDebug($"PlayerBuildingContext nearestRegistrationType={(nearestRegistration == null ? "<null>" : nearestRegistration.GetType().FullName)} nearestDistance={nearestDistance:0.00} nearestAddress={nearestAddressText}");

            foreach (var line in playerContextLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            foreach (var line in playerComponentLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            foreach (var line in gameManagerLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            foreach (var line in nearestRegistrationLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            if (nearestBuilding != null)
            {
                StreetQuestShared.LogDebug($"PlayerBuildingContext nearestBuildingType={nearestBuilding.GetType().FullName}");
                foreach (var line in nearestBuildingLines)
                    StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");
            }

            foreach (var line in nearbyColliderLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            foreach (var line in nearbyTargetLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            StreetQuestShared.LogDebug("=== PlayerBuildingContext end ===");
        }

        private static object SafeGetBuilding(Address address)
        {
            if (address == null)
                return null;

            try
            {
                return BuildingHelper.GetBuilding(address);
            }
            catch
            {
                return null;
            }
        }

        private static BuildingRegistration FindNearestBuildingRegistration(
            Vector3 position,
            float maxDistance,
            out float nearestDistance)
        {
            nearestDistance = float.PositiveInfinity;
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null || saveGame.BuildingRegistrations.Count == 0)
                return null;

            var maxDistanceSquared = maxDistance * maxDistance;
            BuildingRegistration nearest = null;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null || !registration.HasValidAddress)
                    continue;

                if (!TryResolveWorldPosition(registration, out var registrationPosition))
                    continue;

                var distanceSquared = (registrationPosition - position).sqrMagnitude;
                if (distanceSquared > maxDistanceSquared || distanceSquared >= nearestDistance * nearestDistance)
                    continue;

                nearestDistance = Mathf.Sqrt(distanceSquared);
                nearest = registration;
            }

            return nearest;
        }

        private static bool TryResolveWorldPosition(object candidate, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (candidate == null)
                return false;

            if (candidate is Transform transform)
            {
                worldPosition = transform.position;
                return true;
            }

            if (candidate is Component component)
            {
                worldPosition = component.transform.position;
                return true;
            }

            if (candidate is GameObject gameObject)
            {
                worldPosition = gameObject.transform.position;
                return true;
            }

            foreach (var memberName in new[] { "position", "Position", "worldPosition", "WorldPosition" })
            {
                if (!TryReadMemberValue(candidate, memberName, out var value))
                    continue;

                if (value is Vector3 vector3)
                {
                    worldPosition = vector3;
                    return true;
                }

                if (value is Transform memberTransform)
                {
                    worldPosition = memberTransform.position;
                    return true;
                }

                if (value is Component memberComponent)
                {
                    worldPosition = memberComponent.transform.position;
                    return true;
                }
            }

            return false;
        }

        private static List<string> CollectInterestingMembers(object instance, string prefix, int depth)
        {
            var lines = new List<string>();
            if (instance == null || depth > 1)
                return lines;

            var type = instance.GetType();
            lines.Add($"{prefix}.type={type.FullName}");

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member =>
                    (member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property) &&
                    IsInterestingName(member.Name))
                .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .Take(40);

            foreach (var member in members)
            {
                if (!seenNames.Add(member.Name))
                    continue;

                if (!TryReadMemberValue(instance, member.Name, out var value))
                    continue;

                var formattedValue = FormatValue(value);
                lines.Add($"{prefix}.{member.Name}={formattedValue}");

                if (ShouldRecurseInto(value))
                    lines.AddRange(CollectInterestingMembers(value, $"{prefix}.{member.Name}", depth + 1));
            }

            return lines;
        }

        private static List<string> CollectNearbyColliderLines(Vector3 playerPosition)
        {
            var lines = new List<string>();
            var colliders = Physics.OverlapSphere(playerPosition, NearbyColliderRadius);
            var uniqueTransforms = new HashSet<Transform>();
            foreach (var collider in colliders
                         .Where(value => value != null && value.transform != null)
                         .OrderBy(value => (value.transform.position - playerPosition).sqrMagnitude))
            {
                if (!uniqueTransforms.Add(collider.transform))
                    continue;

                var path = GetHierarchyPath(collider.transform);
                var name = collider.name ?? "<unnamed>";
                var distance = Vector3.Distance(playerPosition, collider.transform.position);
                var layerName = LayerMask.LayerToName(collider.gameObject.layer);
                var componentSummary = string.Join(",",
                    collider.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().Name)
                        .Take(6));

                lines.Add(
                    $"nearbyCollider name={name} distance={distance:0.00} layer={layerName} path={path} components={componentSummary}");

                if (lines.Count >= 20)
                    break;
            }

            if (lines.Count == 0)
                lines.Add("nearbyCollider <none>");

            return lines;
        }

        private static List<string> CollectNearbyTargetContextLines(Vector3 playerPosition)
        {
            var lines = new List<string>();
            var colliders = Physics.OverlapSphere(playerPosition, NearbyColliderRadius);
            var visitedObjects = new HashSet<GameObject>();
            var visitedLinkedObjects = new HashSet<object>();

            foreach (var collider in colliders.Where(value => value != null))
            {
                var targetTransform = FindInterestingContextTransform(collider.transform);
                if (targetTransform == null || !visitedObjects.Add(targetTransform.gameObject))
                    continue;

                lines.Add($"nearbyTarget path={GetHierarchyPath(targetTransform)}");
                lines.AddRange(CollectComponentContextLines(targetTransform.gameObject, $"nearbyTarget[{targetTransform.name}]"));
                lines.AddRange(CollectLinkedContextLines(targetTransform.gameObject, $"nearbyTarget[{targetTransform.name}]", visitedLinkedObjects));

                if (lines.Count >= 40)
                    break;
            }

            return lines;
        }

        private static Transform FindInterestingContextTransform(Transform start)
        {
            var current = start;
            while (current != null)
            {
                var name = current.name ?? string.Empty;
                if (IsInterestingName(name))
                    return current;

                current = current.parent;
            }

            return null;
        }
        private static List<string> CollectComponentContextLines(GameObject gameObject, string prefix)
        {
            var lines = new List<string>();
            if (gameObject == null)
                return lines;

            lines.Add($"{prefix}.path={GetHierarchyPath(gameObject.transform)}");
            foreach (var component in gameObject.GetComponents<Component>().Where(value => value != null))
            {
                var componentPrefix = $"{prefix}.{component.GetType().Name}";
                lines.Add($"{componentPrefix}.type={component.GetType().FullName}");
                lines.AddRange(CollectInterestingMembers(component, componentPrefix, 0).Skip(1));
            }

            return lines;
        }

        private static List<string> CollectLinkedContextLines(
            GameObject gameObject,
            string prefix,
            HashSet<object> visitedLinkedObjects)
        {
            var lines = new List<string>();
            if (gameObject == null)
                return lines;

            foreach (var component in gameObject.GetComponents<Component>().Where(value => value != null))
            {
                var componentType = component.GetType();
                var members = componentType
                    .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(member =>
                        (member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property) &&
                        IsInterestingName(member.Name))
                    .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var member in members)
                {
                    if (!TryReadMemberValue(component, member.Name, out var value) || value == null)
                        continue;

                    if (!ShouldInspectLinkedObject(value) || !visitedLinkedObjects.Add(value))
                        continue;

                    var linkedPrefix = $"{prefix}.{componentType.Name}.{member.Name}";
                    lines.AddRange(CollectInterestingMembers(value, linkedPrefix, 0));

                    if (value is Component linkedComponent)
                        lines.AddRange(CollectComponentContextLines(linkedComponent.gameObject, $"{linkedPrefix}.gameObject"));
                    else if (value is GameObject linkedGameObject)
                        lines.AddRange(CollectComponentContextLines(linkedGameObject, $"{linkedPrefix}.gameObject"));
                }
            }

            return lines;
        }

        private static bool ShouldRecurseInto(object value)
        {
            if (value == null)
                return false;

            var type = value.GetType();
            return !type.IsPrimitive &&
                   type != typeof(string) &&
                   type != typeof(Vector3) &&
                   type != typeof(Vector2) &&
                   type != typeof(Vector2Int) &&
                   type != typeof(Vector3Int) &&
                   type != typeof(Quaternion) &&
                   !typeof(UnityEngine.Object).IsAssignableFrom(type) &&
                   !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
        }

        private static bool ShouldInspectLinkedObject(object value)
        {
            if (value == null)
                return false;

            if (value is Component || value is GameObject)
                return true;

            var type = value.GetType();
            return !type.IsPrimitive &&
                   type != typeof(string) &&
                   !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
        }

        private static bool IsInterestingName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return InterestingKeywords.Any(keyword =>
                name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryReadMemberValue(object instance, string memberName, out object value)
        {
            value = null;
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var type = instance.GetType();
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    value = field.GetValue(instance);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                return false;

            try
            {
                value = property.GetValue(instance, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatValue(object value)
        {
            if (value == null)
                return "<null>";

            return value switch
            {
                string text => string.IsNullOrWhiteSpace(text) ? "<empty>" : text,
                Vector3 vector3 => FormatVector3(vector3),
                Vector2 vector2 => $"{vector2.x:0.00}, {vector2.y:0.00}",
                Quaternion quaternion => $"{quaternion.eulerAngles.x:0.00}, {quaternion.eulerAngles.y:0.00}, {quaternion.eulerAngles.z:0.00}",
                Address address => FormatAddress(address),
                Enum enumValue => enumValue.ToString(),
                _ when value is UnityEngine.Object unityObject => $"{unityObject.name} ({value.GetType().FullName})",
                _ => value.ToString()
            };
        }

        private static string FormatAddress(Address address)
        {
            if (address == null)
                return "<null>";

            try
            {
                return address.ToString();
            }
            catch
            {
                return $"{address.GetType().FullName}";
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"{value.x:0.00}, {value.y:0.00}, {value.z:0.00}";
        }
    }
}
