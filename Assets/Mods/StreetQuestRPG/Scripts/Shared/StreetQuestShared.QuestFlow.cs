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
            {
                LogDebug("AcceptQuest aborted: quest=<null>");
                return false;
            }

            var record = GetQuestStateRecord();
            var progress = GetQuestProgress(quest.Id);
            LogDebug($"AcceptQuest start quest={quest.Id} type={quest.QuestType} mainQuestId={record.CurrentMainQuestId} mainState={record.CurrentMainQuestState} progress={progress}");
            if (progress != StreetQuestQuestProgressState.NotStarted ||
                !StreetQuestQuestCatalog.AreRequirementsMet(quest, record))
            {
                LogDebug($"AcceptQuest aborted quest={quest.Id} progress={progress} requirementsMet={StreetQuestQuestCatalog.AreRequirementsMet(quest, record)}");
                return false;
            }

            if (quest.QuestType == StreetQuestQuestType.Main)
            {
                if (record.CurrentMainQuestId != quest.Id ||
                    record.CurrentMainQuestState != StreetQuestQuestProgressState.NotStarted)
                {
                    LogDebug($"AcceptQuest aborted main quest={quest.Id} currentMainQuestId={record.CurrentMainQuestId} currentMainState={record.CurrentMainQuestState}");
                    return false;
                }

                record.CurrentMainQuestState = StreetQuestQuestProgressState.Active;
            }
            else if (!record.TryActivateSideQuest(quest.Id))
            {
                LogDebug($"AcceptQuest aborted side quest={quest.Id} activation failed");
                return false;
            }

            record.AddStoryFlags(quest.AcceptedStoryFlags);
            SaveQuestStateRecord(record);
            LogDebug($"AcceptQuest saved quest={quest.Id} type={quest.QuestType} mainQuestId={record.CurrentMainQuestId} mainState={record.CurrentMainQuestState}");
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
            if (quest.QuestType == StreetQuestQuestType.Main)
            {
                if (record.CurrentMainQuestId != quest.Id)
                    return false;

                if (record.CurrentMainQuestState == StreetQuestQuestProgressState.Active)
                {
                    record.CurrentMainQuestState = StreetQuestQuestProgressState.ReadyToTurnIn;
                    SaveQuestStateRecord(record);
                }

                return record.CurrentMainQuestState == StreetQuestQuestProgressState.ReadyToTurnIn;
            }

            if (!record.ActiveSideQuestIds.Contains(quest.Id) && !record.ReadySideQuestIds.Contains(quest.Id))
                return false;

            if (record.TryMarkSideQuestReady(quest.Id))
                SaveQuestStateRecord(record);

            return record.ReadySideQuestIds.Contains(quest.Id);
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
            if (quest.QuestType == StreetQuestQuestType.Main && record.CurrentMainQuestId != quest.Id)
            {
                LogDebug($"CompleteQuest aborted quest={quest.Id} currentMainQuestId={record.CurrentMainQuestId}");
                return false;
            }

            if (quest.QuestType == StreetQuestQuestType.Side &&
                !record.ActiveSideQuestIds.Contains(quest.Id) &&
                !record.ReadySideQuestIds.Contains(quest.Id))
            {
                LogDebug($"CompleteQuest aborted side quest={quest.Id} not active");
                return false;
            }

            var rewardSummary = GrantRewards(quest, record);
            LogDebug($"CompleteQuest rewards quest={quest.Id} cash={rewardSummary.CashAmount} favorChanges={rewardSummary.FavorDeltas.Count} favorMack={record.GetFavor(StreetQuestCharacterCatalog.DefaultQuestGiverId)}");
            record.AddStoryFlags(quest.CompletedStoryFlags);
            record.CompletedQuestIds.Add(quest.Id);

            if (quest.QuestType == StreetQuestQuestType.Main)
            {
                var nextQuestId = StreetQuestQuestCatalog.ResolveNextQuestId(quest, record);
                if (string.IsNullOrEmpty(nextQuestId))
                {
                    record.CurrentMainQuestId = string.Empty;
                    record.CurrentMainQuestState = StreetQuestQuestProgressState.Completed;
                }
                else
                {
                    record.CurrentMainQuestId = nextQuestId;
                    record.CurrentMainQuestState = StreetQuestQuestProgressState.NotStarted;
                }
            }
            else
            {
                record.ClearSideQuest(quest.Id);
            }

            SaveQuestStateRecord(record);
            LogDebug($"CompleteQuest saved quest={quest.Id} mainQuestId={record.CurrentMainQuestId} mainState={record.CurrentMainQuestState} favorMack={record.GetFavor(StreetQuestCharacterCatalog.DefaultQuestGiverId)}");
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
                    case StreetQuestQuestRewardType.Contact:
                        if (GrantContactReward(reward.CharacterId))
                            summary.AddContact(reward.CharacterId);
                        break;
                }
            }

            return summary;
        }
    }
}
