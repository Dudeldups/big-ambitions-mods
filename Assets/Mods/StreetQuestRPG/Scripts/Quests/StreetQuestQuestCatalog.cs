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

            var configPath = string.IsNullOrWhiteSpace(modRootPath)
                ? null
                : Path.Combine(modRootPath, ConfigRelativePath);

            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                try
                {
                    var loadedFile = StreetQuestJsonFileLoader.Load<StreetQuestQuestConfigFile>(configPath);
                    if (loadedFile?.quests != null)
                    {
                        foreach (var quest in loadedFile.quests.Where(value => value != null))
                        {
                            if (!string.IsNullOrWhiteSpace(quest.id))
                                AddOrReplace(quest);
                        }
                    }

                    StreetQuestShared.LogBootstrapState($"QuestCatalog.Initialize path={configPath} loaded={loadedFile?.quests?.Length ?? 0}");
                    logger?.Info($"StreetQuestRPG: Loaded quest config from {configPath}. Quests={QuestsById.Count}");
                }
                catch (Exception exception)
                {
                    StreetQuestShared.LogBootstrapState($"QuestCatalog.Initialize failed path={configPath}");
                    logger?.Warn($"StreetQuestRPG: Failed to load quest config from {configPath}. Quest catalog will stay empty. {exception}");
                }
            }
            else
            {
                logger?.Warn($"StreetQuestRPG: No quest config found at {configPath ?? "<null>"}. Quest catalog will stay empty.");
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

                if (AreRequirementsMet(nextQuest, stateRecord))
                    return nextQuest.Id;
            }

            return string.IsNullOrWhiteSpace(quest.NextQuestId) ? null : quest.NextQuestId;
        }

        private static void EnsureInitializedWithoutFile()
        {
            if (_initialized)
                return;

            QuestsById.Clear();
            _initialized = true;
        }

        private static void AddOrReplace(StreetQuestQuestDefinition quest)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.id))
                return;

            QuestsById[quest.id] = quest;
        }
        public static bool AreRequirementsMet(StreetQuestQuestDefinition quest, StreetQuestQuestStateRecord stateRecord)
        {
            if (quest == null)
                return false;

            stateRecord ??= new StreetQuestQuestStateRecord();
            var questRequirementsMet = quest.RequiredQuestIds.All(value => stateRecord.CompletedQuestIds.Contains(value));
            var flagRequirementsMet = quest.RequiredStoryFlags.All(value => stateRecord.StoryFlags.Contains(value));
            var favorRequirementsMet = quest.RequiredFavors.All(requirement =>
            {
                if (requirement == null || string.IsNullOrWhiteSpace(requirement.CharacterId))
                    return true;

                var favor = stateRecord.GetFavor(requirement.CharacterId);
                return favor >= requirement.MinValue && favor <= requirement.MaxValue;
            });

            return questRequirementsMet && flagRequirementsMet && favorRequirementsMet;
        }
    }
}
