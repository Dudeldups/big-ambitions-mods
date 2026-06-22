using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Helpers;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestDebugBuildingLogger : MonoBehaviour
    {
        private const float PollIntervalSeconds = 0.75f;
        private const float NearestBuildingMaxDistance = 60f;

        private static readonly string[] InterestingKeywords =
        {
            "building",
            "address",
            "store",
            "shop",
            "business",
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
            if (!StreetQuestDebugSettings.Enabled || !IsInActiveGameSession())
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
            var nearestRegistrationLines = CollectInterestingMembers(nearestRegistration, "nearestRegistration", 0);
            var nearestBuilding = nearestRegistration?.Address != null ? SafeGetBuilding(nearestRegistration.Address) : null;
            var nearestBuildingLines = CollectInterestingMembers(nearestBuilding, "nearestBuilding", 0);

            var signature = string.Join("|", new[]
            {
                playerController.GetType().FullName ?? "<null>",
                nearestAddressText,
                nearestRegistration?.GetType().FullName ?? "<null>",
                nearestDistance.ToString("F2"),
                string.Join(";", playerContextLines.Take(8)),
                string.Join(";", nearestRegistrationLines.Take(8))
            });

            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
                return;

            _lastSignature = signature;
            StreetQuestShared.LogDebug("=== PlayerBuildingContext start ===");
            StreetQuestShared.LogDebug($"PlayerBuildingContext playerPosition={FormatVector3(playerPosition)} playerType={playerController.GetType().FullName}");
            StreetQuestShared.LogDebug($"PlayerBuildingContext nearestRegistrationType={(nearestRegistration == null ? "<null>" : nearestRegistration.GetType().FullName)} nearestDistance={nearestDistance:0.00} nearestAddress={nearestAddressText}");

            foreach (var line in playerContextLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            foreach (var line in nearestRegistrationLines)
                StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");

            if (nearestBuilding != null)
            {
                StreetQuestShared.LogDebug($"PlayerBuildingContext nearestBuildingType={nearestBuilding.GetType().FullName}");
                foreach (var line in nearestBuildingLines)
                    StreetQuestShared.LogDebug($"PlayerBuildingContext {line}");
            }

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

        private static string FormatVector3(Vector3 value)
        {
            return $"{value.x:0.00}, {value.y:0.00}, {value.z:0.00}";
        }
    }
}
