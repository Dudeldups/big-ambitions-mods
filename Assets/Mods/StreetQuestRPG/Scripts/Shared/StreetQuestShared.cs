using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
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
        private const int HomelessIntroStageInitial = 0;
        private const int HomelessIntroStageKnowsPast = 1;
        private const int HomelessIntroStageCanOfferQuest = 2;
        private const string SpawnedQuestGiverName = "StreetQuestRPG.OutdoorQuestGiver";
        private const string QuestGiverCtaKey = "streetquest:cta_talk";
        private static readonly string[] QuestGiverVisualPrefabNames =
        {
            "Characters/Homeless",
            "Prefabs/Characters/Homeless",
            "Homeless"
        };
        private const Gender QuestGiverVisualGender = Gender.Male;
        private const int QuestGiverVisualAgeInDays = 42 * 365;
        private const int QuestGiverVisualSeed = 104729;
        private const string SellerStandOverlayHeaderKey = MackNameKey;
        private static readonly Vector3 QuestGiverVisualLocalPosition = new(0f, 0f, 0f);
        private static readonly Vector3 QuestGiverVisualLocalEulerAngles = new(0f, 90f, 0f);
        private static readonly Vector3 InteractionRendererLocalPosition = new(0f, 0.9f, 0f);
        private static readonly Vector3 InteractionRendererLocalScale = new(0.08f, 0.08f, 0.08f);
        private static readonly Vector3 FixedSpawnPosition = new(301.58f, 0.09f, -188.47f);
        private static readonly Vector3 FixedForward = new(0f, 0f, -1f);
        private static readonly Vector3 DefaultSpawnOffsetFromPlayer = new(0f, 0f, 4f);
        private static readonly Vector3 SellerPositionLocalOffset = new(0f, 0f, -0.85f);
        private static readonly Vector3 NavTargetLocalOffset = new(0f, 0f, 1.25f);
        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<string, int> OriginalDialogTypesByAddress = new();
        private static readonly object LogSync = new();
        private static readonly string PreferredWorkspaceLogDirectory =
            @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";
        private static string DebugLogDirectory;
        private static readonly Dictionary<string, GameObject> SpawnedCharacterRoots = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Component> SpawnedCharacterControllers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> CharacterIdsByControllerInstanceId = new();
        private static readonly Dictionary<string, string> SpawnedCharacterStateSignatures = new(StringComparer.OrdinalIgnoreCase);
        private static Vector3? PreferredQuestGiverSpawnPosition;
        private static bool QuestGiverCtaInstalled;
        private static string DebugLogFilePath;
        private static object CachedQuestStateOwner;
        private static StreetQuestQuestStateRecord CachedQuestStateRecord;

        public const string MackContactId = "streetquest:mack_contact";
        public const string MackNameKey = "streetquest:mack_name";

        public static readonly Address MackAddress = new("ba:street_secondavenue", 6);

        public static StreetQuestPhysicalQuestGiverInstallResult TryInstallPhysicalQuestGiver(CallDialogType dialogType)
        {
            LogDebug($"TryInstallPhysicalQuestGiver start dialogType={dialogType}");
            var result = StreetQuestPhysicalQuestGiverInstallResult.None;
            if (TryOverrideSpecialServiceDialog(MackAddress, dialogType))
                result |= StreetQuestPhysicalQuestGiverInstallResult.SpecialService;

            LogDebug($"TryInstallPhysicalQuestGiver result={result}");
            return result;
        }
    }
}
