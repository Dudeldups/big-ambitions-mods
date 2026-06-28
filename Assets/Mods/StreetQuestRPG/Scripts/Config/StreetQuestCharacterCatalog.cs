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
        private const string ConfigDirectoryRelativePath = "Config/Characters";
        private const string LegacyConfigRelativePath = "Config/characters.json";

        private static readonly Dictionary<string, StreetQuestCharacterDefinition> CharactersById = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

        public static IReadOnlyCollection<StreetQuestCharacterDefinition> All => CharactersById.Values;

        public static void Initialize(string modRootPath, IModLogger logger = null)
        {
            if (_initialized)
                return;

            CharactersById.Clear();

            var configDirectoryPath = string.IsNullOrWhiteSpace(modRootPath)
                ? null
                : Path.Combine(modRootPath, ConfigDirectoryRelativePath);
            var legacyConfigPath = string.IsNullOrWhiteSpace(modRootPath)
                ? null
                : Path.Combine(modRootPath, LegacyConfigRelativePath);

            try
            {
                var loadedCharacterFileCount = LoadCharacterDirectory(configDirectoryPath);
                if (loadedCharacterFileCount > 0)
                {
                    StreetQuestShared.LogBootstrapState(
                        $"CharacterCatalog.Initialize directory={configDirectoryPath} files={loadedCharacterFileCount} characters={CharactersById.Count}");
                    logger?.Info(
                        $"StreetQuestRPG: Loaded character config from {configDirectoryPath}. Files={loadedCharacterFileCount}, Characters={CharactersById.Count}");
                }
                else if (LoadLegacyCharacterFile(legacyConfigPath))
                {
                    StreetQuestShared.LogBootstrapState(
                        $"CharacterCatalog.Initialize legacyPath={legacyConfigPath} characters={CharactersById.Count}");
                    logger?.Info(
                        $"StreetQuestRPG: Loaded legacy character config from {legacyConfigPath}. Characters={CharactersById.Count}");
                }
                else
                {
                    logger?.Warn(
                        $"StreetQuestRPG: No character config found in {configDirectoryPath ?? "<null>"} and no legacy file found at {legacyConfigPath ?? "<null>"}. Character catalog will stay empty.");
                }
            }
            catch (Exception exception)
            {
                StreetQuestShared.LogBootstrapState(
                    $"CharacterCatalog.Initialize failed directory={configDirectoryPath} legacyPath={legacyConfigPath}");
                logger?.Warn(
                    $"StreetQuestRPG: Failed to load character config from {configDirectoryPath ?? "<null>"}. Character catalog will stay empty. {exception}");
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

        private static int LoadCharacterDirectory(string configDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(configDirectoryPath) || !Directory.Exists(configDirectoryPath))
                return 0;

            var loadedFileCount = 0;
            foreach (var filePath in Directory.GetFiles(configDirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var definition = StreetQuestJsonFileLoader.Load<StreetQuestCharacterDefinition>(filePath);
                if (definition == null)
                    continue;

                RegisterDefinitionTree(definition);
                loadedFileCount++;
            }

            return loadedFileCount;
        }

        private static bool LoadLegacyCharacterFile(string legacyConfigPath)
        {
            if (string.IsNullOrWhiteSpace(legacyConfigPath) || !File.Exists(legacyConfigPath))
                return false;

            var loadedFile = StreetQuestJsonFileLoader.Load<StreetQuestCharacterConfigFile>(legacyConfigPath);
            if (loadedFile?.characters == null)
                return false;

            foreach (var definition in loadedFile.characters.Where(value => value != null))
            {
                RegisterDefinitionTree(definition);
            }

            return loadedFile.characters.Length > 0;
        }
    }
}
