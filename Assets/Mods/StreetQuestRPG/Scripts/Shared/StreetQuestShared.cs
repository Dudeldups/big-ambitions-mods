using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using CustomNPCAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const string QuestStateModDataKey = "streetquest:quest_state_v1";
        private const string SpawnedQuestGiverName = "StreetQuestRPG.OutdoorQuestGiver";
        private static readonly Vector3 FixedSpawnPosition = new(301.58f, 0.09f, -188.47f);
        private static readonly Vector3 FixedForward = new(0f, 0f, -1f);
        private static readonly Vector3 DefaultSpawnOffsetFromPlayer = new(0f, 0f, 4f);
        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly object LogSync = new();
        private static readonly string PreferredWorkspaceLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";
        private static string DebugLogDirectory;
        private static readonly Dictionary<string, GameObject> SpawnedCharacterRoots = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CustomNpcHandle> SpawnedCharacterHandles = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> SpawnedCharacterStateSignatures = new(StringComparer.OrdinalIgnoreCase);
        private static Type CachedNavigationBlockerType;
        private static Type CachedContactType;
        private static Type CachedThirdPersonCharacterType;
        private static Vector3? PreferredQuestGiverSpawnPosition;
        private static string DebugLogFilePath;
        private static string SnapshotLogFilePath;
        private static object CachedQuestStateOwner;
        private static StreetQuestQuestStateRecord CachedQuestStateRecord;
        private static bool RuntimeShutdownInProgress;

        public const string MackContactId = "streetquest:mack_contact";
        public const string MackNameKey = "streetquest:mack_name";

        internal static void SetRuntimeShutdownInProgress(bool value)
        {
            RuntimeShutdownInProgress = value;
        }

        internal static bool IsRuntimeShutdownInProgress()
        {
            return RuntimeShutdownInProgress;
        }
    }
}
