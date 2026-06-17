using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BAModAPI;

namespace StreetQuestRPG
{
    internal static class StreetQuestQuestCatalog
    {
        public const string DefaultQuestId = "streetquest:q1_hotdog";
        private const string ConfigRelativePath = "Config/quests.json";

        private static readonly Dictionary<string, StreetQuestQuestDefinition> QuestsById =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized;

        public static IReadOnlyCollection<StreetQuestQuestDefinition> All => QuestsById.Values;

        public static StreetQuestQuestDefinition FirstQuest
        {
            get
            {
                EnsureInitializedWithoutFile();
                return QuestsById.Values
                           .FirstOrDefault(value => value.Enabled && string.IsNullOrWhiteSpace(value.PreviousQuestId))
                       ?? QuestsById.Values.FirstOrDefault(value => value.Enabled);
            }
        }

        public static void Initialize(string modRootPath, IModLogger logger = null)
        {
            if (_initialized)
                return;

            QuestsById.Clear();
            AddOrReplace(CreateDefaultQuest());

            var configPath = string.IsNullOrWhiteSpace(modRootPath)
                ? null
                : Path.Combine(modRootPath, ConfigRelativePath);

            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var loadedFile = UnityEngine.JsonUtility.FromJson<StreetQuestQuestConfigFile>(json);
                    if (loadedFile?.quests != null)
                    {
                        foreach (var quest in loadedFile.quests.Where(value => value != null))
                        {
                            quest.FillMissingValuesFrom(CreateDefaultQuest());
                            if (!string.IsNullOrWhiteSpace(quest.id))
                                AddOrReplace(quest);
                        }
                    }

                    logger?.Info($"StreetQuestRPG: Loaded quest config from {configPath}. Quests={QuestsById.Count}");
                }
                catch (Exception exception)
                {
                    logger?.Warn($"StreetQuestRPG: Failed to load quest config from {configPath}. Using defaults. {exception}");
                }
            }
            else
            {
                logger?.Info($"StreetQuestRPG: No quest config found at {configPath ?? "<null>"}. Using built-in defaults.");
            }

            _initialized = true;
        }

        public static void Reload(string modRootPath, IModLogger logger = null)
        {
            _initialized = false;
            Initialize(modRootPath, logger);
        }

        public static StreetQuestQuestDefinition Get(string questId)
        {
            EnsureInitializedWithoutFile();
            if (string.IsNullOrWhiteSpace(questId))
                return null;

            return QuestsById.TryGetValue(questId, out var quest) && quest.Enabled
                ? quest
                : null;
        }

        public static string ResolveNextQuestId(StreetQuestQuestDefinition quest, StreetQuestQuestStateRecord stateRecord)
        {
            EnsureInitializedWithoutFile();
            if (quest == null)
                return null;

            foreach (var nextQuestId in quest.NextQuestIds.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (!QuestsById.TryGetValue(nextQuestId, out var nextQuest) || nextQuest == null || !nextQuest.Enabled)
                    continue;

                var questRequirementsMet = nextQuest.RequiredQuestIds.All(value => stateRecord.CompletedQuestIds.Contains(value));
                var flagRequirementsMet = nextQuest.RequiredStoryFlags.All(value => stateRecord.StoryFlags.Contains(value));
                if (questRequirementsMet && flagRequirementsMet)
                    return nextQuest.Id;
            }

            return string.IsNullOrWhiteSpace(quest.NextQuestId) ? null : quest.NextQuestId;
        }

        private static void EnsureInitializedWithoutFile()
        {
            if (_initialized)
                return;

            QuestsById.Clear();
            AddOrReplace(CreateDefaultQuest());
            _initialized = true;
        }

        private static void AddOrReplace(StreetQuestQuestDefinition quest)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.id))
                return;

            QuestsById[quest.id] = quest;
        }

        private static StreetQuestQuestDefinition CreateDefaultQuest()
        {
            return new StreetQuestQuestDefinition
            {
                id = DefaultQuestId,
                giverCharacterId = StreetQuestCharacterCatalog.DefaultQuestGiverId,
                turnInCharacterId = StreetQuestCharacterCatalog.DefaultQuestGiverId,
                previousQuestId = null,
                nextQuestId = null,
                nextQuestIds = Array.Empty<string>(),
                requiredQuestIds = Array.Empty<string>(),
                requiredStoryFlags = Array.Empty<string>(),
                requiredItemName = "ba:itemname_hotdog",
                requiredAmount = 1,
                rewardAmount = 35,
                objectives = new[]
                {
                    new StreetQuestQuestObjectiveDefinition
                    {
                        id = "bring_hotdog",
                        type = nameof(StreetQuestQuestObjectiveType.BringItem),
                        itemName = "ba:itemname_hotdog",
                        amount = 1
                    }
                },
                rewards = new[]
                {
                    new StreetQuestQuestRewardDefinition
                    {
                        type = nameof(StreetQuestQuestRewardType.Cash),
                        amount = 35
                    }
                },
                acceptedStoryFlags = Array.Empty<string>(),
                completedStoryFlags = Array.Empty<string>(),
                offerTextKey = "streetquest:dialog_q1_offer",
                activeTextKey = "streetquest:dialog_q1_active",
                readyTextKey = "streetquest:dialog_q1_ready",
                acceptedPlayerMessageKey = "streetquest:dialog_q1_accept_player",
                acceptedManagerMessageKey = "streetquest:dialog_q1_accept_manager",
                completedPlayerMessageKey = "streetquest:dialog_q1_complete_player",
                completedManagerMessageKey = "streetquest:dialog_q1_complete_manager",
                enabled = true
            };
        }
    }
}
