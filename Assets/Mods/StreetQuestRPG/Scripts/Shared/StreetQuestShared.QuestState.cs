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
            return GetCurrentMainQuest();
        }

        public static StreetQuestQuestDefinition GetCurrentMainQuest()
        {
            var record = GetQuestStateRecord();
            return StreetQuestQuestCatalog.Get(record.CurrentMainQuestId);
        }

        public static IReadOnlyCollection<StreetQuestQuestDefinition> GetActiveSideQuests()
        {
            var record = GetQuestStateRecord();
            return StreetQuestQuestCatalog.AllOrdered
                .Where(quest =>
                    quest != null &&
                    quest.QuestType == StreetQuestQuestType.Side &&
                    (record.ActiveSideQuestIds.Contains(quest.Id) || record.ReadySideQuestIds.Contains(quest.Id)))
                .ToArray();
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

            if (record.CurrentMainQuestId == questId)
                return record.CurrentMainQuestState;

            if (record.ReadySideQuestIds.Contains(questId))
                return StreetQuestQuestProgressState.ReadyToTurnIn;

            if (record.ActiveSideQuestIds.Contains(questId))
                return StreetQuestQuestProgressState.Active;

            return StreetQuestQuestProgressState.NotStarted;
        }

        public static StreetQuestQuestDefinition GetRelevantQuestForCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return null;

            var record = GetQuestStateRecord();
            var candidateQuests = new List<StreetQuestQuestDefinition>();

            var currentMainQuest = GetCurrentMainQuest();
            if (currentMainQuest != null)
                candidateQuests.Add(currentMainQuest);

            candidateQuests.AddRange(GetActiveSideQuests());

            var activeOrReadyQuest = candidateQuests.FirstOrDefault(quest =>
                quest != null &&
                GetQuestProgress(quest.Id) != StreetQuestQuestProgressState.NotStarted &&
                IsQuestRelevantToCharacter(quest, characterId));
            if (activeOrReadyQuest != null)
                return activeOrReadyQuest;

            return StreetQuestQuestCatalog.AllOrdered.FirstOrDefault(quest =>
                quest != null &&
                quest.Enabled &&
                GetQuestProgress(quest.Id) == StreetQuestQuestProgressState.NotStarted &&
                string.Equals(quest.GiverCharacterId, characterId, StringComparison.OrdinalIgnoreCase) &&
                StreetQuestQuestCatalog.AreRequirementsMet(quest, record));
        }

        public static bool IsQuestRelevantToCharacter(StreetQuestQuestDefinition quest, string characterId)
        {
            if (quest == null || string.IsNullOrWhiteSpace(characterId))
                return false;

            return string.Equals(quest.GiverCharacterId, characterId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(quest.TurnInCharacterId, characterId, StringComparison.OrdinalIgnoreCase) ||
                   quest.Objectives.Any(objective =>
                       objective != null &&
                       string.Equals(objective.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
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

        internal static bool TryGetCharacterWorldPosition(string characterId, out Vector3 worldPosition)
        {
            worldPosition = default;
            var definition = StreetQuestCharacterCatalog.Get(characterId);
            if (definition == null)
                return false;

            if (TryGetSpawnedCharacterRoot(characterId, out var spawnedRoot) && spawnedRoot != null)
            {
                worldPosition = spawnedRoot.transform.position;
                return true;
            }

            var runtimeDefinition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (runtimeDefinition != null && runtimeDefinition.enabled)
            {
                worldPosition = runtimeDefinition.PositionOr(Vector3.zero);
                return true;
            }

            if (definition.enabled)
            {
                worldPosition = definition.PositionOr(Vector3.zero);
                return true;
            }

            return false;
        }


        private static StreetQuestQuestStateRecord GetQuestStateRecord()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.modData == null)
            {
                CachedQuestStateRecord = StreetQuestQuestStateRecord.Deserialize(string.Empty);
                SaveQuestStateRecord(CachedQuestStateRecord);
                return CachedQuestStateRecord;
            }

            if (ReferenceEquals(CachedQuestStateOwner, saveGame) && CachedQuestStateRecord != null)
                return CachedQuestStateRecord;

            if (!saveGame.modData.TryGetValue(QuestStateModDataKey, out var serializedRecord))
            {
                CachedQuestStateOwner = saveGame;
                CachedQuestStateRecord = StreetQuestQuestStateRecord.Deserialize(string.Empty);
                SaveQuestStateRecord(CachedQuestStateRecord);
                return CachedQuestStateRecord;
            }

            try
            {
                var record = StreetQuestQuestStateRecord.Deserialize(serializedRecord);
                if (!string.IsNullOrEmpty(record.CurrentMainQuestId) &&
                    StreetQuestQuestCatalog.Get(record.CurrentMainQuestId) == null)
                {
                    CachedQuestStateOwner = saveGame;
                    CachedQuestStateRecord = StreetQuestQuestStateRecord.Deserialize(string.Empty);
                    LogDebug($"GetQuestStateRecord returning new record: unknown mainQuestId={record.CurrentMainQuestId}");
                    SaveQuestStateRecord(CachedQuestStateRecord);
                    return CachedQuestStateRecord;
                }

                CachedQuestStateOwner = saveGame;
                CachedQuestStateRecord = record;
                return record;
            }
            catch (Exception exception)
            {
                CachedQuestStateOwner = saveGame;
                CachedQuestStateRecord = StreetQuestQuestStateRecord.Deserialize(string.Empty);
                LogDebug($"GetQuestStateRecord failed: {exception}");
                Debug.LogWarning($"StreetQuestRPG: Failed to read quest state. Resetting mod state for this session. {exception}");
                SaveQuestStateRecord(CachedQuestStateRecord);
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
            LogDebug($"SaveQuestStateRecord mainQuestId={record.CurrentMainQuestId} mainState={record.CurrentMainQuestState} activeSideCount={record.ActiveSideQuestIds.Count} readySideCount={record.ReadySideQuestIds.Count} serialized={serialized}");
        }
    }
}
