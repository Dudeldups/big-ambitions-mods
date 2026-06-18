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
        private static void GrantReward(int rewardAmount, bool showNotification)
        {
            if (rewardAmount <= 0)
                return;

            var transactionData = new Dictionary<string, string>
            {
                { "amount", rewardAmount.ToString() }
            };
            var transactionInfo = new TransactionInfo("streetquest:transaction_reward", transactionData, false);

            if (!GameManager.ChangeMoneySafe(rewardAmount, transactionInfo, showNotification: showNotification))
            {
                var saveGame = SaveGameManager.Current;
                if (saveGame != null)
                    saveGame.Money += rewardAmount;
            }
        }


        private static void ShowRewardSummaryNotification(StreetQuestRewardSummary summary)
        {
            if (summary == null || !summary.HasFavorChanges)
                return;

            var favorChanges = summary.FavorDeltas.ToList();
            if (favorChanges.Count == 1)
            {
                var favorChange = favorChanges[0];
                var amount = Math.Abs(favorChange.Value).ToString();
                var npcName = ResolveCharacterDisplayName(favorChange.Key);
                var baseKey = favorChange.Value >= 0
                    ? "streetquest:popup_favor_gain"
                    : "streetquest:popup_favor_loss";

                var message = baseKey.Localize(new Dictionary<string, string>
                {
                    { "amount", amount },
                    { "npcname", npcName }
                }).ToString();

                if (summary.CashAmount > 0)
                {
                    var combinedKey = favorChange.Value >= 0
                        ? "streetquest:popup_favor_gain_with_cash"
                        : "streetquest:popup_favor_loss_with_cash";
                    message = combinedKey.Localize(new Dictionary<string, string>
                    {
                        { "favor", amount },
                        { "npcname", npcName },
                        { "amount", summary.CashAmount.ToString() }
                    }).ToString();
                }
                else if (favorChange.Value < 0)
                {
                    message = "streetquest:popup_favor_loss".Localize(new Dictionary<string, string>
                    {
                        { "amount", amount },
                        { "npcname", npcName }
                    }).ToString();
                }

                ShowDebugNotification(message, $"streetquest-reward-{favorChange.Key}");
                return;
            }

            var lines = new List<string>();
            foreach (var favorChange in favorChanges)
            {
                var baseKey = favorChange.Value >= 0
                    ? "streetquest:popup_favor_gain"
                    : "streetquest:popup_favor_loss";
                lines.Add(baseKey.Localize(new Dictionary<string, string>
                {
                    { "amount", Math.Abs(favorChange.Value).ToString() },
                    { "npcname", ResolveCharacterDisplayName(favorChange.Key) }
                }).ToString());
            }

            if (summary.CashAmount > 0)
            {
                lines.Add("streetquest:popup_reward_cash_only".Localize(new Dictionary<string, string>
                {
                    { "amount", summary.CashAmount.ToString() }
                }).ToString());
            }

            ShowDebugNotification(string.Join("\n", lines), "streetquest-reward-summary");
        }


        private sealed class StreetQuestRewardSummary
        {
            public int CashAmount { get; set; }
            public Dictionary<string, int> FavorDeltas { get; } = new(StringComparer.OrdinalIgnoreCase);
            public bool HasFavorChanges => FavorDeltas.Count > 0;

            public void AddFavorDelta(string characterId, int delta)
            {
                if (string.IsNullOrWhiteSpace(characterId) || delta == 0)
                    return;

                FavorDeltas[characterId] = FavorDeltas.TryGetValue(characterId, out var existing)
                    ? existing + delta
                    : delta;
            }
        }
    }
}
