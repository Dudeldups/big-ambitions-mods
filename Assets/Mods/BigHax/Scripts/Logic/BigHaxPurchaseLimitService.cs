#nullable enable
using Entities;

namespace BigHax
{
    internal sealed class BigHaxPurchaseLimitService
    {
        public bool AreLimitsDisabled()
        {
            return SaveGameManager.Current?.gameVariables?.disableWholesaleAndImportLimits == true;
        }

        public void SetLimitsDisabled(bool disabled)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.gameVariables == null)
            {
                BigHaxLogger.PurchaseLimitDiagnostic(
                    "Change skipped: requestedDisabled=" + disabled + ", no active save game.");
                return;
            }

            var before = saveGame.gameVariables.disableWholesaleAndImportLimits;
            saveGame.gameVariables.disableWholesaleAndImportLimits = disabled;
            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();

            var after = saveGame.gameVariables.disableWholesaleAndImportLimits;
            var helperResult = DeliveryHelper.AreWholesaleAndImportLimitsDisabled();
            BigHaxLogger.PurchaseLimitDiagnostic(
                "Change completed: requestedDisabled=" + disabled +
                ", before=" + before +
                ", after=" + after +
                ", deliveryHelperResult=" + helperResult +
                ", saveMarkedChanged=true.");
        }
    }
}
