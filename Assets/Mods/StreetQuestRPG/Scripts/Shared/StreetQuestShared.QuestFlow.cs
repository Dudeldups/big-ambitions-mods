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
        public static bool AcceptQuest(StreetQuestQuestDefinition quest)
        {
            if (quest == null)
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id ||
                record.CurrentQuestState != StreetQuestQuestProgressState.NotStarted ||
                !StreetQuestQuestCatalog.AreRequirementsMet(quest, record))
                return false;

            record.CurrentQuestState = StreetQuestQuestProgressState.Active;
            if (record.IntroStage < HomelessIntroStageCanOfferQuest)
                record.IntroStage = HomelessIntroStageCanOfferQuest;
            record.AddStoryFlags(quest.AcceptedStoryFlags);
            SaveQuestStateRecord(record);
            RefreshSpawnedCharacters();
            return true;
        }


        public static bool CanTurnIn(StreetQuestQuestDefinition quest)
        {
            if (quest == null)
                return false;

            return AreAllObjectivesSatisfied(quest);
        }


        public static bool MarkReadyToTurnIn(StreetQuestQuestDefinition quest)
        {
            if (quest == null || !CanTurnIn(quest))
                return false;

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id)
                return false;

            if (record.CurrentQuestState == StreetQuestQuestProgressState.Active)
            {
                record.CurrentQuestState = StreetQuestQuestProgressState.ReadyToTurnIn;
                SaveQuestStateRecord(record);
            }

            return record.CurrentQuestState == StreetQuestQuestProgressState.ReadyToTurnIn;
        }


        public static bool CompleteQuest(StreetQuestQuestDefinition quest)
        {
            LogDebug($"CompleteQuest start quest={(quest?.Id ?? "<null>")}");
            if (quest == null || !CanTurnIn(quest) || !TryConsumeQuestObjectives(quest))
            {
                LogDebug($"CompleteQuest aborted quest={(quest?.Id ?? "<null>")} canTurnIn={CanTurnIn(quest)}");
                return false;
            }

            var record = GetQuestStateRecord();
            if (record.CurrentQuestId != quest.Id)
            {
                LogDebug($"CompleteQuest aborted quest={quest.Id} currentQuestId={record.CurrentQuestId}");
                return false;
            }

            var rewardSummary = GrantRewards(quest, record);
            LogDebug($"CompleteQuest rewards quest={quest.Id} cash={rewardSummary.CashAmount} favorChanges={rewardSummary.FavorDeltas.Count} favorMack={record.GetFavor(StreetQuestCharacterCatalog.DefaultQuestGiverId)}");
            record.AddStoryFlags(quest.CompletedStoryFlags);
            record.CompletedQuestIds.Add(quest.Id);

            var nextQuestId = StreetQuestQuestCatalog.ResolveNextQuestId(quest, record);
            if (string.IsNullOrEmpty(nextQuestId))
            {
                record.CurrentQuestId = string.Empty;
                record.CurrentQuestState = StreetQuestQuestProgressState.Completed;
            }
            else
            {
                record.CurrentQuestId = nextQuestId;
                record.CurrentQuestState = StreetQuestQuestProgressState.NotStarted;
            }

            SaveQuestStateRecord(record);
            LogDebug($"CompleteQuest saved quest={quest.Id} nextQuestId={record.CurrentQuestId} state={record.CurrentQuestState} favorMack={record.GetFavor(StreetQuestCharacterCatalog.DefaultQuestGiverId)}");
            RefreshSpawnedCharacters();
            ShowRewardSummaryNotification(rewardSummary);
            return true;
        }


        private static bool AreAllObjectivesSatisfied(StreetQuestQuestDefinition quest)
        {
            foreach (var objective in quest.Objectives)
            {
                if (!IsObjectiveSatisfied(quest, objective))
                    return false;
            }

            return true;
        }


        private static bool IsObjectiveSatisfied(StreetQuestQuestDefinition quest, StreetQuestQuestObjectiveDefinition objective)
        {
            if (quest == null || objective == null)
                return true;

            switch (objective.ObjectiveType)
            {
                case StreetQuestQuestObjectiveType.BringItem:
                    return objective.InventorySource switch
                    {
                        StreetQuestQuestInventorySource.Quest => StreetQuestInventoryService.GetAmount(objective.ItemName) >= objective.Amount,
                        StreetQuestQuestInventorySource.Either => GetVanillaPlayerItemAmount(objective.ItemName) + StreetQuestInventoryService.GetAmount(objective.ItemName) >= objective.Amount,
                        _ => GetVanillaPlayerItemAmount(objective.ItemName) >= objective.Amount
                    };
                case StreetQuestQuestObjectiveType.TalkToCharacter:
                case StreetQuestQuestObjectiveType.VisitLocation:
                    return HasObjectiveToken(objective.GetTrackingToken(quest.Id));
                case StreetQuestQuestObjectiveType.HaveStoryFlag:
                    return HasStoryFlag(objective.StoryFlagId);
                case StreetQuestQuestObjectiveType.CompleteQuest:
                    return GetQuestStateRecord().CompletedQuestIds.Contains(objective.QuestId);
                default:
                    return true;
            }
        }


        private static bool TryConsumeQuestObjectives(StreetQuestQuestDefinition quest)
        {
            foreach (var objective in quest.Objectives.Where(value => value != null))
            {
                if (objective.ObjectiveType != StreetQuestQuestObjectiveType.BringItem)
                    continue;

                if (!TryConsumeQuestItems(objective.ItemName, objective.Amount, objective.InventorySource))
                    return false;
            }

            return true;
        }


        private static StreetQuestRewardSummary GrantRewards(
            StreetQuestQuestDefinition quest,
            StreetQuestQuestStateRecord record)
        {
            var summary = new StreetQuestRewardSummary();
            if (quest == null || record == null)
                return summary;

            var hasFavorReward = quest.Rewards.Any(value =>
                value != null && value.RewardType == StreetQuestQuestRewardType.Favor);

            foreach (var reward in quest.Rewards.Where(value => value != null))
            {
                LogDebug($"GrantRewards quest={(quest?.Id ?? "<null>")} rewardType={reward.RewardType} amount={reward.Amount} characterId={reward.CharacterId ?? "<null>"} storyFlagId={reward.StoryFlagId ?? "<null>"}");
                switch (reward.RewardType)
                {
                    case StreetQuestQuestRewardType.Cash:
                        GrantReward(reward.Amount, showNotification: !hasFavorReward);
                        summary.CashAmount += reward.Amount;
                        break;
                    case StreetQuestQuestRewardType.StoryFlag:
                        record.AddStoryFlag(reward.StoryFlagId);
                        break;
                    case StreetQuestQuestRewardType.Favor:
                        if (record.ChangeFavor(reward.CharacterId, reward.Amount))
                            summary.AddFavorDelta(reward.CharacterId, reward.Amount);
                        break;
                }
            }

            return summary;
        }
    }
}
