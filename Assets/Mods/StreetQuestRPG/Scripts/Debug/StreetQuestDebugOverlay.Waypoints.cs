using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace StreetQuestRPG
{
    internal sealed partial class StreetQuestDebugOverlay
    {
        private static readonly List<Vector3> WaypointBuilderPoints = new();

        private static void DrawWaypointsTab()
        {
            GUILayout.Label("Waypoint Builder", _headerStyle);
            GUILayout.Label("Add stores the current player position. Copy JSON creates a paste-ready array for character config.", _textStyle);
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add", _buttonStyle, GUILayout.Width(110f)))
                AddCurrentWaypoint();

            if (GUILayout.Button("Copy JSON", _buttonStyle, GUILayout.Width(110f)))
                CopyWaypointJsonToClipboard();

            if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Width(110f)))
                ResetWaypointBuilder();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Count: {WaypointBuilderPoints.Count}", _textStyle);
            GUILayout.Space(4f);

            _waypointScroll = GUILayout.BeginScrollView(_waypointScroll, GUILayout.ExpandHeight(true));
            if (WaypointBuilderPoints.Count == 0)
            {
                GUILayout.Label("No waypoints yet.", _textStyle);
            }
            else
            {
                for (var index = 0; index < WaypointBuilderPoints.Count; index++)
                {
                    GUILayout.Label($"{index + 1}. {FormatVector3(WaypointBuilderPoints[index])}", _textStyle);
                }
            }

            GUILayout.EndScrollView();
        }

        private static void AddCurrentWaypoint()
        {
            var playerPosition = StreetQuestShared.GetPlayerWorldPosition();
            WaypointBuilderPoints.Add(playerPosition);
            StreetQuestShared.NotifyInfo(
                $"Added waypoint {WaypointBuilderPoints.Count}: {FormatVector3(playerPosition)}",
                "streetquest:debug_waypoint_added",
                2.5f);
        }

        private static void CopyWaypointJsonToClipboard()
        {
            var json = BuildWaypointJson();
            GUIUtility.systemCopyBuffer = json;
            StreetQuestShared.NotifyInfo(
                $"Copied {WaypointBuilderPoints.Count} waypoint(s) as JSON.",
                "streetquest:debug_waypoints_copied",
                2.5f);
        }

        private static void ResetWaypointBuilder()
        {
            WaypointBuilderPoints.Clear();
            StreetQuestShared.NotifyInfo("Waypoint list cleared.", "streetquest:debug_waypoints_reset", 2.5f);
        }

        private static string BuildWaypointJson()
        {
            if (WaypointBuilderPoints.Count == 0)
                return "[]";

            var builder = new StringBuilder();
            builder.AppendLine("[");
            for (var index = 0; index < WaypointBuilderPoints.Count; index++)
            {
                var point = WaypointBuilderPoints[index];
                builder.Append("  { \"x\": ");
                builder.Append(point.x.ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append(", \"y\": ");
                builder.Append(point.y.ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append(", \"z\": ");
                builder.Append(point.z.ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append(" }");
                if (index < WaypointBuilderPoints.Count - 1)
                    builder.Append(',');

                builder.AppendLine();
            }

            builder.Append(']');
            return builder.ToString();
        }
    }
}
