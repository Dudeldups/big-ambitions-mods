#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Entities;

namespace BigHax
{
    /// <summary>
    /// Detects new delivery orders on the game's hourly event. No per-frame polling
    /// is used: an order is made due at the next game hour and completed by the
    /// game's existing delivery routines.
    /// </summary>
    internal sealed class BigHaxInstantDeliveryService
    {
        private readonly Dictionary<ImportPartnership, ImportState> knownImportStates = new Dictionary<ImportPartnership, ImportState>();
        private readonly HashSet<FurnitureDeliveryContract> knownFurnitureContracts = new HashSet<FurnitureDeliveryContract>();
        private readonly HashSet<ImportPartnership> recentlyForcedImports = new HashSet<ImportPartnership>();
        private readonly List<ImportPartnership> pendingImports = new List<ImportPartnership>();
        private readonly List<FurnitureDeliveryContract> pendingFurnitureDeliveries = new List<FurnitureDeliveryContract>();

        private bool instantImportsEnabled;
        private bool instantFurnitureDeliveriesEnabled;

        public bool ShouldProcessHourly => instantImportsEnabled || instantFurnitureDeliveriesEnabled;

        public void ApplyConfiguredBehavior(BigHaxSettings settings)
        {
            var importsChanged = instantImportsEnabled != settings.EnableInstantImports;
            var furnitureChanged = instantFurnitureDeliveriesEnabled != settings.EnableInstantFurnitureDeliveries;
            instantImportsEnabled = settings.EnableInstantImports;
            instantFurnitureDeliveriesEnabled = settings.EnableInstantFurnitureDeliveries;

            if (!instantImportsEnabled)
            {
                knownImportStates.Clear();
                recentlyForcedImports.Clear();
                pendingImports.Clear();
            }

            if (!instantFurnitureDeliveriesEnabled)
            {
                knownFurnitureContracts.Clear();
                pendingFurnitureDeliveries.Clear();
            }

            if (importsChanged || furnitureChanged)
            {
                BigHaxLogger.Diagnostic(
                    "Instant deliveries configured: imports=" + instantImportsEnabled +
                    ", furniture=" + instantFurnitureDeliveriesEnabled + ".");
            }
        }

        public void InvalidateCache()
        {
            knownImportStates.Clear();
            knownFurnitureContracts.Clear();
            recentlyForcedImports.Clear();
            pendingImports.Clear();
            pendingFurnitureDeliveries.Clear();
        }

        public void CaptureNewOrdersBeforeHourlyDelivery()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            var detectedImports = 0;
            var detectedFurnitureDeliveries = 0;

            if (instantImportsEnabled && saveGame.importPartnerships != null)
            {
                foreach (var partnership in saveGame.importPartnerships)
                {
                    if (partnership == null || !partnership.isActive)
                        continue;

                    var becameActive = !knownImportStates.TryGetValue(partnership, out var previousState) ||
                        !previousState.IsActive ||
                        previousState.NextDeliveryDay != partnership.nextDeliveryDay;
                    if (!becameActive)
                        continue;

                    // During accelerated time several game hours can be processed in
                    // one frame. A forced delivery may have already been completed by
                    // vanilla before this handler gets its one-frame completion pass.
                    if (recentlyForcedImports.Remove(partnership))
                    {
                        knownImportStates[partnership] = new ImportState(partnership.isActive, partnership.nextDeliveryDay);
                        continue;
                    }

                    if (!pendingImports.Contains(partnership))
                    {
                        pendingImports.Add(partnership);
                        detectedImports++;
                    }

                    partnership.nextDeliveryDay = saveGame.Day;
                    knownImportStates[partnership] = new ImportState(partnership.isActive, partnership.nextDeliveryDay);
                    recentlyForcedImports.Add(partnership);
                }
            }

            if (instantFurnitureDeliveriesEnabled && saveGame.FurnitureDeliveryContracts != null)
            {
                foreach (var contract in saveGame.FurnitureDeliveryContracts)
                {
                    if (contract == null || knownFurnitureContracts.Contains(contract))
                        continue;

                    if (!pendingFurnitureDeliveries.Contains(contract))
                    {
                        pendingFurnitureDeliveries.Add(contract);
                        detectedFurnitureDeliveries++;
                    }
                    contract.dayOfDelivery = saveGame.Day;
                    contract.hourOfDelivery = saveGame.Hour;
                }
            }

