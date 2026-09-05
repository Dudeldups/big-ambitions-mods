#nullable enable
using System;
using System.Collections.Generic;
using Buildings;
using Helpers;

namespace CameraStore
{
    internal sealed class CameraStoreImporterIntegration
    {
        private static readonly Address BlueStoneImporterAddress = new("ba:street_pier", 4);
        private readonly HashSet<string> addedItems = new(StringComparer.Ordinal);
        private ImportExportSettings? importSettings;

        public void Apply()
        {
            importSettings ??=
                (ImportExportSettings)BuildingHelper.GetBuilding(BlueStoneImporterAddress).SpecialService.settings;

            foreach (var itemName in CameraStoreIds.Products)
            {
                if (importSettings.itemsAvailable.Contains(itemName))
                    continue;

                importSettings.itemsAvailable.Add(itemName);
                addedItems.Add(itemName);
            }
        }

        public void Restore()
        {
            if (importSettings != null)
            {
                foreach (var itemName in addedItems)
                    importSettings.itemsAvailable.Remove(itemName);
            }

            addedItems.Clear();
            importSettings = null;
        }
    }
}
