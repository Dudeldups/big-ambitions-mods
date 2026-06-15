#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxBuildingCustomerCapacityService
    {
        private readonly Dictionary<BuildingSizeData, int[]> originalCapacities = new Dictionary<BuildingSizeData, int[]>();
        private readonly List<BuildingSizeData> resolvedBuildingSizeData = new List<BuildingSizeData>();

        public void InvalidateCache()
        {
            resolvedBuildingSizeData.Clear();
        }

        public void ApplyConfiguredCapacities(ModContext context, BigHaxSettings settings)
        {
            var multiplier = Mathf.Max(BigHaxSettings.DefaultCustomerTrafficMultiplier, settings.CustomerTrafficMultiplier);
            var buildingSizeDataEntries = ResolveBuildingSizeData();
            if (buildingSizeDataEntries.Count == 0)
            {
                BigHaxLogger.WarnOnce(context, "missing-building-size-data", "BigHax: could not resolve any BuildingSizeData definitions.");
                return;
            }

            foreach (var buildingSizeData in buildingSizeDataEntries)
            {
                var customerCapacities = buildingSizeData.customerCapacities;
                if (customerCapacities == null || customerCapacities.Length == 0)
                    continue;

                if (!originalCapacities.TryGetValue(buildingSizeData, out var originalValues))
                {
                    originalValues = new int[customerCapacities.Length];
                    for (var index = 0; index < customerCapacities.Length; index++)
                        originalValues[index] = customerCapacities[index]?.amount ?? 0;

                    originalCapacities[buildingSizeData] = originalValues;
                }

                for (var index = 0; index < customerCapacities.Length; index++)
                {
                    var customerCapacity = customerCapacities[index];
                    if (customerCapacity == null)
                        continue;

                    var originalAmount = originalValues[index];
                    var targetAmount = originalAmount <= 0 || multiplier <= BigHaxSettings.DefaultCustomerTrafficMultiplier
                        ? originalAmount
                        : Mathf.Max(originalAmount, Mathf.CeilToInt(originalAmount * multiplier));

                    if (customerCapacity.amount != targetAmount)
                        customerCapacity.amount = targetAmount;
                }
            }
        }

        public void RestoreOriginalCapacities()
        {
            foreach (var pair in originalCapacities)
            {
                var buildingSizeData = pair.Key;
                var originalValues = pair.Value;
                var customerCapacities = buildingSizeData.customerCapacities;
                if (customerCapacities == null)
                    continue;

                var max = Mathf.Min(customerCapacities.Length, originalValues.Length);
                for (var index = 0; index < max; index++)
                {
                    if (customerCapacities[index] != null)
                        customerCapacities[index].amount = originalValues[index];
                }
            }
        }

        private List<BuildingSizeData> ResolveBuildingSizeData()
        {
            if (resolvedBuildingSizeData.Count > 0)
                return resolvedBuildingSizeData;

            foreach (var buildingSizeData in Resources.FindObjectsOfTypeAll<BuildingSizeData>())
            {
                if (buildingSizeData != null && !resolvedBuildingSizeData.Contains(buildingSizeData))
                    resolvedBuildingSizeData.Add(buildingSizeData);
            }

            return resolvedBuildingSizeData;
        }
    }
}
