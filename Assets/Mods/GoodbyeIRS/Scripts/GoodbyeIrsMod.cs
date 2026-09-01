#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Tags;
using Entities;
using Helpers;
using Streets;
using UI.Elements;
using UI.Notification;
using UI.Overlays;
using UnityEngine;

[assembly: RegisterModClass(typeof(GoodbyeIRS.GoodbyeIrsMod))]

namespace GoodbyeIRS
{
    /// <summary>
    /// Lets the player permanently disable IRS tax handling for one save after
    /// bringing a fuel container into the IRS building.
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
        private const string ActivatedModDataKey = "goodbye-irs:activated_v1";

        private static GoodbyeIrsRuntime? instance;

        private ModContext? context;
        private bool activationPromptVisible;
        private bool restoreCalendarPending;
        private int daysPerYearBeforeTaxCheck;
        private IrsActivationOverlay? activationOverlay;

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
            runtime.SubscribeToIrsEntry();

            if (IsActivatedForCurrentSave())
            {
                runtime.CleanUpTaxLiabilities();
                runtime.SubscribeToNewDay();
                context.Logger.Info("Goodbye IRS is already active for this save.");
            }
            else
            {
                context.Logger.Info("Goodbye IRS is waiting for the player to bring a fuel container to the IRS.");
            }

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
            GlobalEvents.onEnterBuilding -= HandleEnterBuilding;
            GlobalEvents.onExitBuilding -= HandleExitBuilding;
            HideActivationOverlay();
            RestoreCalendarIfNeeded();
        }

        private void SubscribeToNewDay()
        {
            GlobalEvents.onNewDay -= HandleNewDay;
            GlobalEvents.onNewDay += HandleNewDay;
        }

        private void SubscribeToIrsEntry()
        {
            GlobalEvents.onEnterBuilding -= HandleEnterBuilding;
            GlobalEvents.onEnterBuilding += HandleEnterBuilding;
            GlobalEvents.onExitBuilding -= HandleExitBuilding;
            GlobalEvents.onExitBuilding += HandleExitBuilding;
        }

        private void HandleEnterBuilding(Address address)
        {
            if (activationPromptVisible || IsActivatedForCurrentSave() || !IsIrs(address))
                return;

            var heldItem = PlayerHelper.ItemInstanceInHands;
            if (heldItem?.ItemCached == null || !heldItem.ItemCached.HasTag(TagRef.Itemtag.isfuelcontainer))
                return;

            activationPromptVisible = true;
            activationOverlay = new IrsActivationOverlay(this);
            OverlayUI.Show(activationOverlay);
        }

        private void HandleExitBuilding(Address address)
        {
            if (IsIrs(address))
                HideActivationOverlay();
        }

        private static bool IsIrs(Address address)
        {
            return address == TaxHelper.GetIRSAddress();
        }

        private void ActivateFromOverlay()
        {
            HideActivationOverlay();

            var save = SaveGameManager.Current;
            if (save == null || !ConsumeHeldFuelContainer())
                return;

            save.modData ??= new Dictionary<string, string>();
            save.modData[ActivatedModDataKey] = "1";
            CleanUpTaxLiabilities();
            SubscribeToNewDay();
            GameEvent.Invoke(string.Empty);
            ShowActivationConfirmation();
            context?.Logger.Info("The IRS was disabled for the current save.");
        }

        private void HideActivationOverlay()
        {
            activationPromptVisible = false;
            if (activationOverlay != null)
                OverlayUI.Hide(activationOverlay);

            activationOverlay = null;
        }

        private static bool ConsumeHeldFuelContainer()
        {
            var heldItem = PlayerHelper.ItemInstanceInHands;
            if (heldItem?.ItemCached == null || !heldItem.ItemCached.HasTag(TagRef.Itemtag.isfuelcontainer))
                return false;

            // Assigning null invokes PlayerHelper.RemoveItemsFromHands(), which
            // cleanly detaches the item from the player and HUD. Do not invoke
            // OnItemInHandsCargoUpdated afterwards: that routine expects a held
            // item and dereferences it while checking container tags.
            PlayerHelper.ItemInstanceInHands = null;
            return true;
        }

        private static void ShowActivationConfirmation()
        {
            try
            {
                Notifications.Show(
                    NotificationType.Success,
                    "goodbye-irs:activation_success",
                    null,
                    7f,
                    "GoodbyeIRSActivated",
                    null,
                    true,
                    false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Goodbye IRS could not show its activation confirmation: {exception.Message}");
            }
        }

        private static bool IsActivatedForCurrentSave()
        {
            var save = SaveGameManager.Current;
            return save?.modData != null &&
                   save.modData.TryGetValue(ActivatedModDataKey, out var value) &&
                   value == "1";
        }

        private sealed class IrsActivationOverlay : IOverlay
        {
            private readonly GoodbyeIrsRuntime runtime;

            public IrsActivationOverlay(GoodbyeIrsRuntime runtime)
            {
                this.runtime = runtime;
            }

            public Vector3 GetTargetPosition()
            {
                var player = InstanceBehavior<GameManager>.Instance?.playerController;
                return player != null ? player.transform.position + Vector3.up * 1.8f : Vector3.zero;
            }

            public LabelInfo GetFirstLineLabel() => new("goodbye-irs:activation_overlay_title");

            public LabelInfo GetSecondLineLeftLabel() => new("goodbye-irs:activation_overlay_description");

            public LabelInfo GetSecondLineRightLabel() => null!;

            public ButtonInfo[] GetButtons()
            {
                return new[]
                {
                    new ButtonInfo(
                        "GoodbyeIRSActivate",
                        "goodbye-irs:activation_confirm",
                        "red",
                        runtime.ActivateFromOverlay,
                        PlayerAction.Interact)
                };
            }
        }

        private void HandleNewDay()
        {
            if (!IsActivatedForCurrentSave())
                return;

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
