using System;
using System.Collections.Generic;
using System.Linq;
using Localizor;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        internal sealed class ApartmentEntryOption
        {
            public string CharacterId;
            public string CharacterName;
            public string StateId;
            public string ExteriorAddress;
            public string InteriorAddress;
            public string ButtonText;
        }

        internal static IReadOnlyList<ApartmentEntryOption> GetAvailableApartmentEntryOptions(string exteriorAddressKey)
        {
            if (string.IsNullOrWhiteSpace(exteriorAddressKey))
                return Array.Empty<ApartmentEntryOption>();

            var normalizedExteriorAddress = NormalizeApartmentAddress(exteriorAddressKey);
            if (string.IsNullOrWhiteSpace(normalizedExteriorAddress))
                return Array.Empty<ApartmentEntryOption>();

            var results = new List<ApartmentEntryOption>();
            foreach (var character in StreetQuestCharacterCatalog.All.Where(value => value != null && value.enabled))
            {
                var activeState = StreetQuestCharacterRuntimeResolver.ResolveActiveState(character);
                if (activeState == null || string.IsNullOrWhiteSpace(activeState.buildingAddress))
                    continue;

                if (!string.Equals(
                        NormalizeApartmentAddress(activeState.buildingAddress),
                        normalizedExteriorAddress,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var runtime = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinitionWithoutGameplayGates(character, activeState);
                if (runtime == null)
                {
                    LogDebug(
                        $"ApartmentEntryCandidateSkipped character={character.id ?? "<null>"} state={activeState.id ?? "<unnamed>"} reason=runtime_null exteriorAddress={normalizedExteriorAddress}");
                    continue;
                }

                var characterName = ResolveCharacterDisplayName(character.id);
                LogDebug(
                    $"ApartmentEntryCandidateMatched character={character.id ?? "<null>"} state={activeState.id ?? "<unnamed>"} exteriorAddress={normalizedExteriorAddress} interiorAddress={runtime.buildingAddress ?? "<none>"}");
                results.Add(new ApartmentEntryOption
                {
                    CharacterId = character.id ?? string.Empty,
                    CharacterName = characterName,
                    StateId = activeState.id ?? string.Empty,
                    ExteriorAddress = normalizedExteriorAddress,
                    InteriorAddress = runtime.buildingAddress ?? string.Empty,
                    ButtonText = BuildApartmentEntryButtonText(runtime, characterName)
                });
            }

            return results
                .OrderBy(value => value.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static string BuildApartmentEntryPlaceholderMessage(ApartmentEntryOption option)
        {
            if (option == null)
                return string.Empty;

            return "streetquest:debug_apartment_entry_placeholder".Localize(new Dictionary<string, string>
            {
                { "npcname", option.CharacterName ?? option.CharacterId ?? "NPC" }
            }).ToString();
        }

        private static string BuildApartmentEntryButtonText(StreetQuestCharacterDefinition runtime, string characterName)
        {
            var localizationKey = string.IsNullOrWhiteSpace(runtime?.entryButtonTextKey)
                ? "streetquest:cta_enter_apartment"
                : runtime.entryButtonTextKey;

            return localizationKey.Localize(new Dictionary<string, string>
            {
                { "npcname", characterName ?? "NPC" }
            }).ToString();
        }

        private static string NormalizeApartmentAddress(string addressKey)
        {
            return string.IsNullOrWhiteSpace(addressKey)
                ? string.Empty
                : addressKey.Trim().ToLowerInvariant();
        }
    }
}
