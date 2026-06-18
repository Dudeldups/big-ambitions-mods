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


        public static StreetQuestQuestProgressState GetQuestProgress(string questId)
        {
            var record = GetQuestStateRecord();
            if (record.CompletedQuestIds.Contains(questId))
                return StreetQuestQuestProgressState.Completed;

            return record.CurrentQuestId == questId
                ? record.CurrentQuestState
                : StreetQuestQuestProgressState.NotStarted;
        }


        public static bool HasIntroducedHomelessQuestline()
        {
            return HasStoryFlag("streetquest:flag_mack_intro_started")
                   || HasStoryFlag("streetquest:flag_mack_offer_unlocked")
                   || GetQuestStateRecord().CompletedQuestIds.Count > 0
                   || GetQuestStateRecord().CurrentQuestState != StreetQuestQuestProgressState.NotStarted
                   || GetQuestStateRecord().CurrentQuestId != (StreetQuestQuestCatalog.FirstQuest?.Id ?? string.Empty);
        }


        public static int GetHomelessIntroStage()
        {
            if (HasStoryFlag("streetquest:flag_mack_offer_unlocked"))
                return HomelessIntroStageCanOfferQuest;
            if (HasStoryFlag("streetquest:flag_mack_intro_started"))
                return HomelessIntroStageKnowsPast;
            return HomelessIntroStageInitial;
        }


        public static void AdvanceHomelessIntroStage(int stage)
        {
            var record = GetQuestStateRecord();
            if (stage <= record.IntroStage)
                return;

            record.IntroStage = stage;
            if (stage >= HomelessIntroStageKnowsPast)
                record.AddStoryFlag("streetquest:flag_mack_intro_started");
            if (stage >= HomelessIntroStageCanOfferQuest)
                record.AddStoryFlag("streetquest:flag_mack_offer_unlocked");
            SaveQuestStateRecord(record);
        }


        public static void UnlockHomelessBackstory() => AdvanceHomelessIntroStage(HomelessIntroStageKnowsPast);


        public static void UnlockHomelessQuestOffer() => AdvanceHomelessIntroStage(HomelessIntroStageCanOfferQuest);


        public static bool HasStoryFlag(string storyFlagId)
        {
            if (string.IsNullOrWhiteSpace(storyFlagId))
                return false;

            return GetQuestStateRecord().StoryFlags.Contains(storyFlagId);
        }


        public static void AddStoryFlag(string storyFlagId)
        {
            if (string.IsNullOrWhiteSpace(storyFlagId))
                return;

            var record = GetQuestStateRecord();
            if (!record.AddStoryFlag(storyFlagId))
                return;

            SaveQuestStateRecord(record);
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
                SaveQuestStateRecord(record);
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
            return true;
        }


        public static bool ChangeFavor(string characterId, int delta)
        {
            var record = GetQuestStateRecord();
            if (!record.ChangeFavor(characterId, delta))
                return false;

            SaveQuestStateRecord(record);
            return true;
        }


        private static string ResolveCharacterDisplayName(string characterId)
        {
            var character = StreetQuestCharacterCatalog.Get(characterId);
            if (!string.IsNullOrWhiteSpace(character?.nameKey))
                return character.nameKey.Localize().ToString();

            if (!string.IsNullOrWhiteSpace(character?.displayName))
                return character.displayName;

            return characterId ?? "NPC";
        }


        private static StreetQuestQuestStateRecord GetQuestStateRecord()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.modData == null)
                return new StreetQuestQuestStateRecord();

            if (!saveGame.modData.TryGetValue(QuestStateModDataKey, out var serializedRecord))
                return new StreetQuestQuestStateRecord();

            var record = StreetQuestQuestStateRecord.Deserialize(serializedRecord);
            if (!string.IsNullOrEmpty(record.CurrentQuestId) &&
                StreetQuestQuestCatalog.Get(record.CurrentQuestId) == null)
                return new StreetQuestQuestStateRecord();

            return record;
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
            saveGame.modData[QuestStateModDataKey] = record.Serialize();
        }
    }
}
