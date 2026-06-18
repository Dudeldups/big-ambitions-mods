using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace StreetQuestRPG
{
    [Serializable, DataContract]
    internal sealed class StreetQuestQuestStateRecord
    {
        private const char SegmentSeparator = '|';
        private const char CompletedSeparator = ',';

        [DataMember(Name = "currentQuestId")] public string currentQuestId = StreetQuestQuestCatalog.FirstQuest?.Id ?? string.Empty;
        [DataMember(Name = "currentQuestState")] public string currentQuestState = StreetQuestQuestProgressState.NotStarted.ToString();
        [DataMember(Name = "introStage")] public int introStage;
        [DataMember(Name = "completedQuestIds")] public List<string> completedQuestIds = new();
        [DataMember(Name = "storyFlags")] public List<string> storyFlags = new();
        [DataMember(Name = "objectiveTokens")] public List<string> objectiveTokens = new();
        [DataMember(Name = "favorEntries")] public List<StreetQuestFavorStateEntry> favorEntries = new();

        public string CurrentQuestId
        {
            get => currentQuestId ?? string.Empty;
            set => currentQuestId = value ?? string.Empty;
        }

        public StreetQuestQuestProgressState CurrentQuestState
        {
            get => Enum.TryParse(currentQuestState, out StreetQuestQuestProgressState parsed)
                ? parsed
                : StreetQuestQuestProgressState.NotStarted;
            set => currentQuestState = value.ToString();
        }

        public int IntroStage
        {
            get => introStage;
            set => introStage = value;
        }

        public HashSet<string> CompletedQuestIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> StoryFlags { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ObjectiveTokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> FavorByCharacterId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Serialize()
        {
            SyncListsFromSets();
            var serializer = new DataContractJsonSerializer(typeof(StreetQuestQuestStateRecord));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, this);
            return Encoding.UTF8.GetString(stream.ToArray());
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

        public int GetFavor(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                return 0;

            return FavorByCharacterId.TryGetValue(characterId, out var value) ? value : 0;
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
                    var serializer = new DataContractJsonSerializer(typeof(StreetQuestQuestStateRecord));
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(serializedValue));
                    var record = serializer.ReadObject(stream) as StreetQuestQuestStateRecord ?? CreateInitialized();
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
                record.CurrentQuestId = segments[0];

            if (segments.Length > 1 &&
                Enum.TryParse(segments[1], out StreetQuestQuestProgressState progressState))
                record.CurrentQuestState = progressState;

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
            storyFlags ??= new List<string>();
            objectiveTokens ??= new List<string>();
            favorEntries ??= new List<StreetQuestFavorStateEntry>();

            CompletedQuestIds.Clear();
            StoryFlags.Clear();
            ObjectiveTokens.Clear();
            FavorByCharacterId.Clear();

            foreach (var questId in completedQuestIds.Where(value => !string.IsNullOrWhiteSpace(value)))
                CompletedQuestIds.Add(questId);
            foreach (var storyFlagId in storyFlags.Where(value => !string.IsNullOrWhiteSpace(value)))
                StoryFlags.Add(storyFlagId);
            foreach (var objectiveToken in objectiveTokens.Where(value => !string.IsNullOrWhiteSpace(value)))
                ObjectiveTokens.Add(objectiveToken);
            foreach (var favorEntry in favorEntries.Where(value => value != null && !string.IsNullOrWhiteSpace(value.characterId)))
                FavorByCharacterId[favorEntry.characterId] = Math.Max(-100, Math.Min(100, favorEntry.value));

            if (string.IsNullOrEmpty(CurrentQuestId) &&
                CurrentQuestState != StreetQuestQuestProgressState.Completed)
                CurrentQuestState = StreetQuestQuestProgressState.Completed;
        }

        private void SyncListsFromSets()
        {
            completedQuestIds = CompletedQuestIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            storyFlags = StoryFlags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            objectiveTokens = ObjectiveTokens.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            favorEntries = FavorByCharacterId
                .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                .Select(value => new StreetQuestFavorStateEntry
                {
                    characterId = value.Key,
                    value = value.Value
                })
                .ToList();
        }
    }
}
