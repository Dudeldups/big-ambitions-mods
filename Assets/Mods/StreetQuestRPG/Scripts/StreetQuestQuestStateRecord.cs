using System;
using System.Collections.Generic;
using System.Linq;

namespace StreetQuestRPG
{
    internal sealed class StreetQuestQuestStateRecord
    {
        private const char SegmentSeparator = '|';
        private const char CompletedSeparator = ',';

        public string CurrentQuestId { get; set; } = StreetQuestQuestCatalog.FirstQuest.Id;
        public StreetQuestQuestProgressState CurrentQuestState { get; set; } =
            StreetQuestQuestProgressState.NotStarted;
        public HashSet<string> CompletedQuestIds { get; } = new();

        public string Serialize()
        {
            var completed = string.Join(
                CompletedSeparator.ToString(),
                CompletedQuestIds.OrderBy(value => value, StringComparer.Ordinal));
            return string.Join(
                SegmentSeparator.ToString(),
                CurrentQuestId ?? string.Empty,
                CurrentQuestState,
                completed);
        }

        public static StreetQuestQuestStateRecord Deserialize(string serializedValue)
        {
            var record = new StreetQuestQuestStateRecord();
            if (string.IsNullOrWhiteSpace(serializedValue))
                return record;

            var segments = serializedValue.Split(SegmentSeparator);
            if (segments.Length > 0 && !string.IsNullOrWhiteSpace(segments[0]))
                record.CurrentQuestId = segments[0];

            if (segments.Length > 1 &&
                Enum.TryParse(segments[1], out StreetQuestQuestProgressState progressState))
                record.CurrentQuestState = progressState;

            if (segments.Length > 2 && !string.IsNullOrWhiteSpace(segments[2]))
            {
                foreach (var completedQuestId in segments[2].Split(CompletedSeparator))
                {
                    if (!string.IsNullOrWhiteSpace(completedQuestId))
                        record.CompletedQuestIds.Add(completedQuestId);
                }
            }

            if (string.IsNullOrEmpty(record.CurrentQuestId) &&
                record.CurrentQuestState != StreetQuestQuestProgressState.Completed)
                record.CurrentQuestState = StreetQuestQuestProgressState.Completed;

            return record;
        }
    }
}
