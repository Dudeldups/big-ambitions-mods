#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;

namespace CameraStore
{
    internal sealed class CameraStoreShelfIntegration
    {
        private const string RoundedShelf = "ba:itemname_roundedshelf";
        private const string CheapGift = "ba:itemname_cheapgift";
        private const string ExpensiveGift = "ba:itemname_expensivegift";
        private const string ExpensiveFlower = "ba:itemname_expensiveflower";

        private readonly Dictionary<Item, string[]> patchedVanillaShelves = new();
        private bool visualsRegistered;

        public void Apply()
        {
            RegisterVisualMappings();
            PatchVanillaShelfCatalogs();
        }

        public void Restore()
        {
            foreach (var patchedShelf in patchedVanillaShelves)
                patchedShelf.Key.itemsThatCanShowcase = patchedShelf.Value;

            patchedVanillaShelves.Clear();

            if (!visualsRegistered)
                return;

            foreach (var itemName in CameraStoreIds.Products)
                ShelfController.UnregisterItemToShow(itemName);

            visualsRegistered = false;
        }

        private void RegisterVisualMappings()
        {
            if (visualsRegistered)
                return;

            // Product IDs are owned by this mod, so clearing stale mappings is safe during hot reload.
            foreach (var itemName in CameraStoreIds.Products)
                ShelfController.UnregisterItemToShow(itemName);

            if (ItemsGetter.AllItems != null)
            {
                foreach (var shelf in ItemsGetter.AllItems.Where(IsSupportedVanillaShelf))
                {
                    var placementTemplate = shelf.itemName == RoundedShelf ? ExpensiveFlower : CheapGift;
                    foreach (var product in CameraStoreIds.Products)
                        ShelfController.RegisterItemToShow(product, shelf.itemName, placementTemplate);
                }
            }

            foreach (var product in CameraStoreIds.CameraDisplayProducts)
                ShelfController.RegisterItemToShow(product, CameraStoreIds.CameraDisplay, CheapGift);

            foreach (var product in CameraStoreIds.AccessoriesShelfProducts)
                ShelfController.RegisterItemToShow(product, CameraStoreIds.CameraAccessoriesShelf, CheapGift);

            visualsRegistered = true;
        }

        private void PatchVanillaShelfCatalogs()
        {
            if (ItemsGetter.AllItems == null)
                return;

            foreach (var shelf in ItemsGetter.AllItems.Where(IsSupportedVanillaShelf))
            {
                var missingProducts = CameraStoreIds.Products
                    .Where(product => !shelf.itemsThatCanShowcase.Contains(product))
                    .ToArray();
                if (missingProducts.Length == 0)
                    continue;

                if (!patchedVanillaShelves.ContainsKey(shelf))
                    patchedVanillaShelves[shelf] = shelf.itemsThatCanShowcase.ToArray();

                shelf.itemsThatCanShowcase = shelf.itemsThatCanShowcase.Concat(missingProducts).ToArray();
            }
        }

        private static bool IsSupportedVanillaShelf(Item item)
        {
            if (item == null || item.itemsThatCanShowcase == null ||
                !item.itemName.StartsWith("ba:", StringComparison.Ordinal))
            {
                return false;
            }

            if (item.itemName == RoundedShelf)
                return true;

            return (item.type & ItemType.ShowcaseShelf) != 0 &&
                   (item.itemsThatCanShowcase.Contains(CheapGift) ||
                    item.itemsThatCanShowcase.Contains(ExpensiveGift));
        }
    }
}
