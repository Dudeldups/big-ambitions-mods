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

            var saveGame = SaveGameManager.Current;
            var moneyBefore = saveGame?.Money ?? 0f;
            var expectedMoney = moneyBefore + rewardAmount;
            var transactionData = new Dictionary<string, string>
            {
                { "amount", rewardAmount.ToString() }
            };
            var transactionInfo = new TransactionInfo("ba:transaction_playerjobsalary", "ba:transactioncategory_salaryincome", transactionData, false);

            var changedMoneySafely = TryChangeMoneyViaBestVanillaPath(
                rewardAmount,
                transactionInfo,
                showNotification,
                out var changeMoneyPath);
            var moneyAfter = saveGame?.Money ?? 0f;

            if (!changedMoneySafely || saveGame == null)
            {
                if (saveGame != null)
                {
                    saveGame.Money = expectedMoney;
                    saveGame.hasEverUsedMods = true;
                    SaveGameManager.MarkChange();
                }
                return;
            }

            if (Math.Abs(moneyAfter - expectedMoney) > 0.01f)
            {
                saveGame.Money = expectedMoney;
                saveGame.hasEverUsedMods = true;
                SaveGameManager.MarkChange();
                return;
            }

            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }


        private static bool TryChangeMoneyViaBestVanillaPath(
            float amount,
            TransactionInfo transactionInfo,
            bool showNotification,
            out string path)
        {
            path = "GameManager.ChangeMoneySafe(legacy)";
            var gameManagerType = typeof(GameManager);
            var methods = gameManagerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, "ChangeMoneySafe", StringComparison.Ordinal))
                .ToArray();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 6)
                    continue;

                if (parameters[0].ParameterType != typeof(float) ||
                    parameters[1].ParameterType != typeof(TransactionInfo))
                {
                    continue;
                }

                var thirdParameterType = parameters[2].ParameterType;
                var fourthParameterType = parameters[3].ParameterType;
                if (thirdParameterType == typeof(int?) && fourthParameterType == typeof(Address))
                    continue;

                var supportsNullableVector3 = thirdParameterType == typeof(Vector3?);
                var supportsStringSource = fourthParameterType == typeof(string);
                if (!supportsNullableVector3 || !supportsStringSource)
                    continue;

                try
                {
                    path = $"GameManager.ChangeMoneySafe({thirdParameterType.Name},{fourthParameterType.Name})";
                    return (bool)method.Invoke(null, new object[]
                    {
                        amount,
                        transactionInfo,
                        null,
                        "StreetQuestRPG",
                        false,
                        showNotification
                    });
                }
                catch (Exception)
                {
                }
            }

            return GameManager.ChangeMoneySafe(amount, transactionInfo, null, null, false, showNotification);
        }


        private static void ShowRewardSummaryNotification(StreetQuestRewardSummary summary)
        {
            LogDebug(summary == null
                ? "ShowRewardSummaryNotification skipped: summary null"
                : $"ShowRewardSummaryNotification start cash={summary.CashAmount} favorChanges={summary.FavorDeltas.Count}");
            if (summary == null || !summary.HasFavorChanges)
            {
                LogDebug("ShowRewardSummaryNotification skipped: no favor changes");
                return;
            }

            var favorChanges = summary.FavorDeltas.ToList();
            if (favorChanges.Count == 1)
            {
                var favorChange = favorChanges[0];
                var amount = Math.Abs(favorChange.Value).ToString();
                var npcName = ResolveCharacterDisplayName(favorChange.Key);
                var baseKey = favorChange.Value >= 0
                    ? "streetquest:popup_favor_gain"
                    : "streetquest:popup_favor_loss";

                var localizationData = new Dictionary<string, string>
                {
                    { "amount", amount },
                    { "npcname", npcName }
                };

                if (summary.CashAmount > 0)
                {
                    baseKey = favorChange.Value >= 0
                        ? "streetquest:popup_favor_gain_with_cash"
                        : "streetquest:popup_favor_loss_with_cash";
                    localizationData = new Dictionary<string, string>
                    {
                        { "favor", amount },
                        { "npcname", npcName },
                        { "amount", summary.CashAmount.ToString() }
                    };
                }

                LogDebug($"ShowRewardSummaryNotification single key={baseKey}");
                ShowInfoNotification(baseKey, localizationData);
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

            var combinedMessage = string.Join("\n", lines);
            LogDebug($"ShowRewardSummaryNotification multi message={combinedMessage}");
            ShowInfoNotification(
                "streetquest:popup_raw",
                new Dictionary<string, string> { { "message", combinedMessage } });
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
