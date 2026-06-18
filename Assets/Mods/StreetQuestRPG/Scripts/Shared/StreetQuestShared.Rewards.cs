using System;
using System.Collections;
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
using UI;
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
            var transactionsBefore = saveGame?.Transactions?.Count ?? -1;
            var transactionData = new Dictionary<string, string>
            {
                { "amount", rewardAmount.ToString() }
            };
            var transactionInfo = new TransactionInfo("ba:transaction_playerjobsalary", "ba:transactioncategory_salaryincome", transactionData, false);

            LogDebug($"GrantReward start amount={rewardAmount} moneyBefore={moneyBefore} showNotification={showNotification}");

            if (saveGame != null && Math.Abs(expectedMoney - moneyBefore) < 0.01f)
            {
                LogDebug($"GrantReward precision-limited amount={rewardAmount} moneyBefore={moneyBefore} expectedMoney={expectedMoney}");
            }

            var changedMoneySafely = TryChangeMoneyViaBestVanillaPath(
                rewardAmount,
                transactionInfo,
                showNotification,
                out var changeMoneyPath);
            var moneyAfter = saveGame?.Money ?? 0f;
            var transactionsAfter = saveGame?.Transactions?.Count ?? -1;
            var transactionCreatedImmediately = transactionsBefore >= 0 && transactionsAfter > transactionsBefore;
            LogDebug(
                $"GrantReward after ChangeMoneySafe changed={changedMoneySafely} path={changeMoneyPath} moneyAfter={moneyAfter} expected={expectedMoney} transactionsBefore={transactionsBefore} transactionsAfter={transactionsAfter} transactionCreated={transactionCreatedImmediately} uiMoneyText={GetVisibleMoneyTextForDebug()}");
            ScheduleMoneyDiagnostics(rewardAmount, moneyBefore, transactionsBefore);

            if (!changedMoneySafely || saveGame == null)
            {
                if (saveGame != null)
                {
                    saveGame.Money = expectedMoney;
                    saveGame.hasEverUsedMods = true;
                    SaveGameManager.MarkChange();
                    LogDebug($"GrantReward fallback applied expected={expectedMoney}");
                }
                return;
            }

            if (Math.Abs(moneyAfter - expectedMoney) > 0.01f)
            {
                saveGame.Money = expectedMoney;
                saveGame.hasEverUsedMods = true;
                SaveGameManager.MarkChange();
                LogDebug($"GrantReward corrected money mismatch expected={expectedMoney} actualAfterCall={moneyAfter}");
                return;
            }

            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
            LogDebug($"GrantReward success persisted money={saveGame.Money}");
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
                catch (Exception exception)
                {
                    LogDebug($"GrantReward rich ChangeMoneySafe invoke failed path={path} exception={exception}");
                }
            }

            return GameManager.ChangeMoneySafe(amount, transactionInfo, null, null, false, showNotification);
        }


        private static void ScheduleMoneyDiagnostics(int rewardAmount, float moneyBefore, int transactionsBefore)
        {
            var watcher = UnityEngine.Object.FindObjectOfType<StreetQuestPhysicalQuestGiverWatcher>();
            if (watcher == null || !watcher.isActiveAndEnabled)
            {
                LogDebug("GrantReward diagnostics skipped: watcher missing");
                return;
            }

            watcher.StartCoroutine(LogMoneyDiagnosticsCoroutine(rewardAmount, moneyBefore, transactionsBefore));
        }


        private static IEnumerator LogMoneyDiagnosticsCoroutine(int rewardAmount, float moneyBefore, int transactionsBefore)
        {
            yield return null;
            LogMoneyDiagnosticsSnapshot("afterOneFrame", rewardAmount, moneyBefore, transactionsBefore);
            yield return new WaitForSecondsRealtime(1f);
            LogMoneyDiagnosticsSnapshot("afterOneSecond", rewardAmount, moneyBefore, transactionsBefore);
        }


        private static void LogMoneyDiagnosticsSnapshot(
            string stage,
            int rewardAmount,
            float moneyBefore,
            int transactionsBefore)
        {
            var saveGame = SaveGameManager.Current;
            var moneyNow = saveGame?.Money ?? 0f;
            var transactionsNow = saveGame?.Transactions?.Count ?? -1;
            var transactionCreated = transactionsBefore >= 0 && transactionsNow > transactionsBefore;
            var latestTransaction = DescribeLatestTransaction(saveGame);
            LogDebug(
                $"GrantReward {stage} amount={rewardAmount} moneyBefore={moneyBefore} moneyNow={moneyNow} uiMoneyText={GetVisibleMoneyTextForDebug()} transactionsBefore={transactionsBefore} transactionsNow={transactionsNow} transactionCreated={transactionCreated} latestTransaction={latestTransaction}");
        }


        private static string DescribeLatestTransaction(GameInstance saveGame)
        {
            if (saveGame?.Transactions == null || saveGame.Transactions.Count == 0)
                return "<none>";

            try
            {
                var latest = saveGame.Transactions.Last();
                if (latest == null)
                    return "<null>";

                var transactionType = GetMemberValue(latest, "transactionType") as string ?? "<unknown>";
                var amount = GetMemberValue(latest, "amount");
                var balance = GetMemberValue(latest, "balance");
                return $"type={transactionType} amount={amount ?? "<null>"} balance={balance ?? "<null>"}";
            }
            catch (Exception exception)
            {
                return $"<error {exception.GetType().Name}>";
            }
        }


        private static string GetVisibleMoneyTextForDebug()
        {
            try
            {
                var uiRoot = InstanceBehavior<UIs>.Instance;
                var topBar = uiRoot?.topBar;
                if (topBar == null)
                    return "<topbar-missing>";

                var moneyLabel = topBar.money;
                if (moneyLabel != null && !string.IsNullOrWhiteSpace(moneyLabel.text))
                    return moneyLabel.text;

                var fullMenuMoneyLabel = topBar.fullMenuMoney;
                if (fullMenuMoneyLabel != null && !string.IsNullOrWhiteSpace(fullMenuMoneyLabel.text))
                    return fullMenuMoneyLabel.text;

                return "<money-label-empty>";
            }
            catch (Exception exception)
            {
                return $"<error {exception.GetType().Name}>";
            }
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

                LogDebug($"ShowRewardSummaryNotification single message={message}");
                ShowInfoNotification(message);
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
            ShowInfoNotification(combinedMessage);
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
