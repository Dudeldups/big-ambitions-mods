using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Buildings;
using Helpers;
using Localizor;
using UnityEngine;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private const float ApartmentEntryTransitionTimeoutSeconds = 8f;
        private const float ApartmentReturnOutdoorConfirmationSeconds = 1.25f;
        private const float ApartmentOutdoorReturnIgnoreAfterEnterSeconds = 2.0f;
        private const float ApartmentReloadReentryDelaySeconds = 1.5f;
        private const float ApartmentReloadReentryIndoorSettleSeconds = 0.75f;
        private const float ApartmentReloadNpcRespawnInitialDelaySeconds = 0.05f;
        private const float ApartmentReloadNpcRespawnRetryDelaySeconds = 0.25f;
        private const int ApartmentReloadNpcRespawnMaxAttempts = 1;

        private static readonly string[] ApartmentRegistrationFieldNames =
        {
            "Layout",
            "interiorDesigns",
            "itemInstances",
            "itemsInBuilding",
            "deliveredItems",
            "dirtSpots"
        };

        private static readonly Dictionary<string, StreetQuestApartmentInteriorPayload> ApartmentPayloadCacheByVisitKey =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, StreetQuestApartmentRegistrationSnapshot> ApartmentOriginalSnapshotCacheByVisitKey =
            new(StringComparer.OrdinalIgnoreCase);

        private static StreetQuestApartmentVisitContext ActiveApartmentVisit;
        private static string LastApartmentResumeAttemptAddress = string.Empty;

        internal static bool IsApartmentVisitContextActiveFor(string characterId)
        {
            return ActiveApartmentVisit != null &&
                   ActiveApartmentVisit.State == StreetQuestApartmentVisitState.ActiveInside &&
                   string.Equals(ActiveApartmentVisit.CharacterId, characterId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldPreserveIndoorAddressForActiveApartmentVisit(string addressKey)
        {
            var visit = ActiveApartmentVisit;
            if (visit == null || visit.State != StreetQuestApartmentVisitState.ActiveInside)
                return false;

            var normalizedAddress = NormalizeAddressKey(addressKey);
            if (string.IsNullOrWhiteSpace(normalizedAddress))
                return false;

            if (!string.Equals(
                    NormalizeAddressKey(visit.ExteriorAddress),
                    normalizedAddress,
                    StringComparison.Ordinal))
            {
                return false;
            }

            // During save/load or scene handoff the game can briefly report an outdoor context
            // while the player is still effectively saved inside the routed apartment. While the
            // visit is active, the routed apartment address is authoritative; only the apartment
            // visit tracker may clear it after a confirmed exterior return.
            return true;
        }

        internal static bool TryEnterApartment(StreetQuestShared.ApartmentEntryOption option)
        {
            if (option == null)
                return false;

            if (ActiveApartmentVisit != null &&
                TryRestoreActiveApartmentVisitForReentry(option, out var reentryRestoreReason))
            {
                LogDebug(
                    $"ApartmentEntryActiveVisitClearedForReentry character={option.CharacterId} state={option.StateId} reason={reentryRestoreReason}");
            }

            if (ActiveApartmentVisit != null)
            {
                LogDebug(
                    $"ApartmentEntryFailed reason=visit_already_active character={option.CharacterId} state={option.StateId} activeKey={ActiveApartmentVisit.VisitKey}");
                NotifyInfo(
                    "streetquest:apartment_entry_failed".Localize(new Dictionary<string, string>
                    {
                        { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
                    }).ToString(),
                    $"streetquest:apartment_entry_failed:{option.CharacterId}",
                    3.5f);
                return false;
            }

            LogDebug(
                $"ApartmentEntryResolveStart character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress}");

            if (!TryResolveApartmentVisitTarget(option, out var building, out var registration, out var parsedAddress, out var failureReason))
            {
                LogDebug(
                    $"ApartmentEntryFailed reason={failureReason} character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress}");
                NotifyInfo(
                    "streetquest:apartment_entry_failed".Localize(new Dictionary<string, string>
                    {
                        { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
                    }).ToString(),
                    $"streetquest:apartment_entry_failed:{option.CharacterId}",
                    3.5f);
                return false;
            }

            var visitContext = CreateApartmentVisitContext(option, building, registration);
            TrySuppressPreEntryApartmentItems(visitContext);

            if (!TryStartVanillaApartmentEntry(building, out var route))
            {
                LogDebug(
                    $"ApartmentEntryFailed reason=start_vanilla_entry_failed character={option.CharacterId} state={option.StateId} exteriorAddress={option.ExteriorAddress}");
                RestoreApartmentVisitContext(visitContext, "entry_start_failed", clearActiveVisit: false);
                NotifyInfo(
                    "streetquest:apartment_entry_failed".Localize(new Dictionary<string, string>
                    {
                        { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
                    }).ToString(),
                    $"streetquest:apartment_entry_failed:{option.CharacterId}",
                    3.5f);
                return false;
            }

            visitContext.EntryStartedAtSeconds = Time.unscaledTime;
            visitContext.EntryRoute = route;
            visitContext.State = StreetQuestApartmentVisitState.WaitingForIndoorTransition;
            ActiveApartmentVisit = visitContext;
            LastApartmentReturnDeferredLogKey = string.Empty;
            SetPersistedApartmentVisit(visitContext.CharacterId, visitContext.StateId, visitContext.ExteriorAddress);

            LogDebug(
                $"ApartmentEntryStarted character={option.CharacterId} state={option.StateId} address={option.ExteriorAddress} route={route} building={DescribeObject(building)} registration={DescribeObject(registration)}");
            return true;
        }

        private static bool TryRestoreActiveApartmentVisitForReentry(
            ApartmentEntryOption option,
            out string reason)
        {
            reason = string.Empty;

            var visit = ActiveApartmentVisit;
            if (visit == null)
                return true;

            if (option == null)
            {
                reason = "missing_option";
                return false;
            }

            if (visit.State != StreetQuestApartmentVisitState.ActiveInside)
            {
                reason = "active_visit_not_inside";
                return false;
            }

            if (IsIndoorGameplayContextActive())
            {
                reason = "still_indoor";
                return false;
            }

            if (!string.Equals(visit.CharacterId ?? string.Empty, option.CharacterId ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(visit.StateId ?? string.Empty, option.StateId ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    NormalizeAddressKey(visit.ExteriorAddress),
                    NormalizeAddressKey(option.ExteriorAddress),
                    StringComparison.Ordinal))
            {
                reason = "different_visit";
                return false;
            }

            // A real click on the same routed apartment door is stronger evidence than the
            // delayed outdoor-return confirmation timer. Without this, leaving after a reload
            // keeps the old apartment visit active for a few seconds and blocks immediate
            // re-entry with the generic failure popup.
            reason = "same_apartment_door_reentry";
            ClearPersistedApartmentVisit();
            SetCurrentIndoorBuildingAddressKey(string.Empty);
            RestoreActiveApartmentVisit(reason);
            return ActiveApartmentVisit == null;
        }

        internal static void TickApartmentVisit(float elapsedSeconds)
        {
            var visit = ActiveApartmentVisit;
            if (visit == null)
            {
                TryResumeApartmentVisitForLoadedIndoorState();
                return;
            }

            if (visit.State == StreetQuestApartmentVisitState.WaitingForIndoorTransition)
            {
                if (visit.ReloadReentryPending)
                {
                    if (!visit.ReloadReentryStarted)
                    {
                        if (Time.unscaledTime < visit.ReloadReentryDueAtSeconds)
                            return;

                        if (IsRuntimeShutdownInProgress())
                            return;

                        if (TryStartReloadApartmentReentry(visit))
                            return;

                        visit.ReloadReentryPending = false;
                    }
                    else if (Time.unscaledTime - visit.EntryStartedAtSeconds < ApartmentReloadReentryIndoorSettleSeconds)
                    {
                        return;
                    }
                }

                if (IsIndoorGameplayContextActive())
                {
                    if (!visit.PayloadAppliedInside)
                    {
                        if (!visit.PayloadSeededBeforeIndoor)
                            ApplyApartmentPayload(visit);
                        else
                            LogDebug($"ApartmentPayloadApplied key={visit.VisitKey} mode=seeded_before_indoor_reused interiorDesigns={DescribeValueShape(GetMemberValue(visit.Registration, "interiorDesigns"))} itemInstances={DescribeValueShape(GetMemberValue(visit.Registration, "itemInstances"))} itemsInBuilding={DescribeValueShape(GetMemberValue(visit.Registration, "itemsInBuilding"))}");

                        if (!visit.PayloadSeededBeforeIndoor)
                            TryApplyRuntimeApartmentLayout(visit);
                        else
                            LogDebug($"ApartmentRuntimeLayoutSkipped key={visit.VisitKey} reason=payload_seeded_before_indoor_reload");

                        visit.PayloadAppliedInside = true;
                    }

                    visit.State = StreetQuestApartmentVisitState.ActiveInside;
                    visit.ActiveInsideStartedAtSeconds = Time.unscaledTime;
                    ClearApartmentOutdoorReturnCandidate(visit);
                    PrepareApartmentVisitCharacterSpawn(visit, "entered");
                    RefreshSpawnedCharacters();
                    ScheduleApartmentVisitCharacterRespawnIfNeeded(visit, "entered");
                    LogDebug(
                        $"ApartmentVisitEntered character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} route={visit.EntryRoute}");
                    return;
                }

                if (Time.unscaledTime - visit.EntryStartedAtSeconds >= ApartmentEntryTransitionTimeoutSeconds)
                {
                    LogDebug(
                        $"ApartmentVisitTransitionTimeout character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} route={visit.EntryRoute}");
                    RestoreActiveApartmentVisit("transition_timeout");
                }

                return;
            }

            if (visit.State == StreetQuestApartmentVisitState.ActiveInside &&
                !visit.ApartmentItemsProtected &&
                visit.ActivePayload?.IsCustomLayoutPayload == true &&
                Time.unscaledTime - visit.EntryStartedAtSeconds >= 0.45f)
            {
                TryProtectApartmentItems(visit);
            }

            if (visit.State == StreetQuestApartmentVisitState.ActiveInside)
                TickApartmentVisitCharacterRespawn(visit);

            if (visit.State == StreetQuestApartmentVisitState.ActiveInside &&
                !IsIndoorGameplayContextActive())
            {
                if (IsRuntimeShutdownInProgress())
                {
                    ClearApartmentOutdoorReturnCandidate(visit);
                    LogDebug(
                        $"ApartmentVisitReturnSkipped character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} reason=runtime_shutdown");
                    return;
                }

                var visitExteriorAddress = NormalizeAddressKey(visit.ExteriorAddress);

                // Do not use the last exterior candidate blindly here. When saving/reloading inside
                // a routed apartment, the gameplay camera can briefly leave the IndoorCam context
                // while the exterior candidate is still the stale door candidate from the original
                // apartment entry. Force a fresh probe and require that fresh probe to resolve the
                // routed apartment's exterior address. If the fresh probe fails or points somewhere
                // else, keep the routed apartment context alive.
                if (!StreetQuestIndoorAddressTracker.TryForceResolveExteriorAddressCandidate(
                        elapsedSeconds,
                        out var freshlyResolvedExteriorAddress))
                {
                    if (HasApartmentOutdoorReturnCandidateExpired(visit))
                    {
                        ClearPersistedApartmentVisit();
                        SetCurrentIndoorBuildingAddressKey(string.Empty);
                        RestoreActiveApartmentVisit("returned_outdoor_after_pending_probe");
                        return;
                    }

                    LogApartmentVisitReturnDeferred(
                        visit,
                        string.IsNullOrWhiteSpace(visit.PendingOutdoorReturnAddress)
                            ? "awaiting_fresh_exterior_probe"
                            : $"confirming_return currentExterior={visit.PendingOutdoorReturnAddress} requiredSeconds={ApartmentReturnOutdoorConfirmationSeconds:0.##}");
                    return;
                }

                var currentExteriorAddress = NormalizeAddressKey(freshlyResolvedExteriorAddress);

                if (string.IsNullOrWhiteSpace(currentExteriorAddress) ||
                    !string.Equals(currentExteriorAddress, visitExteriorAddress, StringComparison.Ordinal))
                {
                    if (HasApartmentOutdoorReturnCandidateExpired(visit))
                    {
                        ClearPersistedApartmentVisit();
                        SetCurrentIndoorBuildingAddressKey(string.Empty);
                        RestoreActiveApartmentVisit("returned_outdoor_after_pending_mismatch");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(visit.PendingOutdoorReturnAddress))
                        ClearApartmentOutdoorReturnCandidate(visit);

                    LogApartmentVisitReturnDeferred(
                        visit,
                        $"awaiting_matching_exterior currentExterior={currentExteriorAddress}");
                    return;
                }

                var activeInsideAge = visit.ActiveInsideStartedAtSeconds >= 0f
                    ? Time.unscaledTime - visit.ActiveInsideStartedAtSeconds
                    : float.MaxValue;
                if (activeInsideAge < ApartmentOutdoorReturnIgnoreAfterEnterSeconds)
                {
                    LogApartmentVisitReturnDeferred(
                        visit,
                        $"ignoring_early_outdoor_flicker currentExterior={currentExteriorAddress} age={activeInsideAge:0.##}s");
                    return;
                }

                // A fresh exterior probe that matches the routed apartment door is strong enough
                // evidence that the player is really back outside. Restore immediately here.
                // Waiting for another confirmation tick can leave the NPC-apartment payload on the
                // vanilla BuildingRegistration long enough for the player's own apartment under the
                // same shell/address to enter with a poisoned registration and render black.
                ClearPersistedApartmentVisit();
                SetCurrentIndoorBuildingAddressKey(string.Empty);
                RestoreActiveApartmentVisit("returned_outdoor_fresh_match");
                return;
            }

            if (visit.State == StreetQuestApartmentVisitState.ActiveInside)
                ClearApartmentOutdoorReturnCandidate(visit);
        }

        private static void PrepareApartmentVisitCharacterSpawn(StreetQuestApartmentVisitContext visit, string reason)
        {
            if (visit == null)
                return;

            PreferredQuestGiverSpawnPosition = null;
            CachedItemsContainerTransform = null;
            StreetQuestCharacterRuntimeResolver.ClearCache();
            LogDebug(
                $"ApartmentVisitCharacterSpawnPrepared character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} reason={reason}");
        }

        private static void ScheduleApartmentVisitCharacterRespawnIfNeeded(StreetQuestApartmentVisitContext visit, string reason)
        {
            if (visit == null || string.IsNullOrWhiteSpace(visit.CharacterId))
                return;

            if (!visit.ReloadReentryStarted &&
                (visit.EntryRoute == null || visit.EntryRoute.IndexOf("reload", StringComparison.OrdinalIgnoreCase) < 0))
            {
                return;
            }

            visit.CharacterRespawnScheduled = true;
            visit.CharacterRespawnAttempts = 0;
            visit.CharacterRespawnDueAtSeconds = Time.unscaledTime + ApartmentReloadNpcRespawnInitialDelaySeconds;
            LogDebug(
                $"ApartmentVisitCharacterRespawnScheduled character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} reason={reason} dueSeconds={ApartmentReloadNpcRespawnInitialDelaySeconds:0.##}");
        }

        private static void TickApartmentVisitCharacterRespawn(StreetQuestApartmentVisitContext visit)
        {
            if (visit == null || !visit.CharacterRespawnScheduled)
                return;

            if (Time.unscaledTime < visit.CharacterRespawnDueAtSeconds)
                return;

            if (IsRuntimeShutdownInProgress())
                return;

            visit.CharacterRespawnAttempts++;
            var result = ForceRespawnApartmentVisitCharacter(visit.CharacterId, $"apartment_reload_settle_attempt_{visit.CharacterRespawnAttempts}");
            LogDebug(
                $"ApartmentVisitCharacterRespawnAttempt character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} attempt={visit.CharacterRespawnAttempts} result={result}");

            if (visit.CharacterRespawnAttempts >= ApartmentReloadNpcRespawnMaxAttempts)
            {
                visit.CharacterRespawnScheduled = false;
                return;
            }

            visit.CharacterRespawnDueAtSeconds = Time.unscaledTime + ApartmentReloadNpcRespawnRetryDelaySeconds;
        }

        internal static void RestoreActiveApartmentVisit(string reason)
        {
            if (ActiveApartmentVisit == null)
                return;

            RestoreApartmentVisitContext(ActiveApartmentVisit, reason, clearActiveVisit: true);
        }

        internal static void PrimeApartmentVisitFromPersistedIndoorState()
        {
            if (ActiveApartmentVisit != null)
                return;

            if (!TryGetPersistedApartmentVisit(out var persistedCharacterId, out var persistedStateId, out var indoorAddress))
                return;

            var option = GetAvailableApartmentEntryOptions(indoorAddress)
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    candidate.RequiresApartmentVisitContext &&
                    string.Equals(candidate.CharacterId, persistedCharacterId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.StateId, persistedStateId, StringComparison.OrdinalIgnoreCase));
            if (option == null)
            {
                LogDebug($"ApartmentVisitPrimeSkipped address={indoorAddress} character={persistedCharacterId} state={persistedStateId} reason=no_matching_routed_option");
                return;
            }

            if (!TryResolveApartmentVisitTarget(option, out var building, out var registration, out _, out var failureReason))
            {
                LogDebug(
                    $"ApartmentVisitPrimeFailed address={indoorAddress} character={option.CharacterId} state={option.StateId} reason={failureReason}");
                return;
            }

            var visitContext = CreateApartmentVisitContext(option, building, registration, preferCachedOriginalSnapshot: true);
            visitContext.EntryStartedAtSeconds = Time.unscaledTime;
            visitContext.EntryRoute = "prime_persisted_indoor_reentry_scheduled";
            visitContext.State = StreetQuestApartmentVisitState.WaitingForIndoorTransition;
            visitContext.ReloadReentryPending = true;
            visitContext.ReloadReentryDueAtSeconds = Time.unscaledTime + ApartmentReloadReentryDelaySeconds;

            // Reloading directly inside a routed NPC apartment can leave vanilla in a half-built
            // black indoor state. Instead of accepting that loaded indoor scene, schedule one
            // normal delayed apartment re-entry after the city runtime is alive. This deliberately
            // mimics the successful outside-door entry path: restore the original registration,
            // suppress pre-entry item instances, let vanilla enter the real building shell again,
            // and only then apply the routed NPC apartment payload/layout.
            SetCurrentIndoorBuildingAddressKey(indoorAddress);

            ActiveApartmentVisit = visitContext;
            LastApartmentReturnDeferredLogKey = string.Empty;

            LogDebug(
                $"ApartmentVisitPrimed address={indoorAddress} character={option.CharacterId} state={option.StateId} route={visitContext.EntryRoute} payload=deferred_reload_reentry dueSeconds={ApartmentReloadReentryDelaySeconds:0.##}");
        }

        internal static void HandleApartmentVisitRuntimeShutdown(string reason)
        {
            if (ActiveApartmentVisit == null)
                return;

            var visit = ActiveApartmentVisit;
            if (visit.State == StreetQuestApartmentVisitState.ActiveInside &&
                visit.ActivePayload?.IsCustomLayoutPayload == true)
            {
                if (!IsIndoorGameplayContextActive())
                {
                    ClearPersistedApartmentVisit();
                    SetCurrentIndoorBuildingAddressKey(string.Empty);
                    RestoreApartmentVisitContext(visit, reason + "_outside_restore", clearActiveVisit: true);
                    return;
                }

                // Even when the game/mod is shutting down while the player is still inside the
                // routed apartment, do not leave the temporary NPC payload on the shared vanilla
                // BuildingRegistration. The persisted apartment-visit save record is enough to
                // re-prime and force a clean re-entry on reload.
                RestoreApartmentVisitContext(visit, reason + "_indoor_restore", clearActiveVisit: true);
                return;
            }

            ClearPersistedApartmentVisit();
            RestoreApartmentVisitContext(visit, reason, clearActiveVisit: true);
        }

        internal static void HandleApartmentVisitRuntimeReset(string reason)
        {
            if (ActiveApartmentVisit == null)
                return;

            var visit = ActiveApartmentVisit;
            if (visit.ActivePayload?.IsCustomLayoutPayload == true &&
                TryGetPersistedApartmentVisit(out var persistedCharacterId, out var persistedStateId, out var persistedExteriorAddress) &&
                string.Equals(visit.CharacterId, persistedCharacterId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(visit.StateId, persistedStateId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeAddressKey(visit.ExteriorAddress), persistedExteriorAddress, StringComparison.Ordinal))
            {
                RestoreApartmentRegistrationSnapshotOnly(visit);
                ActiveApartmentVisit = null;
                LastApartmentResumeAttemptAddress = string.Empty;
                LogDebug(
                    $"ApartmentVisitResetReprimeScheduled character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} reason={reason}");
                return;
            }

            RestoreApartmentVisitContext(visit, reason, clearActiveVisit: true);
        }

        private static void TryResumeApartmentVisitForLoadedIndoorState()
        {
            if (!IsIndoorGameplayContextActive())
            {
                LastApartmentResumeAttemptAddress = string.Empty;
                return;
            }

            var indoorAddress = NormalizeAddressKey(GetCurrentIndoorBuildingAddressKey());
            if (string.IsNullOrWhiteSpace(indoorAddress) ||
                string.Equals(LastApartmentResumeAttemptAddress, indoorAddress, StringComparison.Ordinal))
            {
                return;
            }

            LastApartmentResumeAttemptAddress = indoorAddress;

            // Do not auto-route every vanilla apartment entered at a matching address.
            // The routed NPC apartment shares the same exterior/indoor shell as the player's
            // normal apartment, so address alone is not enough evidence. Only resume the routed
            // apartment when StreetQuest has an explicit persisted apartment-visit record, such
            // as a save made while the player was inside Fran's apartment. Normal explicit entry
            // through the StreetQuest apartment button already owns ActiveApartmentVisit and does
            // not need this resume path.
            if (!TryGetPersistedApartmentVisit(out var persistedCharacterId, out var persistedStateId, out var persistedExteriorAddress) ||
                string.IsNullOrWhiteSpace(persistedCharacterId) ||
                string.IsNullOrWhiteSpace(persistedStateId) ||
                string.IsNullOrWhiteSpace(persistedExteriorAddress))
            {
                LogVerbose($"ApartmentVisitResumeSkipped address={indoorAddress} reason=no_persisted_routed_visit");
                return;
            }

            var normalizedPersistedExteriorAddress = NormalizeAddressKey(persistedExteriorAddress);
            if (!string.Equals(indoorAddress, normalizedPersistedExteriorAddress, StringComparison.Ordinal))
            {
                LogDebug(
                    $"ApartmentVisitResumeSkipped address={indoorAddress} persistedAddress={normalizedPersistedExteriorAddress} character={persistedCharacterId} state={persistedStateId} reason=persisted_address_mismatch");
                return;
            }

            var option = GetAvailableApartmentEntryOptions(indoorAddress)
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    candidate.RequiresApartmentVisitContext &&
                    string.Equals(candidate.CharacterId, persistedCharacterId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.StateId, persistedStateId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        NormalizeAddressKey(candidate.ExteriorAddress),
                        normalizedPersistedExteriorAddress,
                        StringComparison.Ordinal));
            if (option == null)
            {
                LogDebug($"ApartmentVisitResumeSkipped address={indoorAddress} character={persistedCharacterId} state={persistedStateId} reason=no_matching_persisted_routed_option");
                return;
            }

            if (!TryResolveApartmentVisitTarget(option, out var building, out var registration, out _, out var failureReason))
            {
                LogDebug(
                    $"ApartmentVisitResumeFailed address={indoorAddress} character={option.CharacterId} state={option.StateId} reason={failureReason}");
                return;
            }

            var visitContext = CreateApartmentVisitContext(option, building, registration, preferCachedOriginalSnapshot: true);
            visitContext.EntryStartedAtSeconds = Time.unscaledTime - 1f;
            visitContext.EntryRoute = "resume_loaded_indoor_persisted";
            visitContext.State = StreetQuestApartmentVisitState.ActiveInside;

            ApplyApartmentPayload(visitContext);
            TryApplyRuntimeApartmentLayout(visitContext);
            visitContext.PayloadAppliedInside = true;
            ActiveApartmentVisit = visitContext;
            LastApartmentReturnDeferredLogKey = string.Empty;

            RefreshSpawnedCharacters();
            LogDebug(
                $"ApartmentVisitResumed address={indoorAddress} character={option.CharacterId} state={option.StateId} route={visitContext.EntryRoute}");
        }

        private static StreetQuestApartmentVisitContext CreateApartmentVisitContext(
            StreetQuestShared.ApartmentEntryOption option,
            Building building,
            BuildingRegistration registration,
            bool preferCachedOriginalSnapshot = false)
        {
            var visitKey = BuildApartmentVisitKey(option);
            var capturedOriginalSnapshot = CaptureApartmentRegistrationSnapshot(registration);
            var usesCustomLayout = option != null && !string.IsNullOrWhiteSpace(option.ApartmentLayoutFile);
            var originalSnapshot = ResolveApartmentOriginalSnapshot(
                visitKey,
                capturedOriginalSnapshot,
                usesCustomLayout,
                preferCachedOriginalSnapshot);

            StreetQuestApartmentInteriorPayload cachedPayload = null;
            if (!usesCustomLayout)
                ApartmentPayloadCacheByVisitKey.TryGetValue(visitKey, out cachedPayload);

            var payload = cachedPayload ?? CreateDefaultApartmentPayload(option, originalSnapshot, registration);
            if (cachedPayload == null)
            {
                if (!usesCustomLayout)
                    ApartmentPayloadCacheByVisitKey[visitKey] = payload;

                LogDebug(
                    $"ApartmentPayloadPrepared key={visitKey} source={(usesCustomLayout ? "custom_layout_fresh" : "blank_payload")} layout={payload.Layout ?? "<null>"}");
            }
            else
            {
                LogDebug(
                    $"ApartmentPayloadPrepared key={visitKey} source=session_cache layout={payload.Layout ?? "<null>"}");
            }

            return new StreetQuestApartmentVisitContext
            {
                CharacterId = option.CharacterId ?? string.Empty,
                StateId = option.StateId ?? string.Empty,
                ExteriorAddress = option.ExteriorAddress ?? string.Empty,
                VisitKey = visitKey,
                Building = building,
                Registration = registration,
                OriginalSnapshot = originalSnapshot,
                ActivePayload = payload
            };
        }

        private static StreetQuestApartmentRegistrationSnapshot ResolveApartmentOriginalSnapshot(
            string visitKey,
            StreetQuestApartmentRegistrationSnapshot capturedOriginalSnapshot,
            bool usesCustomLayout,
            bool preferCachedOriginalSnapshot)
        {
            if (string.IsNullOrWhiteSpace(visitKey) || !usesCustomLayout)
                return capturedOriginalSnapshot;

            if (preferCachedOriginalSnapshot &&
                ApartmentOriginalSnapshotCacheByVisitKey.TryGetValue(visitKey, out var cachedOriginalSnapshot) &&
                cachedOriginalSnapshot != null)
            {
                LogDebug(
                    $"ApartmentOriginalSnapshotReused key={visitKey} reason=reload_or_resume capturedInteriorDesigns={DescribeValueShape(capturedOriginalSnapshot?.GetRaw("interiorDesigns"))} cachedInteriorDesigns={DescribeValueShape(cachedOriginalSnapshot.GetRaw("interiorDesigns"))}");
                return cachedOriginalSnapshot;
            }

            ApartmentOriginalSnapshotCacheByVisitKey[visitKey] = capturedOriginalSnapshot;
            LogDebug(
                $"ApartmentOriginalSnapshotCached key={visitKey} reason={(preferCachedOriginalSnapshot ? "cache_missing" : "explicit_entry_refresh")} interiorDesigns={DescribeValueShape(capturedOriginalSnapshot?.GetRaw("interiorDesigns"))} itemInstances={DescribeValueShape(capturedOriginalSnapshot?.GetRaw("itemInstances"))} itemsInBuilding={DescribeValueShape(capturedOriginalSnapshot?.GetRaw("itemsInBuilding"))}");
            return capturedOriginalSnapshot;
        }

        private static StreetQuestApartmentRegistrationSnapshot GetApartmentRestoreSnapshot(StreetQuestApartmentVisitContext context)
        {
            if (context == null)
                return null;

            if (!string.IsNullOrWhiteSpace(context.VisitKey) &&
                ApartmentOriginalSnapshotCacheByVisitKey.TryGetValue(context.VisitKey, out var cachedOriginalSnapshot) &&
                cachedOriginalSnapshot != null)
            {
                return cachedOriginalSnapshot;
            }

            return context.OriginalSnapshot;
        }

        private static string DescribeApartmentRestoreSnapshotSource(
            StreetQuestApartmentVisitContext context,
            StreetQuestApartmentRegistrationSnapshot restoreSnapshot)
        {
            if (context == null || restoreSnapshot == null)
                return "none";

            if (!string.IsNullOrWhiteSpace(context.VisitKey) &&
                ApartmentOriginalSnapshotCacheByVisitKey.TryGetValue(context.VisitKey, out var cachedOriginalSnapshot) &&
                ReferenceEquals(restoreSnapshot, cachedOriginalSnapshot))
            {
                return "cached_original";
            }

            if (ReferenceEquals(restoreSnapshot, context.OriginalSnapshot))
                return "captured_original";

            return "unknown";
        }

        private static bool IsApartmentShutdownRestoreReason(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason) &&
                   reason.StartsWith("watcher_shutdown", StringComparison.OrdinalIgnoreCase);
        }

        private static StreetQuestApartmentRegistrationSnapshot CaptureApartmentRegistrationSnapshot(BuildingRegistration registration)
        {
            var snapshot = new StreetQuestApartmentRegistrationSnapshot();
            if (registration == null)
                return snapshot;

            foreach (var fieldName in ApartmentRegistrationFieldNames)
                snapshot.FieldValues[fieldName] = GetMemberValue(registration, fieldName);

            return snapshot;
        }

        private static StreetQuestApartmentInteriorPayload CreateDefaultApartmentPayload(
            StreetQuestShared.ApartmentEntryOption option,
            StreetQuestApartmentRegistrationSnapshot originalSnapshot,
            BuildingRegistration registration)
        {
            if (TryCreateRegisteredLayoutApartmentPayload(option, originalSnapshot, registration, out var registeredLayoutPayload))
                return registeredLayoutPayload;

            return new StreetQuestApartmentInteriorPayload
            {
                Layout = originalSnapshot.Get<string>("Layout"),
                InteriorDesigns = CreateEmptyValueLike(originalSnapshot.GetRaw("interiorDesigns"), GetMemberType(registration, "interiorDesigns")),
                ItemInstances = CreateEmptyValueLike(originalSnapshot.GetRaw("itemInstances"), GetMemberType(registration, "itemInstances")),
                ItemsInBuilding = CreateEmptyValueLike(originalSnapshot.GetRaw("itemsInBuilding"), GetMemberType(registration, "itemsInBuilding")),
                DeliveredItems = CreateEmptyValueLike(originalSnapshot.GetRaw("deliveredItems"), GetMemberType(registration, "deliveredItems")),
                DirtSpots = CreateEmptyValueLike(originalSnapshot.GetRaw("dirtSpots"), GetMemberType(registration, "dirtSpots"))
            };
        }

        private static void ApplyApartmentPayload(StreetQuestApartmentVisitContext context)
        {
            if (context?.Registration == null || context.ActivePayload == null)
                return;

            var interiorDesigns = CloneValueLike(
                context.ActivePayload.InteriorDesigns,
                GetMemberType(context.Registration, "interiorDesigns"));
            var itemInstances = context.ActivePayload.IsCustomLayoutPayload
                ? CloneValueLike(
                    context.ActivePayload.ItemInstances,
                    GetMemberType(context.Registration, "itemInstances"))
                : MergeApartmentItemInstances(
                    GetMemberValue(context.Registration, "itemInstances"),
                    context.ActivePayload.ItemInstances,
                    GetMemberType(context.Registration, "itemInstances"));
            var itemsInBuilding = context.ActivePayload.IsCustomLayoutPayload
                ? CloneValueLike(
                    context.ActivePayload.ItemsInBuilding,
                    GetMemberType(context.Registration, "itemsInBuilding"))
                : CloneValueLike(
                    context.ActivePayload.ItemsInBuilding,
                    GetMemberType(context.Registration, "itemsInBuilding"));
            var deliveredItems = CloneValueLike(
                context.ActivePayload.DeliveredItems,
                GetMemberType(context.Registration, "deliveredItems"));
            var dirtSpots = CloneValueLike(
                context.ActivePayload.DirtSpots,
                GetMemberType(context.Registration, "dirtSpots"));

            SetMemberValue(context.Registration, "Layout", context.ActivePayload.Layout);
            if (interiorDesigns != null)
                SetMemberValue(context.Registration, "interiorDesigns", interiorDesigns);
            if (itemInstances != null)
                SetMemberValue(context.Registration, "itemInstances", itemInstances);
            if (itemsInBuilding != null)
                SetMemberValue(context.Registration, "itemsInBuilding", itemsInBuilding);
            if (deliveredItems != null)
                SetMemberValue(context.Registration, "deliveredItems", deliveredItems);
            if (dirtSpots != null)
                SetMemberValue(context.Registration, "dirtSpots", dirtSpots);

            var originalLayout = context.OriginalSnapshot?.Get<string>("Layout") ?? "<null>";
            var payloadLayout = context.ActivePayload.Layout ?? "<null>";

            LogDebug(
                $"ApartmentPayloadApplied key={context.VisitKey} originalLayout={originalLayout} payloadLayout={payloadLayout} mode={(context.ActivePayload.IsCustomLayoutPayload ? "replace_custom_layout" : "merge_live")} interiorDesigns={DescribeValueShape(GetMemberValue(context.Registration, "interiorDesigns"))} itemInstances={DescribeValueShape(GetMemberValue(context.Registration, "itemInstances"))} itemsInBuilding={DescribeValueShape(GetMemberValue(context.Registration, "itemsInBuilding"))}");
        }

        private static bool TryStartReloadApartmentReentry(StreetQuestApartmentVisitContext context)
        {
            if (context == null || context.Building == null)
            {
                LogDebug($"ApartmentReloadReentryFailed key={context?.VisitKey ?? "<null>"} reason=building_missing");
                return false;
            }

            RestoreApartmentRegistrationSnapshotOnly(context);
            context.PreEntryItemsSuppressed = false;
            TrySuppressPreEntryApartmentItems(context);

            if (!TryStartVanillaApartmentEntry(context.Building, out var route))
            {
                LogDebug($"ApartmentReloadReentryFailed key={context.VisitKey} reason=start_vanilla_entry_failed");
                return false;
            }

            context.ReloadReentryPending = true;
            context.ReloadReentryStarted = true;
            context.PayloadAppliedInside = false;
            context.PayloadSeededBeforeIndoor = false;
            context.ApartmentItemsProtected = false;
            context.ApartmentItemProtectionAttempts = 0;
            context.EntryStartedAtSeconds = Time.unscaledTime;
            context.EntryRoute = $"reload_forced_reentry:{route}";
            context.State = StreetQuestApartmentVisitState.WaitingForIndoorTransition;
            ClearApartmentOutdoorReturnCandidate(context);
            SetCurrentIndoorBuildingAddressKey(context.ExteriorAddress);
            SetPersistedApartmentVisit(context.CharacterId, context.StateId, context.ExteriorAddress);

            LogDebug(
                $"ApartmentReloadReentryStarted character={context.CharacterId} state={context.StateId} address={context.ExteriorAddress} route={route}");
            return true;
        }

        private static void TrySeedApartmentPayloadBeforeIndoorReload(StreetQuestApartmentVisitContext context)
        {
            if (context?.Registration == null ||
                context.ActivePayload == null ||
                context.PayloadSeededBeforeIndoor ||
                !context.ActivePayload.IsCustomLayoutPayload)
            {
                return;
            }

            ApplyApartmentPayload(context);
            context.PayloadSeededBeforeIndoor = true;
            LogDebug(
                $"ApartmentPayloadSeededBeforeIndoor key={context.VisitKey} route={context.EntryRoute} interiorDesigns={DescribeValueShape(GetMemberValue(context.Registration, "interiorDesigns"))} itemInstances={DescribeValueShape(GetMemberValue(context.Registration, "itemInstances"))} itemsInBuilding={DescribeValueShape(GetMemberValue(context.Registration, "itemsInBuilding"))}");
        }

        private static void TrySuppressPreEntryApartmentItems(StreetQuestApartmentVisitContext context)
        {
            if (context?.Registration == null ||
                context.ActivePayload == null ||
                !context.ActivePayload.IsCustomLayoutPayload ||
                context.PreEntryItemsSuppressed)
                return;

            var emptyItemInstances = CreateEmptyValueLike(
                context.OriginalSnapshot?.GetRaw("itemInstances"),
                GetMemberType(context.Registration, "itemInstances"));
            var emptyItemsInBuilding = CreateEmptyValueLike(
                context.OriginalSnapshot?.GetRaw("itemsInBuilding"),
                GetMemberType(context.Registration, "itemsInBuilding"));

            if (emptyItemInstances != null)
                SetMemberValue(context.Registration, "itemInstances", emptyItemInstances);
            if (emptyItemsInBuilding != null)
                SetMemberValue(context.Registration, "itemsInBuilding", emptyItemsInBuilding);

            context.PreEntryItemsSuppressed = true;
            LogDebug(
                $"ApartmentPreEntryItemsSuppressed key={context.VisitKey} itemInstances={DescribeValueShape(GetMemberValue(context.Registration, "itemInstances"))} itemsInBuilding={DescribeValueShape(GetMemberValue(context.Registration, "itemsInBuilding"))}");
        }

        private static void TryApplyRuntimeApartmentLayout(StreetQuestApartmentVisitContext context)
        {
            if (context?.ActivePayload == null ||
                !context.ActivePayload.IsCustomLayoutPayload ||
                string.IsNullOrWhiteSpace(context.ActivePayload.TempLayoutPath) ||
                !System.IO.File.Exists(context.ActivePayload.TempLayoutPath))
                return;

            try
            {
                var helperType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(ApartmentLayoutHelperTypeName, false))
                    .FirstOrDefault(type => type != null);
                if (helperType == null)
                {
                    LogDebug("ApartmentRuntimeLayoutApplyFailed reason=helper_missing");
                    return;
                }

                var deserializeMethod = helperType.GetMethod(
                    "Deserialize",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                if (deserializeMethod == null)
                {
                    LogDebug("ApartmentRuntimeLayoutApplyFailed reason=deserialize_missing");
                    return;
                }

                var layoutSet = deserializeMethod.Invoke(null, new object[] { context.ActivePayload.TempLayoutPath });
                if (layoutSet == null)
                {
                    LogDebug($"ApartmentRuntimeLayoutApplyFailed reason=layoutset_null path={context.ActivePayload.TempLayoutPath}");
                    return;
                }

                TryApplyLegacyLayoutFix(layoutSet);

                var buildingManagerType = FindType("BuildingManager");
                var buildingManager = buildingManagerType != null ? UnityEngine.Object.FindObjectOfType(buildingManagerType) : null;
                var loadBusinessLayoutSetMethod = buildingManagerType?.GetMethod(
                    "LoadBusinessLayoutSet",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { layoutSet.GetType() },
                    null);
                if (buildingManager == null || loadBusinessLayoutSetMethod == null)
                {
                    LogDebug("ApartmentRuntimeLayoutApplyFailed reason=building_manager_method_missing");
                    return;
                }

                var result = loadBusinessLayoutSetMethod.Invoke(buildingManager, new[] { layoutSet });
                LogDebug(
                    $"ApartmentRuntimeLayoutApplied key={context.VisitKey} layoutName={context.ActivePayload.LayoutName ?? "<unknown>"} path={context.ActivePayload.TempLayoutPath} result={result}");
            }
            catch (Exception exception)
            {
                LogDebug(
                    $"ApartmentRuntimeLayoutApplyFailed reason={exception.GetType().Name}:{exception.Message} key={context?.VisitKey}");
            }
        }

        private static void TryProtectApartmentItems(StreetQuestApartmentVisitContext context)
        {
            if (context == null || context.ApartmentItemsProtected)
                return;

            var itemControllerType = FindType("ItemController");
            if (itemControllerType == null)
            {
                context.ApartmentItemsProtected = true;
                LogDebug($"ApartmentItemProtectionSkipped key={context.VisitKey} reason=item_controller_type_missing");
                return;
            }

            try
            {
                var itemControllers = UnityEngine.Object.FindObjectsOfType(itemControllerType, true);
                if (itemControllers == null || itemControllers.Length == 0)
                {
                    context.ApartmentItemProtectionAttempts++;
                    if (context.ApartmentItemProtectionAttempts >= 5)
                    {
                        context.ApartmentItemsProtected = true;
                        LogDebug($"ApartmentItemProtectionSkipped key={context.VisitKey} reason=no_item_controllers_found");
                    }

                    return;
                }

                var protectedObjectIds = new HashSet<int>();
                var protectedColliders = 0;
                var protectedBehaviours = 0;
                var sampleNames = new StringBuilder();

                foreach (var candidate in itemControllers)
                {
                    if (candidate is not Component component || component == null)
                        continue;

                    var targetObject = component.gameObject;
                    if (targetObject == null || !protectedObjectIds.Add(targetObject.GetInstanceID()))
                        continue;

                    if (sampleNames.Length < 120)
                    {
                        if (sampleNames.Length > 0)
                            sampleNames.Append(", ");
                        sampleNames.Append(targetObject.name);
                    }

                    foreach (var collider in targetObject.GetComponents<Collider>())
                    {
                        if (collider == null || !collider.enabled)
                            continue;

                        collider.enabled = false;
                        protectedColliders++;
                    }

                    foreach (var behaviour in targetObject.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour == null || !behaviour.enabled)
                            continue;

                        var behaviourType = behaviour.GetType();
                        var typeName = behaviourType.Name;
                        if (!string.Equals(typeName, "ItemController", StringComparison.Ordinal) &&
                            !typeName.EndsWith("ItemController", StringComparison.Ordinal))
                            continue;

                        if (string.Equals(typeName, "Animator", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(typeName, "Animation", StringComparison.OrdinalIgnoreCase) ||
                            behaviour is StreetQuestPhysicalQuestGiverWatcher ||
                            behaviour is StreetQuestCharacterSpeechBubble)
                            continue;

                        if (behaviour is Behaviour unityBehaviour)
                        {
                            unityBehaviour.enabled = false;
                            protectedBehaviours++;
                        }
                    }
                }

                context.ApartmentItemsProtected = true;
                LogDebug(
                    $"ApartmentItemProtectionApplied key={context.VisitKey} objects={protectedObjectIds.Count} colliders={protectedColliders} behaviours={protectedBehaviours} samples=[{sampleNames}]");
            }
            catch (Exception exception)
            {
                context.ApartmentItemProtectionAttempts++;
                if (context.ApartmentItemProtectionAttempts >= 5)
                    context.ApartmentItemsProtected = true;

                LogDebug(
                    $"ApartmentItemProtectionFailed key={context.VisitKey} attempt={context.ApartmentItemProtectionAttempts} reason={exception.GetType().Name}:{exception.Message}");
            }
        }

        private static object MergeApartmentItemInstances(object liveItemInstances, object payloadItemInstances, Type targetType)
        {
            if (payloadItemInstances is not IDictionary payloadDictionary)
                return liveItemInstances;

            var merged = CloneValueLike(liveItemInstances, targetType) ??
                         CreateEmptyValueLike(liveItemInstances, targetType) ??
                         CreateEmptyValueLike(payloadItemInstances, targetType);
            if (merged is not IDictionary mergedDictionary)
                return liveItemInstances;

            foreach (DictionaryEntry entry in payloadDictionary)
                mergedDictionary[entry.Key] = entry.Value;

            return merged;
        }

        private static void CaptureApartmentPayload(StreetQuestApartmentVisitContext context)
        {
            if (context?.Registration == null || context.ActivePayload == null)
                return;

            if (context.ActivePayload.IsCustomLayoutPayload)
            {
                LogDebug($"ApartmentPayloadCaptureSkipped key={context.VisitKey} reason=custom_layout_payload");
                return;
            }

            context.ActivePayload.Layout = GetMemberValue(context.Registration, "Layout") as string;
            context.ActivePayload.InteriorDesigns = GetMemberValue(context.Registration, "interiorDesigns");
            context.ActivePayload.ItemInstances = GetMemberValue(context.Registration, "itemInstances");
            context.ActivePayload.ItemsInBuilding = GetMemberValue(context.Registration, "itemsInBuilding");
            context.ActivePayload.DeliveredItems = GetMemberValue(context.Registration, "deliveredItems");
            context.ActivePayload.DirtSpots = GetMemberValue(context.Registration, "dirtSpots");
            ApartmentPayloadCacheByVisitKey[context.VisitKey] = context.ActivePayload;

            LogDebug(
                $"ApartmentPayloadCaptured key={context.VisitKey} layout={context.ActivePayload.Layout ?? "<null>"} interiorDesigns={DescribeValueShape(context.ActivePayload.InteriorDesigns)} itemInstances={DescribeValueShape(context.ActivePayload.ItemInstances)} itemsInBuilding={DescribeValueShape(context.ActivePayload.ItemsInBuilding)}");
        }

        private static bool IsApartmentOutdoorReturnConfirmed(StreetQuestApartmentVisitContext visit, string currentExteriorAddress)
        {
            if (visit == null)
                return false;

            var normalizedExterior = NormalizeAddressKey(currentExteriorAddress);
            if (string.IsNullOrWhiteSpace(normalizedExterior))
                return false;

            if (!string.Equals(visit.PendingOutdoorReturnAddress, normalizedExterior, StringComparison.Ordinal))
            {
                visit.PendingOutdoorReturnAddress = normalizedExterior;
                visit.PendingOutdoorReturnStartedAtSeconds = Time.unscaledTime;
                return false;
            }

            return HasApartmentOutdoorReturnCandidateExpired(visit);
        }

        private static bool HasApartmentOutdoorReturnCandidateExpired(StreetQuestApartmentVisitContext visit)
        {
            if (visit == null ||
                string.IsNullOrWhiteSpace(visit.PendingOutdoorReturnAddress) ||
                visit.PendingOutdoorReturnStartedAtSeconds < 0f)
            {
                return false;
            }

            return Time.unscaledTime - visit.PendingOutdoorReturnStartedAtSeconds >=
                   ApartmentReturnOutdoorConfirmationSeconds;
        }

        private static void ClearApartmentOutdoorReturnCandidate(StreetQuestApartmentVisitContext visit)
        {
            if (visit == null)
                return;

            visit.PendingOutdoorReturnAddress = string.Empty;
            visit.PendingOutdoorReturnStartedAtSeconds = -1f;
        }

        private static string LastApartmentReturnDeferredLogKey = string.Empty;

        private static void LogApartmentVisitReturnDeferred(StreetQuestApartmentVisitContext visit, string reason)
        {
            if (visit == null)
                return;

            var logKey = $"{visit.CharacterId}|{visit.StateId}|{visit.ExteriorAddress}|{reason}";
            if (string.Equals(LastApartmentReturnDeferredLogKey, logKey, StringComparison.Ordinal))
                return;

            LastApartmentReturnDeferredLogKey = logKey;
            LogDebug(
                $"ApartmentVisitReturnDeferred character={visit.CharacterId} state={visit.StateId} address={visit.ExteriorAddress} reason={reason}");
        }

        private static void RestoreApartmentRegistrationSnapshotOnly(StreetQuestApartmentVisitContext context)
        {
            var restoreSnapshot = GetApartmentRestoreSnapshot(context);
            if (context?.Registration == null || restoreSnapshot == null)
                return;

            foreach (var fieldName in ApartmentRegistrationFieldNames)
                SetMemberValue(context.Registration, fieldName, restoreSnapshot.GetRaw(fieldName));
        }

        private static void RestoreApartmentVisitContext(
            StreetQuestApartmentVisitContext context,
            string reason,
            bool clearActiveVisit)
        {
            if (context == null)
                return;

            CaptureApartmentPayload(context);

            var restoreSnapshot = GetApartmentRestoreSnapshot(context);
            if (context.Registration != null && restoreSnapshot != null)
            {
                foreach (var fieldName in ApartmentRegistrationFieldNames)
                    SetMemberValue(context.Registration, fieldName, restoreSnapshot.GetRaw(fieldName));
            }

            var restoreLogMessage =
                $"ApartmentVisitRestored character={context.CharacterId} state={context.StateId} address={context.ExteriorAddress} reason={reason} snapshot={DescribeApartmentRestoreSnapshotSource(context, restoreSnapshot)}";
            if (IsApartmentShutdownRestoreReason(reason))
                LogVerbose(restoreLogMessage);
            else
                LogDebug(restoreLogMessage);

            if (clearActiveVisit && ReferenceEquals(ActiveApartmentVisit, context))
            {
                ActiveApartmentVisit = null;
                LastApartmentReturnDeferredLogKey = string.Empty;
            }

            RefreshSpawnedCharacters();
        }

        private static bool TryResolveApartmentVisitTarget(
            StreetQuestShared.ApartmentEntryOption option,
            out Building building,
            out BuildingRegistration registration,
            out object parsedAddress,
            out string failureReason)
        {
            building = null;
            registration = null;
            parsedAddress = null;
            failureReason = string.Empty;

            var addressText = option.ExteriorAddress ?? string.Empty;
            if (string.IsNullOrWhiteSpace(addressText))
            {
                failureReason = "missing_exterior_address";
                return false;
            }

            registration = FindBuildingRegistrationByAddressText(addressText);
            parsedAddress = registration?.Address;

            if (parsedAddress == null)
            {
                try
                {
                    parsedAddress = BuildingHelper.ParseAddressString(addressText);
                }
                catch (Exception exception)
                {
                    failureReason = $"parse_address_failed:{exception.GetType().Name}";
                    return false;
                }
            }

            if (parsedAddress == null)
            {
                failureReason = "parsed_address_null";
                return false;
            }

            try
            {
                building = InvokeStaticBuildingHelperMethod<Building>("GetBuilding", parsedAddress);
            }
            catch (Exception exception)
            {
                failureReason = $"get_building_failed:{exception.GetType().Name}";
                return false;
            }

            if (building == null)
            {
                failureReason = "building_not_found";
                return false;
            }

            if (registration == null)
            {
                try
                {
                    registration = InvokeStaticBuildingHelperMethod<BuildingRegistration>("GetBuildingRegistration", parsedAddress) ??
                                   building.GetRegistration();
                }
                catch (Exception exception)
                {
                    failureReason = $"get_registration_failed:{exception.GetType().Name}";
                    return false;
                }
            }

            if (registration == null)
            {
                failureReason = "registration_not_found";
                return false;
            }

            return true;
        }

        private static BuildingRegistration FindBuildingRegistrationByAddressText(string addressText)
        {
            var normalizedAddress = NormalizeAddressKey(addressText);
            if (string.IsNullOrWhiteSpace(normalizedAddress))
                return null;

            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return null;

            foreach (var candidate in registrations)
            {
                if (candidate?.Address == null)
                    continue;

                var candidateText = NormalizeAddressKey(candidate.Address.ToString());
                if (string.Equals(candidateText, normalizedAddress, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        private static bool TryStartVanillaApartmentEntry(Building building, out string route)
        {
            route = string.Empty;
            if (building == null)
                return false;

            var buildingManagerType = FindType("BuildingManager");
            var buildingManager = buildingManagerType != null ? UnityEngine.Object.FindObjectOfType(buildingManagerType) : null;
            var enterBuildingMethod = buildingManagerType?.GetMethod(
                "EnterBuilding",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Building), typeof(bool), typeof(bool), typeof(int), typeof(int), typeof(bool) },
                null);
            if (buildingManager != null && enterBuildingMethod != null)
            {
                var result = enterBuildingMethod.Invoke(buildingManager, new object[] { building, false, false, -1, -1, true });
                route = $"BuildingManager.EnterBuilding result={result}";
                return result as bool? ?? false;
            }

            var cityManagerType = FindType("CityManager");
            var cityManager = cityManagerType != null ? UnityEngine.Object.FindObjectOfType(cityManagerType) : null;
            var loadIndoorsMethod = cityManagerType?.GetMethod(
                "LoadIndoors",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Building), typeof(bool) },
                null);
            if (cityManager != null && loadIndoorsMethod != null)
            {
                loadIndoorsMethod.Invoke(cityManager, new object[] { building, false });
                route = "CityManager.LoadIndoors useSaveGamePlayerPosition=false";
                return true;
            }

            return false;
        }

        private static string BuildApartmentVisitKey(StreetQuestShared.ApartmentEntryOption option)
        {
            return string.Join(
                "|",
                new[]
                {
                    NormalizeAddressKey(option?.ExteriorAddress),
                    option?.CharacterId ?? string.Empty,
                    option?.StateId ?? string.Empty
                });
        }

        private static object CreateEmptyValueLike(object existingValue, Type targetType)
        {
            targetType ??= existingValue?.GetType();
            if (targetType == null)
                return null;

            if (targetType == typeof(string))
                return string.Empty;

            if (targetType.IsArray)
            {
                var elementType = targetType.GetElementType() ?? typeof(object);
                return Array.CreateInstance(elementType, 0);
            }

            if (typeof(IDictionary).IsAssignableFrom(targetType) ||
                typeof(IList).IsAssignableFrom(targetType) ||
                targetType.GetConstructor(Type.EmptyTypes) != null)
            {
                try
                {
                    return Activator.CreateInstance(targetType);
                }
                catch
                {
                }
            }

            return existingValue;
        }

        private static Type GetMemberType(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            for (var currentType = instance.GetType(); currentType != null; currentType = currentType.BaseType)
            {
                var property = currentType.GetProperty(memberName, ReflectionFlags);
                if (property != null)
                    return property.PropertyType;

                var field = currentType.GetField(memberName, ReflectionFlags);
                if (field != null)
                    return field.FieldType;
            }

            return null;
        }

        private static string DescribeValueShape(object value)
        {
            if (value == null)
                return "<null>";

            if (value is ICollection collection)
                return $"{value.GetType().Name}(count={collection.Count})";

            return value.GetType().Name;
        }

        private static T InvokeStaticBuildingHelperMethod<T>(string methodName, object argument)
        {
            var method = typeof(BuildingHelper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                        return false;

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 1 &&
                           argument != null &&
                           parameters[0].ParameterType.IsInstanceOfType(argument);
                });

            if (method == null)
                throw new MissingMethodException(typeof(BuildingHelper).FullName, methodName);

            var result = method.Invoke(null, new[] { argument });
            return result is T typedResult ? typedResult : default;
        }

        private sealed class StreetQuestApartmentVisitContext
        {
            public string CharacterId;
            public string StateId;
            public string ExteriorAddress;
            public string VisitKey;
            public Building Building;
            public BuildingRegistration Registration;
            public StreetQuestApartmentRegistrationSnapshot OriginalSnapshot;
            public StreetQuestApartmentInteriorPayload ActivePayload;
            public string EntryRoute;
            public float EntryStartedAtSeconds;
            public float ActiveInsideStartedAtSeconds = -1f;
            public StreetQuestApartmentVisitState State;
            public bool PayloadAppliedInside;
            public bool PayloadSeededBeforeIndoor;
            public bool PreEntryItemsSuppressed;
            public bool ApartmentItemsProtected;
            public int ApartmentItemProtectionAttempts;
            public bool ReloadReentryPending;
            public bool ReloadReentryStarted;
            public float ReloadReentryDueAtSeconds;
            public bool CharacterRespawnScheduled;
            public int CharacterRespawnAttempts;
            public float CharacterRespawnDueAtSeconds;
            public string PendingOutdoorReturnAddress = string.Empty;
            public float PendingOutdoorReturnStartedAtSeconds = -1f;
        }

        private sealed class StreetQuestApartmentRegistrationSnapshot
        {
            public readonly Dictionary<string, object> FieldValues = new(StringComparer.Ordinal);

            public T Get<T>(string fieldName) where T : class
            {
                return GetRaw(fieldName) as T;
            }

            public object GetRaw(string fieldName)
            {
                return FieldValues.TryGetValue(fieldName, out var value) ? value : null;
            }
        }

        private sealed class StreetQuestApartmentInteriorPayload
        {
            public bool IsCustomLayoutPayload;
            public string LayoutName;
            public string TempLayoutPath;
            public string Layout;
            public object InteriorDesigns;
            public object ItemInstances;
            public object ItemsInBuilding;
            public object DeliveredItems;
            public object DirtSpots;
        }

        private enum StreetQuestApartmentVisitState
        {
            WaitingForIndoorTransition,
            ActiveInside
        }
    }
}
