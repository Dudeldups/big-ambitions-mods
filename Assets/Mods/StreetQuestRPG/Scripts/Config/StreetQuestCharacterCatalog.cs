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
            AddOrReplace(CreateDefaultQuestGiver());

            var configPath = string.IsNullOrWhiteSpace(modRootPath)
                ? null
                : Path.Combine(modRootPath, ConfigRelativePath);

            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    var loadedFile = UnityEngine.JsonUtility.FromJson<StreetQuestCharacterConfigFile>(json);
                    if (loadedFile?.characters != null)
                    {
                        foreach (var definition in loadedFile.characters.Where(value => value != null))
                        {
                            definition.FillMissingValuesFrom(CreateDefaultQuestGiver());
                            if (!string.IsNullOrWhiteSpace(definition.id))
                                AddOrReplace(definition);
                        }
                    }

                    logger?.Info($"StreetQuestRPG: Loaded character config from {configPath}. Characters={CharactersById.Count}");
                }
                catch (Exception exception)
                {
                    logger?.Warn($"StreetQuestRPG: Failed to load character config from {configPath}. Using defaults. {exception}");
                }
            }
            else
            {
                logger?.Info($"StreetQuestRPG: No character config found at {configPath ?? "<null>"}. Using built-in defaults.");
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
            return Get(DefaultQuestGiverId) ?? CreateDefaultQuestGiver();
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
            AddOrReplace(CreateDefaultQuestGiver());
            _initialized = true;
        }

        private static void AddOrReplace(StreetQuestCharacterDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                return;

            CharactersById[definition.id] = definition;
        }

        private static StreetQuestCharacterDefinition CreateDefaultQuestGiver()
        {
            return new StreetQuestCharacterDefinition
            {
                id = DefaultQuestGiverId,
                displayName = "Mack",
                nameKey = StreetQuestShared.HomelessNameKey,
                contactId = StreetQuestShared.HomelessContactId,
                dialogTypeKey = "streetquest_homeless_dialog",
                gameObjectName = "StreetQuestRPG.OutdoorQuestGiver",
                visualObjectName = "MackVisual",
                overlayHeaderKey = StreetQuestShared.HomelessNameKey,
                ctaKey = "streetquest:cta_talk",
                fallbackLabel = "MACK",
                gender = "Male",
                ageInDays = 42 * 365,
                appearanceSeed = 104729,
                enabled = true,
                useFixedSpawnPosition = true,
                prefabNames = new[]
                {
                    "Characters/Homeless",
                    "Prefabs/Characters/Homeless",
                    "Homeless"
                },
                position = new StreetQuestVector3Data(301.58f, 0.09f, -188.47f),
                forward = new StreetQuestVector3Data(0f, 0f, -1f),
                localPosition = new StreetQuestVector3Data(0f, 0f, 0f),
                localEulerAngles = new StreetQuestVector3Data(0f, 90f, 0f),
                localScale = new StreetQuestVector3Data(1f, 1f, 1f),
                navTargetLocalOffset = new StreetQuestVector3Data(0f, 0f, 1.25f),
                sellerPositionLocalOffset = new StreetQuestVector3Data(0f, 0f, -0.85f),
                colliderCenterWithPrefab = new StreetQuestVector3Data(0f, 1.05f, -0.05f),
                colliderSizeWithPrefab = new StreetQuestVector3Data(1.3f, 2.1f, 0.55f),
                colliderCenterFallback = new StreetQuestVector3Data(0f, 0.95f, 0f),
                colliderSizeFallback = new StreetQuestVector3Data(1.8f, 1.9f, 1.2f),
                interactionRendererLocalPosition = new StreetQuestVector3Data(0f, 0.9f, 0f),
                interactionRendererLocalScale = new StreetQuestVector3Data(0.08f, 0.08f, 0.08f)
            };
        }
    }
}