            if (detectedImports > 0 || detectedFurnitureDeliveries > 0)
            {
                BigHaxLogger.Diagnostic(
                    "Instant deliveries detected new orders: imports=" + detectedImports +
                    ", furniture=" + detectedFurnitureDeliveries +
                    ", day=" + saveGame.Day + ", hour=" + saveGame.Hour + ".");
                SaveGameManager.MarkChange();
            }
        }

        public IEnumerator CompletePendingOrdersAfterHourlyDelivery()
        {
            // GlobalEvents.onNewHour runs before the game's hourly delivery methods.
            // Wait one frame so Monday's normal delivery reset and any furniture
            // delivery both finish before we complete imports outside regular hours.
            yield return null;

            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
            {
                pendingImports.Clear();
                pendingFurnitureDeliveries.Clear();
                yield break;
            }

            try
            {
                var importsCompletedByVanilla = 0;
                var importsStillDue = 0;
                foreach (var partnership in pendingImports)
                {
                    if (partnership == null)
                        continue;

                    if (!partnership.isActive || partnership.nextDeliveryDay > saveGame.Day)
                        importsCompletedByVanilla++;
                    else
                        importsStillDue++;
                }

                if (importsStillDue > 0)
                    ImportPartnership.DoAllDeliveries();

                var importsRemainingAfterCompletion = 0;
                foreach (var partnership in pendingImports)
                {
                    if (partnership != null && partnership.isActive && partnership.nextDeliveryDay <= saveGame.Day)
                        importsRemainingAfterCompletion++;
                }
                var importsCompletedAfterHourly = importsStillDue - importsRemainingAfterCompletion;

                var furnitureDelivered = 0;
                foreach (var contract in pendingFurnitureDeliveries)
                {
                    if (contract == null || saveGame.FurnitureDeliveryContracts == null || !saveGame.FurnitureDeliveryContracts.Contains(contract))
                        furnitureDelivered++;
                }

                if (pendingImports.Count > 0 || pendingFurnitureDeliveries.Count > 0)
                {
                    BigHaxLogger.Diagnostic(
                        "Instant deliveries completed: importsDetected=" + pendingImports.Count +
                        ", importsCompletedByVanilla=" + importsCompletedByVanilla +
                        ", importsCompletedAfterHourly=" + importsCompletedAfterHourly +
                        ", importsRemainingAfterCompletion=" + importsRemainingAfterCompletion +
                        ", furnitureDetected=" + pendingFurnitureDeliveries.Count +
                        ", furnitureDelivered=" + furnitureDelivered + ".");
                }
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException("Instant deliveries completion", exception);
            }
            finally
            {
                RefreshKnownOrders(saveGame);
                pendingImports.Clear();
                pendingFurnitureDeliveries.Clear();
            }
        }

        private void RefreshKnownOrders(GameInstance saveGame)
        {
            knownImportStates.Clear();
            recentlyForcedImports.Clear();
            if (instantImportsEnabled && saveGame.importPartnerships != null)
            {
                foreach (var partnership in saveGame.importPartnerships)
                {
                    if (partnership != null)
                        knownImportStates[partnership] = new ImportState(partnership.isActive, partnership.nextDeliveryDay);
                }
            }

            knownFurnitureContracts.Clear();
            if (instantFurnitureDeliveriesEnabled && saveGame.FurnitureDeliveryContracts != null)
            {
                foreach (var contract in saveGame.FurnitureDeliveryContracts)
                {
                    if (contract != null)
                        knownFurnitureContracts.Add(contract);
                }
            }
        }

        private readonly struct ImportState
        {
            public ImportState(bool isActive, int nextDeliveryDay)
            {
                IsActive = isActive;
                NextDeliveryDay = nextDeliveryDay;
            }

            public bool IsActive { get; }
            public int NextDeliveryDay { get; }
        }
    }
}
