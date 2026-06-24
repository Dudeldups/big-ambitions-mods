using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StreetQuestRPG
{
    [Serializable]
    internal sealed class StreetQuestQuestStateRecord
    {
        private const char SegmentSeparator = '|';
        private const char CompletedSeparator = ',';
        private static readonly string[] DefaultKnownCharacterIds = { "mack" };

        [SerializeField] public string currentMainQuestId = StreetQuestQuestCatalog.FirstQuest?.Id ?? string.Empty;
        [SerializeField] public string currentMainQuestState = StreetQuestQuestProgressState.NotStarted.ToString();
        [SerializeField] public int introStage;
        [SerializeField] public List<string> completedQuestIds = new();
        [SerializeField] public List<string> activeSideQuestIds = new();
        [SerializeField] public List<string> readySideQuestIds = new();
        [SerializeField] public List<string> storyFlags = new();
        [SerializeField] public List<string> objectiveTokens = new();
        [SerializeField] public List<string> knownCharacterIds = new();
        [SerializeField] public List<string> favorCharacterIds = new();
        [SerializeField] public List<int> favorValues = new();
        [SerializeField] public string currentIndoorBuildingAddress = string.Empty;
        [SerializeField] public string activeApartmentCharacterId = string.Empty;
        [SerializeField] public string activeApartmentStateId = string.Empty;
        [SerializeField] public string activeApartmentExteriorAddress = string.Empty;

        public string CurrentMainQuestId
        {
            get => currentMainQuestId ?? string.Empty;
            set => currentMainQuestId = value ?? string.Empty;
        }

        public StreetQuestQuestProgressState CurrentMainQuestState
        {
            get => Enum.TryParse(currentMainQuestState, out StreetQuestQuestProgressState parsed)
                ? parsed
                : StreetQuestQuestProgressState.NotStarted;
            set => currentMainQuestState = value.ToString();
        }

        public int IntroStage
        {
            get => introStage;
            set => introStage = value;
        }

        public string CurrentIndoorBuildingAddress
        {
            get => currentIndoorBuildingAddress ?? string.Empty;
            set => currentIndoorBuildingAddress = value ?? string.Empty;
        }

        public string ActiveApartmentCharacterId
        {
            get => activeApartmentCharacterId ?? string.Empty;
            set => activeApartmentCharacterId = value ?? string.Empty;
        }

        public string ActiveApartmentStateId
        {
            get => activeApartmentStateId ?? string.Empty;
            set => activeApartmentStateId = value ?? string.Empty;
        }

        public string ActiveApartmentExteriorAddress
        {
            get => activeApartmentExteriorAddress ?? string.Empty;
            set => activeApartmentExteriorAddress = value ?? string.Empty;
        }

        public HashSet<string> CompletedQuestIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ActiveSideQuestIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReadySideQuestIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> StoryFlags { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ObjectiveTokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KnownCharacterIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> FavorByCharacterId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Serialize()
        {
            SyncListsFromSets();
            return JsonUtility.ToJson(this);
        }

        public bool AddStoryFlag(string storyFlagId)
        {
            if (string.IsNullOrWhiteSpace(storyFlagId))
                return false;

            var changed = StoryFlags.Add(storyFlagId);
            if (changed)
                SyncListsFromSets();
            return changed;
        }

        public void AddStoryFlags(IEnumerable<string> storyFlagIds)
        {
            if (storyFlagIds == null)
                return;

            var changed = false;
            foreach (var storyFlagId in storyFlagIds)
                changed |= AddStoryFlag(storyFlagId);

            if (changed)
                SyncListsFromSets();
        }

        public bool AddObjectiveToken(string objectiveToken)
        {
            if (string.IsNullOrWhiteSpace(objectiveToken))
                return false;

            var changed = ObjectiveTokens.Add(objectiveToken);
            if (changed)
                SyncListsFromSets();
            return changed;
        }

        public bool TryActivateSideQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId) || CompletedQuestIds.Contains(questId))
                return false;

            var changed = ActiveSideQuestIds.Add(questId);
            ReadySideQuestIds.Remove(questId);
            if (changed)
                SyncListsFromSets();

            return changed;
        }

        public bool TryMarkSideQuestReady(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId) || CompletedQuestIds.Contains(questId))
                return false;

            var removed = ActiveSideQuestIds.Remove(questId);
            var added = ReadySideQuestIds.Add(questId);
            if (removed || added)
                SyncListsFromSets();

            return added || removed;
        }

        public bool ClearSideQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;

            var removed = ActiveSideQuestIds.Remove(questId);
            removed |= ReadySideQuestIds.Remove(questId);
            if (removed)
                SyncListsFromSets();

            return removed;
        }

        public int GetFavor(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return 0;

            return FavorByCharacterId.TryGetValue(characterId, out var value) ? value : 0;
        }

        public bool AddKnownCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var changed = KnownCharacterIds.Add(characterId);
            if (changed)
                SyncListsFromSets();

            return changed;
        }

        public bool SetFavor(string characterId, int value)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return false;

            var clamped = Math.Max(-100, Math.Min(100, value));
            if (FavorByCharacterId.TryGetValue(characterId, out var existing) && existing == clamped)
                return false;

            FavorByCharacterId[characterId] = clamped;
            SyncListsFromSets();
            return true;
        }

        public bool ChangeFavor(string characterId, int delta)
        {
            if (string.IsNullOrWhiteSpace(characterId) || delta == 0)
                return false;

            return SetFavor(characterId, GetFavor(characterId) + delta);
        }

        public static StreetQuestQuestStateRecord Deserialize(string serializedValue)
        {
            if (string.IsNullOrWhiteSpace(serializedValue))
                return CreateInitialized();

            try
            {
                var trimmed = serializedValue.TrimStart();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    var record = JsonUtility.FromJson<StreetQuestQuestStateRecord>(serializedValue) ?? CreateInitialized();
                    record.SyncSetsFromLists();
                    return record;
                }

                return DeserializeLegacy(serializedValue);
            }
            catch
            {
                return CreateInitialized();
            }
        }

        private static StreetQuestQuestStateRecord DeserializeLegacy(string serializedValue)
        {
            var record = CreateInitialized();
            var segments = serializedValue.Split(SegmentSeparator);
            if (segments.Length > 0 && !string.IsNullOrWhiteSpace(segments[0]))
                record.CurrentMainQuestId = segments[0];

            if (segments.Length > 1 &&
                Enum.TryParse(segments[1], out StreetQuestQuestProgressState progressState))
                record.CurrentMainQuestState = progressState;

            if (segments.Length > 2 && int.TryParse(segments[2], out var parsedIntroStage))
            {
                record.IntroStage = parsedIntroStage;
                if (parsedIntroStage >= 1)
                    record.AddStoryFlag("streetquest:flag_mack_intro_started");
                if (parsedIntroStage >= 2)
                    record.AddStoryFlag("streetquest:flag_mack_offer_unlocked");
            }

            if (segments.Length > 3 && !string.IsNullOrWhiteSpace(segments[3]))
            {
                foreach (var completedQuestId in segments[3].Split(CompletedSeparator))
                {
                    if (!string.IsNullOrWhiteSpace(completedQuestId))
                        record.CompletedQuestIds.Add(completedQuestId);
                }
            }

            record.SyncListsFromSets();
            return record;
        }

        private static StreetQuestQuestStateRecord CreateInitialized()
        {
            var record = new StreetQuestQuestStateRecord();
            record.SyncSetsFromLists();
            return record;
        }

        private void SyncSetsFromLists()
        {
            completedQuestIds ??= new List<string>();
            activeSideQuestIds ??= new List<string>();
            readySideQuestIds ??= new List<string>();
            storyFlags ??= new List<string>();
            objectiveTokens ??= new List<string>();
            knownCharacterIds ??= new List<string>();
            favorCharacterIds ??= new List<string>();
            favorValues ??= new List<int>();

            CompletedQuestIds.Clear();
            ActiveSideQuestIds.Clear();
            ReadySideQuestIds.Clear();
            StoryFlags.Clear();
            ObjectiveTokens.Clear();
            KnownCharacterIds.Clear();
            FavorByCharacterId.Clear();

            foreach (var questId in completedQuestIds.Where(value => !string.IsNullOrWhiteSpace(value)))
                CompletedQuestIds.Add(questId);
            foreach (var questId in activeSideQuestIds.Where(value => !string.IsNullOrWhiteSpace(value)))
                ActiveSideQuestIds.Add(questId);
            foreach (var questId in readySideQuestIds.Where(value => !string.IsNullOrWhiteSpace(value)))
                ReadySideQuestIds.Add(questId);
            foreach (var storyFlagId in storyFlags.Where(value => !string.IsNullOrWhiteSpace(value)))
                StoryFlags.Add(storyFlagId);
            foreach (var objectiveToken in objectiveTokens.Where(value => !string.IsNullOrWhiteSpace(value)))
                ObjectiveTokens.Add(objectiveToken);
            foreach (var characterId in knownCharacterIds.Where(value => !string.IsNullOrWhiteSpace(value)))
                KnownCharacterIds.Add(characterId);

            foreach (var defaultCharacterId in DefaultKnownCharacterIds)
                KnownCharacterIds.Add(defaultCharacterId);

            for (var index = 0; index < Math.Min(favorCharacterIds.Count, favorValues.Count); index++)
            {
                var characterId = favorCharacterIds[index];
                if (string.IsNullOrWhiteSpace(characterId))
                    continue;

                FavorByCharacterId[characterId] = Math.Max(-100, Math.Min(100, favorValues[index]));
            }

            if (string.IsNullOrEmpty(CurrentMainQuestId) &&
                CurrentMainQuestState != StreetQuestQuestProgressState.Completed)
                CurrentMainQuestState = StreetQuestQuestProgressState.Completed;

            SyncListsFromSets();
        }

        private void SyncListsFromSets()
        {
            completedQuestIds = CompletedQuestIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            activeSideQuestIds = ActiveSideQuestIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            readySideQuestIds = ReadySideQuestIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            storyFlags = StoryFlags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            objectiveTokens = ObjectiveTokens.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            knownCharacterIds = KnownCharacterIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            var orderedFavorEntries = FavorByCharacterId
                .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            favorCharacterIds = orderedFavorEntries.Select(value => value.Key).ToList();
            favorValues = orderedFavorEntries.Select(value => value.Value).ToList();
        }
    }
}
