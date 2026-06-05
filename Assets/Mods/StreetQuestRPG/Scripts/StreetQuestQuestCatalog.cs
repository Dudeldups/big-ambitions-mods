using System.Collections.Generic;

namespace StreetQuestRPG
{
    internal static class StreetQuestQuestCatalog
    {
        public const string Quest1Id = "streetquest:q1";
        public const string Quest2Id = "streetquest:q2";
        public const string Quest3Id = "streetquest:q3";

        private static readonly IReadOnlyDictionary<string, StreetQuestQuestDefinition> Quests =
            new Dictionary<string, StreetQuestQuestDefinition>
            {
                [Quest1Id] = new(
                    Quest1Id,
                    StreetQuestShared.HomelessContactId,
                    StreetQuestShared.HomelessContactId,
                    "ba:itemname_cheapgift",
                    1,
                    120,
                    "streetquest:dialog_q1_offer",
                    "streetquest:dialog_q1_active",
                    "streetquest:dialog_q1_ready",
                    "streetquest:dialog_q1_accept_player",
                    "streetquest:dialog_q1_accept_manager",
                    "streetquest:dialog_q1_complete_player",
                    "streetquest:dialog_q1_complete_manager",
                    Quest2Id),
                [Quest2Id] = new(
                    Quest2Id,
                    StreetQuestShared.HomelessContactId,
                    StreetQuestShared.CourierContactId,
                    "ba:itemname_expensivegift",
                    1,
                    220,
                    "streetquest:dialog_q2_offer",
                    "streetquest:dialog_q2_active",
                    "streetquest:dialog_q2_ready",
                    "streetquest:dialog_q2_accept_player",
                    "streetquest:dialog_q2_accept_manager",
                    "streetquest:dialog_q2_complete_player",
                    "streetquest:dialog_q2_complete_manager",
                    Quest3Id),
                [Quest3Id] = new(
                    Quest3Id,
                    StreetQuestShared.HomelessContactId,
                    StreetQuestShared.HomelessContactId,
                    "ba:itemname_expensiveflower",
                    2,
                    450,
                    "streetquest:dialog_q3_offer",
                    "streetquest:dialog_q3_active",
                    "streetquest:dialog_q3_ready",
                    "streetquest:dialog_q3_accept_player",
                    "streetquest:dialog_q3_accept_manager",
                    "streetquest:dialog_q3_complete_player",
                    "streetquest:dialog_q3_complete_manager",
                    null),
            };

        public static StreetQuestQuestDefinition FirstQuest => Quests[Quest1Id];

        public static StreetQuestQuestDefinition Get(string questId)
        {
            if (string.IsNullOrEmpty(questId))
                return null;

            return Quests.TryGetValue(questId, out var quest) ? quest : null;
        }
    }
}
