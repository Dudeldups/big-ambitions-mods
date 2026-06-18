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
        public static StreetQuestQuestDefinition GetCurrentQuest()
        {
            var record = GetQuestStateRecord();
            return StreetQuestQuestCatalog.Get(record.CurrentQuestId);
        }


        public static StreetQuestQuestStateRecord GetQuestStateSnapshot()
        {
            return GetQuestStateRecord();
        }

        public static IReadOnlyCollection<string> GetKnownCharacterIds()
        {
            return GetQuestStateRecord().KnownCharacterIds;
        }

        public static StreetQuestQuestProgressState GetQuestProgress(string questId)
        {
            var record = GetQuestStateRecord();
            if (record.CompletedQuestIds.Contains(questId))
                return StreetQuestQuestProgressState.Completed;

            return record.CurrentQuestId == questId
                ? record.CurrentQuestState
                : StreetQuestQuestProgressState.NotStarted;
        }

        public static bool HasStoryFlag(string storyFlagId)
        {
            if (string.IsNullOrWhiteSpace(storyFlagId))
                return false;

            return GetQuestStateRecord().StoryFlags.Contains(storyFlagId);
        }

        public static bool HasMetCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            return GetQuestStateRecord().KnownCharacterIds.Contains(characterId);
        }


        public static void AddStoryFlag(string storyFlagId)
        {
            if (string.IsNullOrWhiteSpace(storyFlagId))
                return;

            var record = GetQuestStateRecord();
            if (!record.AddStoryFlag(storyFlagId))
                return;

            SaveQuestStateRecord(record);
            RefreshSpawnedCharacters();
        }


        public static void AddStoryFlags(IEnumerable<string> storyFlagIds)
        {
            if (storyFlagIds == null)
                return;

            var record = GetQuestStateRecord();
            var changed = false;
            foreach (var storyFlagId in storyFlagIds)
                changed |= record.AddStoryFlag(storyFlagId);

            if (changed)
            {
                SaveQuestStateRecord(record);
                RefreshSpawnedCharacters();
            }
        }


        public static int GetFavor(string characterId)
        {
            return GetQuestStateRecord().GetFavor(characterId);
        }


        public static bool SetFavor(string characterId, int value)
        {
            var record = GetQuestStateRecord();
            if (!record.SetFavor(characterId, value))
                return false;

            SaveQuestStateRecord(record);
            RefreshSpawnedCharacters();
            return true;
        }


        public static bool ChangeFavor(string characterId, int delta)
        {
            var record = GetQuestStateRecord();
            if (!record.ChangeFavor(characterId, delta))
                return false;

            SaveQuestStateRecord(record);
            RefreshSpawnedCharacters();
            return true;
        }

        public static bool RecordKnownCharacter(string characterId)
        {
            var record = GetQuestStateRecord();
            if (!record.AddKnownCharacter(characterId))
                return false;

            SaveQuestStateRecord(record);
            return true;
        }

        internal static string ResolveCharacterDisplayName(string characterId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (!string.IsNullOrWhiteSpace(character?.nameKey))
                return character.nameKey.Localize().ToString();

            if (!string.IsNullOrWhiteSpace(character?.displayName))
                return character.displayName;

            return characterId ?? "NPC";
        }

        internal static string ResolveCharacterProfession(string characterId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (!string.IsNullOrWhiteSpace(character?.professionKey))
                return character.professionKey.Localize().ToString();

            return string.Empty;
        }


        private static StreetQuestQuestStateRecord GetQuestStateRecord()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.modData == null)
            {
                CachedQuestStateOwner = null;
                CachedQuestStateRecord = null;
                return new StreetQuestQuestStateRecord();
            }

            if (ReferenceEquals(CachedQuestStateOwner, saveGame) && CachedQuestStateRecord != null)
                return CachedQuestStateRecord;

            if (!saveGame.modData.TryGetValue(QuestStateModDataKey, out var serializedRecord))
            {
                CachedQuestStateOwner = saveGame;
                CachedQuestStateRecord = new StreetQuestQuestStateRecord();
                return CachedQuestStateRecord;
            }

            try
            {
                var record = StreetQuestQuestStateRecord.Deserialize(serializedRecord);
                if (!string.IsNullOrEmpty(record.CurrentQuestId) &&
                    StreetQuestQuestCatalog.Get(record.CurrentQuestId) == null)
                {
                    CachedQuestStateOwner = saveGame;
                    CachedQuestStateRecord = new StreetQuestQuestStateRecord();
                    LogDebug($"GetQuestStateRecord returning new record: unknown questId={record.CurrentQuestId}");
                    return CachedQuestStateRecord;
                }

                CachedQuestStateOwner = saveGame;
                CachedQuestStateRecord = record;
                return record;
            }
            catch (Exception exception)
            {
                CachedQuestStateOwner = saveGame;
                CachedQuestStateRecord = new StreetQuestQuestStateRecord();
                LogDebug($"GetQuestStateRecord failed: {exception}");
                Debug.LogWarning($"StreetQuestRPG: Failed to read quest state. Resetting mod state for this session. {exception}");
                return CachedQuestStateRecord;
            }
        }


        private static bool HasObjectiveToken(string objectiveToken)
        {
            if (string.IsNullOrWhiteSpace(objectiveToken))
                return false;

            return GetQuestStateRecord().ObjectiveTokens.Contains(objectiveToken);
        }


        private static void MarkObjectiveToken(string objectiveToken)
        {
            if (string.IsNullOrWhiteSpace(objectiveToken))
                return;

            var record = GetQuestStateRecord();
            if (!record.AddObjectiveToken(objectiveToken))
                return;

            SaveQuestStateRecord(record);
        }


        private static void SaveQuestStateRecord(StreetQuestQuestStateRecord record)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            saveGame.modData ??= new Dictionary<string, string>();
            var serialized = record.Serialize();
            saveGame.modData[QuestStateModDataKey] = serialized;
            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            CachedQuestStateOwner = saveGame;
            CachedQuestStateRecord = record;
            LogDebug($"SaveQuestStateRecord questId={record.CurrentQuestId} state={record.CurrentQuestState} serialized={serialized}");
        }
    }
}
