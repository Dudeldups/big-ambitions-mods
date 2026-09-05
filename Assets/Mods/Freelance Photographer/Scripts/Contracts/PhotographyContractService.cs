#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FreelancePhotographer
{
    internal sealed class PhotographyContractService
    {
        private readonly PhotographyContractCatalogData catalog;

        internal PhotographyContractService(PhotographyContractCatalogData catalog)
        {
            this.catalog = catalog;
        }

        internal PhotographySaveState? State => PhotographySaveService.Load();

        internal bool Tick(PhotographyEquipmentSnapshot equipment)
        {
            var save = SaveGameManager.Current;
            var state = State;
            if (save == null || state == null)
                return false;

            var now = PhotographySaveService.CurrentGameHours();
            var changed = state.availableContracts.RemoveAll(contract => contract == null || contract.availableUntil <= now) > 0;

            if (state.activeContract != null && state.activeContract.acceptedUntil > 0d && state.activeContract.acceptedUntil <= now)
            {
                state.activeContract = null;
                changed = true;
            }

            if (state.lastContractRefreshDay != save.Day)
            {
                state.lastContractRefreshDay = save.Day;
                GenerateDailyContracts(state, equipment, now, save.Day);
                changed = true;
            }

            if (changed)
                PhotographySaveService.Save(state);

            return changed;
        }

        internal bool Accept(string contractId)
        {
            var state = State;
            if (state == null || state.activeContract != null)
                return false;

            var contract = state.availableContracts.FirstOrDefault(value =>
                value != null && string.Equals(value.id, contractId, StringComparison.Ordinal));
            if (contract == null || contract.availableUntil <= PhotographySaveService.CurrentGameHours())
                return false;

            state.availableContracts.Remove(contract);
            contract.acceptedUntil = PhotographySaveService.CurrentGameHours() + catalog.acceptedContractDays * 24d;
            state.activeContract = contract;
            if (!string.IsNullOrWhiteSpace(contract.targetStreet))
            {
                state.recentTargets.RemoveAll(value => string.Equals(value, contract.TargetKey, StringComparison.OrdinalIgnoreCase));
                state.recentTargets.Add(contract.TargetKey);
                while (state.recentTargets.Count > 8)
                    state.recentTargets.RemoveAt(0);
            }

            PhotographySaveService.Save(state);
            return true;
        }

        internal void RecordCapture(PhotographyShotResult shot)
        {
            var state = State;
            var contract = state?.activeContract;
            if (state == null || contract == null || !shot.IsValid)
                return;

            contract.hasCapturedShot = true;
            contract.capturedQuality = shot.Quality;
            contract.framingScore = shot.Framing;
            contract.distanceScore = shot.Distance;
            contract.visibilityScore = shot.Visibility;
            contract.equipmentScore = shot.Equipment;
            contract.timingScore = shot.Timing;
            contract.bonusScore = shot.Bonus;
            PhotographySaveService.Save(state);
        }

        internal void Retake()
        {
            var state = State;
            var contract = state?.activeContract;
            if (state == null || contract == null)
                return;

            contract.hasCapturedShot = false;
            contract.capturedQuality = 0;
            contract.framingScore = 0;
            contract.distanceScore = 0;
            contract.visibilityScore = 0;
            contract.equipmentScore = 0;
            contract.timingScore = 0;
            contract.bonusScore = 0;
            PhotographySaveService.Save(state);
        }

        internal int Submit()
        {
            var save = SaveGameManager.Current;
            var state = State;
            var contract = state?.activeContract;
            if (save == null || state == null || contract == null || !contract.hasCapturedShot)
                return 0;

            var payout = Mathf.RoundToInt(contract.basePayout * GetQualityMultiplier(contract.capturedQuality));
            save.Money += payout;
            state.xp += 25 + contract.capturedQuality / 2;
            state.reputation = Mathf.Clamp(state.reputation + 2 + contract.capturedQuality / 20, 0, 100);
            state.completedContracts++;
            state.lifetimeIncome += payout;
            state.activeContract = null;
            PhotographySaveService.Save(state);
            return payout;
        }

        private void GenerateDailyContracts(
            PhotographySaveState state,
            PhotographyEquipmentSnapshot equipment,
            double now,
            int day)
        {
            state.availableContracts.Clear();
            var usableTier = Math.Max(1, equipment.CameraTier);
            var eligibleDefinitions = catalog.definitions
                .Where(definition => definition != null &&
                                     definition.minimumLevel <= state.Level &&
                                     definition.requiredTier <= usableTier)
                .ToList();
            if (eligibleDefinitions.Count == 0)
                return;

            var random = new System.Random(unchecked(day * 7919 + state.completedContracts * 104729 + 17));
            var usedTargets = new HashSet<string>(state.recentTargets, StringComparer.OrdinalIgnoreCase);
            var targetCount = Mathf.Clamp(catalog.availableContractCount, 3, 5);
            var attempts = 0;
            while (state.availableContracts.Count < targetCount && attempts++ < targetCount * 8)
            {
                var definition = eligibleDefinitions[random.Next(eligibleDefinitions.Count)];
                if (!TryCreateContract(definition, random, usedTargets, now, out var contract))
                    continue;

                state.availableContracts.Add(contract);
                if (!string.IsNullOrWhiteSpace(contract.targetStreet))
                    usedTargets.Add(contract.TargetKey);
            }
        }

        private bool TryCreateContract(
            PhotographyContractDefinition definition,
            System.Random random,
            HashSet<string> usedTargets,
            double now,
            out PhotographyContractInstance contract)
        {
            contract = new PhotographyContractInstance
            {
                id = Guid.NewGuid().ToString("N"),
                definitionId = definition.id,
                category = definition.category,
                titleKey = definition.titleKey,
                descriptionKey = definition.descriptionKey,
                requiredTier = definition.requiredTier,
                requiredAccessory = definition.requiredAccessory,
                minimumDistance = definition.minimumDistance,
                idealDistanceMinimum = definition.idealDistanceMinimum,
                idealDistanceMaximum = definition.idealDistanceMaximum,
                maximumDistance = definition.maximumDistance,
                requiredSubjectCount = definition.requiredSubjectCount,
                basePayout = random.Next(definition.minimumPayout, definition.maximumPayout + 1),
                availableUntil = now + random.Next(
                    Math.Max(1, catalog.minimumAvailableDays),
                    Math.Max(catalog.minimumAvailableDays + 1, catalog.maximumAvailableDays + 1)) * 24d
            };

            if (definition.category != PhotographyCategory.Location &&
                definition.category != PhotographyCategory.Business)
            {
                contract.targetDisplayName = definition.category == PhotographyCategory.Vehicle
                    ? "freelancephotographer:target_any_vehicle"
                    : "freelancephotographer:target_three_pedestrians";
                return true;
            }

            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return false;

            var candidates = registrations
                .Where(registration => registration != null &&
                                       registration.HasValidAddress &&
                                       !registration.RentedByPlayer &&
                                       (definition.category != PhotographyCategory.Business ||
                                        !string.IsNullOrWhiteSpace(registration.BusinessName)))
                .Where(registration => !usedTargets.Contains(registration.StreetName + ":" + registration.StreetNumber))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = registrations
                    .Where(registration => registration != null &&
                                           registration.HasValidAddress &&
                                           !registration.RentedByPlayer &&
                                           (definition.category != PhotographyCategory.Business ||
                                            !string.IsNullOrWhiteSpace(registration.BusinessName)))
                    .ToList();
            }

            if (candidates.Count == 0)
                return false;

            var selected = candidates[random.Next(candidates.Count)];
            contract.targetStreet = selected.StreetName;
            contract.targetNumber = selected.StreetNumber;
            contract.targetDisplayName = selected.GetDisplayName();
            return true;
        }

        internal static float GetQualityMultiplier(int quality)
        {
            if (quality >= 90)
                return 1.5f;
            if (quality >= 80)
                return 1.2f;
            if (quality >= 65)
                return 1f;
            if (quality >= 50)
                return 0.75f;
            return 0.5f;
        }
    }
}
