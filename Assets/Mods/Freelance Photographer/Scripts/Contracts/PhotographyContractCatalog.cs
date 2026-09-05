#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using UnityEngine;

namespace FreelancePhotographer
{
    internal static class PhotographyContractCatalog
    {
        internal static PhotographyContractCatalogData Load(string modRootPath, IModLogger logger)
        {
            var path = Path.Combine(modRootPath, "Config", "contracts.json");
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonUtility.FromJson<PhotographyContractCatalogData>(File.ReadAllText(path));
                    if (loaded?.definitions != null && loaded.definitions.Count > 0)
                        return loaded;
                }
            }
            catch (Exception exception)
            {
                logger.Warn($"Freelance Photographer: failed to load {path}; using built-in contracts. {exception.Message}");
            }

            return CreateDefaults();
        }

        private static PhotographyContractCatalogData CreateDefaults()
        {
            return new PhotographyContractCatalogData
            {
                definitions = new List<PhotographyContractDefinition>
                {
                    new PhotographyContractDefinition
                    {
                        id = "city_architecture", category = PhotographyCategory.Location,
                        titleKey = "freelancephotographer:contract_location_title",
                        descriptionKey = "freelancephotographer:contract_location_description",
                        minimumLevel = 1, requiredTier = 1, minimumPayout = 300, maximumPayout = 550
                    },
                    new PhotographyContractDefinition
                    {
                        id = "street_life", category = PhotographyCategory.Street,
                        titleKey = "freelancephotographer:contract_street_title",
                        descriptionKey = "freelancephotographer:contract_street_description",
                        minimumLevel = 2, requiredTier = 2, minimumPayout = 500, maximumPayout = 750,
                        requiredSubjectCount = 3, minimumDistance = 3f, idealDistanceMinimum = 7f,
                        idealDistanceMaximum = 16f, maximumDistance = 28f
                    },
                    new PhotographyContractDefinition
                    {
                        id = "automotive", category = PhotographyCategory.Vehicle,
                        titleKey = "freelancephotographer:contract_vehicle_title",
                        descriptionKey = "freelancephotographer:contract_vehicle_description",
                        minimumLevel = 2, requiredTier = 2, minimumPayout = 650, maximumPayout = 950,
                        requiredAccessory = PhotographyAccessory.Lens, minimumDistance = 4f,
                        idealDistanceMinimum = 9f, idealDistanceMaximum = 18f, maximumDistance = 32f
                    },
                    new PhotographyContractDefinition
                    {
                        id = "commercial_exterior", category = PhotographyCategory.Business,
                        titleKey = "freelancephotographer:contract_business_title",
                        descriptionKey = "freelancephotographer:contract_business_description",
                        minimumLevel = 3, requiredTier = 3, minimumPayout = 900, maximumPayout = 1400,
                        requiredAccessory = PhotographyAccessory.Tripod, minimumDistance = 6f,
                        idealDistanceMinimum = 14f, idealDistanceMaximum = 24f, maximumDistance = 42f
                    }
                }
            };
        }
    }
}
