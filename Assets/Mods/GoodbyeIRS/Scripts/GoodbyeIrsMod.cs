#nullable enable
using System;
using System.Collections;
using System.Threading.Tasks;
using BAModAPI;
using Entities;
using Helpers;
using UnityEngine;

[assembly: RegisterModClass(typeof(GoodbyeIRS.GoodbyeIrsMod))]

namespace GoodbyeIRS
{
    /// <summary>
    /// Prevents the base game's daily IRS handler from running without changing
    /// the save's calendar settings. This keeps normal player aging intact.
    /// </summary>
    [ModEntryOnCityLoad]
    public sealed class GoodbyeIrsMod : IModBigAmbitions
    {
        private static GoodbyeIrsRuntime? runtime;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            runtime = GoodbyeIrsRuntime.Initialize(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;
            return Task.CompletedTask;
        }
    }

    [DefaultExecutionOrder(-10000)]
    internal sealed class GoodbyeIrsRuntime : MonoBehaviour
    {
        // TaxHelper.RunDaily checks Day % daysPerYear immediately after
        // GlobalEvents.onNewDay. This value cannot be reached in normal play,
        // so it reliably makes that one check false without changing aging.
        private const int TaxCheckBypassDaysPerYear = int.MaxValue;

        private static GoodbyeIrsRuntime? instance;

        private ModContext? context;
        private bool restoreCalendarPending;
        private int daysPerYearBeforeTaxCheck;

        internal static GoodbyeIrsRuntime Initialize(ModContext context)
        {
            var runtime = FindObjectOfType<GoodbyeIrsRuntime>();
            if (runtime == null)
            {
                var runtimeObject = new GameObject(nameof(GoodbyeIrsRuntime));
                DontDestroyOnLoad(runtimeObject);
                runtime = runtimeObject.AddComponent<GoodbyeIrsRuntime>();
            }

            runtime.context = context;
            instance = runtime;
            runtime.CleanUpTaxLiabilities();
            runtime.SubscribeToNewDay();
            context.Logger.Info("Goodbye IRS is active. New tax assessments and collections are disabled.");
            return runtime;
        }

        internal void Shutdown()
        {
            RestoreCalendarIfNeeded();
            if (instance == this)
                instance = null;

            Destroy(gameObject);
        }

        private void OnDisable()
        {
            GlobalEvents.onNewDay -= HandleNewDay;
            RestoreCalendarIfNeeded();
        }

        private void SubscribeToNewDay()
        {
            GlobalEvents.onNewDay -= HandleNewDay;
            GlobalEvents.onNewDay += HandleNewDay;
        }

        private void HandleNewDay()
        {
            // The event is invoked directly before TaxHelper.RunDaily. Remove
            // old debt first, then make its annual modulo check fail for this
            // game tick. PlayerHelper.IncreasePlayerAge already ran by here.
            CleanUpTaxLiabilities();

            var save = SaveGameManager.Current;
            if (save?.gameVariables == null || restoreCalendarPending)
                return;

            daysPerYearBeforeTaxCheck = save.gameVariables.daysPerYear;
            save.gameVariables.daysPerYear = TaxCheckBypassDaysPerYear;
            restoreCalendarPending = true;

            if (daysPerYearBeforeTaxCheck > 0 && save.Day % daysPerYearBeforeTaxCheck == 0)
            {
                context?.Logger.Info(
                    $"Skipped IRS assessment on day {save.Day}; restored year length will remain {daysPerYearBeforeTaxCheck}.");
            }

            StartCoroutine(RestoreCalendarAfterTaxCheck());
        }

        private IEnumerator RestoreCalendarAfterTaxCheck()
        {
            // TaxHelper.RunDaily and the rest of GameManager.NewDay execute
            // synchronously before the next frame.
            yield return null;
            RestoreCalendarIfNeeded();
        }

        private void RestoreCalendarIfNeeded()
        {
            if (!restoreCalendarPending)
                return;

            var save = SaveGameManager.Current;
            if (save?.gameVariables != null)
                save.gameVariables.daysPerYear = daysPerYearBeforeTaxCheck;

            restoreCalendarPending = false;
        }

        private void CleanUpTaxLiabilities()
        {
            var save = SaveGameManager.Current;
            if (save == null)
                return;

            var changed = false;
            if (save.currentUnpaidTaxes != null)
            {
                save.currentUnpaidTaxes = null;
                changed = true;
            }

            if (save.currentBackTaxes > 0f)
            {
                save.currentBackTaxes = 0f;
                changed = true;
            }

            if (save.TodoTasks != null)
            {
                for (var index = save.TodoTasks.Count - 1; index >= 0; index--)
                {
                    if (save.TodoTasks[index].type != TodoTaskType.PayTaxes)
                        continue;

                    save.TodoTasks.RemoveAt(index);
                    changed = true;
                }
            }

            if (!changed)
                return;

            GameEvent.Invoke(string.Empty);
            context?.Logger.Info("Cleared outstanding IRS debt and tax tasks.");
        }
    }
}
