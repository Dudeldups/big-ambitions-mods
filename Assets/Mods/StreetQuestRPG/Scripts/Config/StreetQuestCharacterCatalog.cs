using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BAModAPI;

namespace StreetQuestRPG
{
    internal static class StreetQuestCharacterCatalog
    {
        public const string DefaultQuestGiverId = "mack";
        private const string ConfigRelativePath = "Config/characters.json";

        private static readonly Dictionary<string, StreetQuestCharacterDefinition> CharactersById = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

        public static IReadOnlyCollection<StreetQuestCharacterDefinition> All => CharactersById.Values;

        public static void Initialize(string modRootPath, IModLogger logger = null)
        {
            if (_initialized)
                return;

            CharactersById.Clear();

            var configPath = string.IsNullOrWhiteSpace(modRootPath)
                ? null
                : Path.Combine(modRootPath, ConfigRelativePath);

            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                try
                {
                    var loadedFile = StreetQuestJsonFileLoader.Load<StreetQuestCharacterConfigFile>(configPath);
                    if (loadedFile?.characters != null)
                    {
                        foreach (var definition in loadedFile.characters.Where(value => value != null))
                        {
                            RegisterDefinitionTree(definition);
                        }
                    }

                    StreetQuestShared.LogBootstrapState($"CharacterCatalog.Initialize path={configPath} loaded={loadedFile?.characters?.Length ?? 0}");
                    logger?.Info($"StreetQuestRPG: Loaded character config from {configPath}. Characters={CharactersById.Count}");
                }
                catch (Exception exception)
                {
                    StreetQuestShared.LogBootstrapState($"CharacterCatalog.Initialize failed path={configPath}");
                    logger?.Warn($"StreetQuestRPG: Failed to load character config from {configPath}. Character catalog will stay empty. {exception}");
                }
            }
            else
            {
                logger?.Warn($"StreetQuestRPG: No character config found at {configPath ?? "<null>"}. Character catalog will stay empty.");
            }

            _initialized = true;
        }

        public static StreetQuestCharacterDefinition Get(string id)
        {
            EnsureInitializedWithoutFile();
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return CharactersById.TryGetValue(id, out var definition) ? definition : null;
        }

        public static StreetQuestCharacterDefinition GetDefaultQuestGiver()
        {
            EnsureInitializedWithoutFile();
            return Get(DefaultQuestGiverId);
        }

        public static void Reload(string modRootPath, IModLogger logger = null)
        {
            _initialized = false;
            Initialize(modRootPath, logger);
        }

        private static void EnsureInitializedWithoutFile()
        {
            if (_initialized)
                return;

            CharactersById.Clear();
            _initialized = true;
        }

        private static void AddOrReplace(StreetQuestCharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                return;

            CharactersById[definition.id] = definition;
        }

        private static void RegisterDefinitionTree(StreetQuestCharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                return;

            AddOrReplace(definition);

            if (definition.alternateActors == null || definition.alternateActors.Length == 0)
                return;

            foreach (var alternateActor in definition.alternateActors.Where(value => value != null))
            {
                if (string.IsNullOrWhiteSpace(alternateActor.id))
                    continue;

                var expanded = alternateActor.ShallowCopy();
                expanded.alternateActors = null;
                expanded.FillMissingValuesFrom(definition);
                AddOrReplace(expanded);
            }
        }
    }
}
