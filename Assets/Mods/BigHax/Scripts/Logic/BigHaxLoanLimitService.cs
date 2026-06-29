#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Buildings;
using Dialogs;
using Helpers;
using SpecialServices.Bank;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxLoanLimitService
    {
        private const string DebugLogFileName = "bighax-loan-debug.log";
        private const string DebugFallbackFileName = "bighax-loan-debug.log";
        private const int VantanderDefaultMaximumLoanAmount = 800000;

        private readonly List<PatchedSettingsState> patchedStates = new List<PatchedSettingsState>();
        private string? lastObservedSignature;

        public void InvalidateCache()
        {
            lastObservedSignature = null;
        }

        public void ApplyConfiguredLimit(BigHaxSettings settings)
        {
            if (!TryGetCurrentBankSettings(out var bankSettings, out var building, out var currentValue))
                return;

            var state = GetOrCreateState(bankSettings, currentValue);
            var isVantander = state.OriginalValue == VantanderDefaultMaximumLoanAmount ||
                              Mathf.Approximately(currentValue, VantanderDefaultMaximumLoanAmount);

            var signature = building.Address + "|" + currentValue + "|" + isVantander;
            if (!string.Equals(signature, lastObservedSignature, StringComparison.Ordinal))
            {
                LogDebug(
                    $"Observed bank loan settings: building={building.StreetName} {building.StreetNumber}, currentMaxTotalLoanAmount={currentValue}, originalMaxTotalLoanAmount={state.OriginalValue}, isVantander={isVantander}.");
                lastObservedSignature = signature;
            }

            if (!isVantander)
                return;

            if (settings.EnableVantanderMaxLoanOverride)
            {
                if (!Mathf.Approximately(currentValue, BigHaxSettings.VantanderMaximumLoanOverrideAmount))
                {
                    bankSettings.maxTotalLoanAmount = BigHaxSettings.VantanderMaximumLoanOverrideAmount;
                    LogDebug(
                        $"Applied Vantander max loan override: building={building.StreetName} {building.StreetNumber}, {currentValue} -> {bankSettings.maxTotalLoanAmount}.");
                }

                return;
            }

            if (!Mathf.Approximately(bankSettings.maxTotalLoanAmount, state.OriginalValue))
            {
                var previousValue = bankSettings.maxTotalLoanAmount;
                state.Restore();
                LogDebug(
                    $"Restored Vantander max loan override: building={building.StreetName} {building.StreetNumber}, {previousValue} -> {bankSettings.maxTotalLoanAmount}.");
            }
        }

        public void RestoreOriginalLimit()
        {
            foreach (var state in patchedStates)
                state.Restore();
        }

        private PatchedSettingsState GetOrCreateState(BankSettings bankSettings, float currentValue)
        {
            foreach (var state in patchedStates)
            {
                if (ReferenceEquals(state.Settings, bankSettings))
                    return state;
            }

            var createdState = new PatchedSettingsState(bankSettings, currentValue);
            patchedStates.Add(createdState);
            return createdState;
        }

        private static bool TryGetCurrentBankSettings(out BankSettings bankSettings, out Building building, out float currentValue)
        {
            bankSettings = null!;
            building = null!;
            currentValue = 0f;

            var dialogController = DialogController.current;
            if (dialogController == null || dialogController.contact == null)
                return false;

            Building? resolvedBuilding;
            try
            {
                resolvedBuilding = BuildingHelper.GetBuilding(dialogController.contact.Address);
            }
            catch (Exception exception)
            {
                LogDebug($"Failed to resolve bank building from current dialog contact: {exception.GetType().Name}.");
                return false;
            }

            if (resolvedBuilding == null || resolvedBuilding.SpecialService == null)
                return false;

            if (resolvedBuilding.SpecialService.settings is not BankSettings resolvedSettings)
                return false;

            bankSettings = resolvedSettings;
            building = resolvedBuilding;
            currentValue = resolvedSettings.maxTotalLoanAmount;
            return true;
        }

        private sealed class PatchedSettingsState
        {
            public PatchedSettingsState(BankSettings settings, float originalValue)
            {
                Settings = settings;
                OriginalValue = originalValue;
            }

            public BankSettings Settings { get; }
            public float OriginalValue { get; }

            public void Restore()
            {
                Settings.maxTotalLoanAmount = OriginalValue;
            }
        }

        private static void LogDebug(string message)
        {
            BigHaxFileLogger.Log(DebugLogFileName, DebugFallbackFileName, "[Loan] " + message);
        }
    }
}
