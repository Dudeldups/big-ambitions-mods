#nullable enable
using System.Collections.Generic;
using Buildings;
using Dialogs;
using Helpers;
using SpecialServices.Bank;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxLoanLimitService
    {
        private const int VantanderDefaultMaximumLoanAmount = 800000;

        private readonly List<PatchedSettingsState> patchedStates = new List<PatchedSettingsState>();

        public void InvalidateCache()
        {
        }

        public void ApplyConfiguredLimit(BigHaxSettings settings)
        {
            if (!TryGetCurrentBankSettings(out var bankSettings, out var building, out var currentValue))
                return;

            var state = GetOrCreateState(bankSettings, currentValue);
            var isVantander = state.OriginalValue == VantanderDefaultMaximumLoanAmount ||
                              Mathf.Approximately(currentValue, VantanderDefaultMaximumLoanAmount);

            if (!isVantander)
                return;

            if (settings.EnableVantanderMaxLoanOverride)
            {
                if (!Mathf.Approximately(currentValue, BigHaxSettings.VantanderMaximumLoanOverrideAmount))
                    bankSettings.maxTotalLoanAmount = BigHaxSettings.VantanderMaximumLoanOverrideAmount;

                return;
            }

            if (!Mathf.Approximately(bankSettings.maxTotalLoanAmount, state.OriginalValue))
                state.Restore();
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

            try
            {
                building = BuildingHelper.GetBuilding(dialogController.contact.Address);
            }
            catch
            {
                return false;
            }

            if (building == null || building.SpecialService == null)
                return false;

            if (building.SpecialService.settings is not BankSettings resolvedSettings)
                return false;

            bankSettings = resolvedSettings;
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
    }
}
