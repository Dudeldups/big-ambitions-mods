#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using BigAmbitions.Items;
using Helpers;
using Localizor;

namespace DeveloperTools
{
    internal sealed class DeveloperToolsItemService
    {
        private readonly List<CatalogEntry> entries = new List<CatalogEntry>();
        private readonly ModContext context;
        private bool populated;

        public DeveloperToolsItemService(ModContext context) => this.context = context;
        public IReadOnlyList<CatalogEntry> Entries => entries;

        public void EnsurePopulated()
        {
            if (populated)
                return;

            entries.Clear();
            foreach (var id in ItemsGetter.AllItems
                         .Where(value => value != null && !string.IsNullOrWhiteSpace(value.itemName))
                         .Select(value => value.itemName)
                         .Distinct())
            {
                if (!ItemsGetter.IsModItem(id) && ItemsGetter.GetByName(id) != null)
                    entries.Add(new CatalogEntry(id, Localize(id)));
            }
            entries.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            populated = true;
            context.Logger.Info("DeveloperTools: cached vanilla item catalog; count=" + entries.Count + ".");
        }

        public bool Spawn(string itemName, int amount, out string message)
        {
            if (amount <= 0 || ItemsGetter.GetByName(itemName) == null || ItemsGetter.IsModItem(itemName))
            {
                message = "Choose a valid vanilla item and a positive amount.";
                return false;
            }

            try
            {
                var holder = PlayerHelper.GetCurrentCargoHolder();
                if (holder != null)
                {
                    var cargo = new CargoInstance(itemName, amount, 0f, true);
                    if (!holder.TryToAddToCargo(cargo))
                    {
                        message = "The current held inventory cannot fit that item or amount.";
                        context.Logger.Warn("DeveloperTools: item spawn rejected by current cargo holder; item=" + itemName + ", amount=" + amount + ".");
                        return false;
                    }
                    if (PlayerHelper.ItemInstanceInHands != null)
                        PlayerHelper.ItemInstanceInHands.OnItemsInCargoUpdated();
                    PlayerHelper.OnItemInHandsCargoUpdated();
                }
                else
                {
                    if (PlayerHelper.IsHoldingItem)
                    {
                        message = "The held item is not an inventory container. Empty your hands or hold a bag/box.";
                        return false;
                    }
                    if (PlayerHelper.IsUsingVehicle)
                    {
                        message = "Exit the vehicle before spawning an item into empty hands.";
                        return false;
                    }
                    ItemHelper.Command_GetItem(itemName, amount);
                }

                message = "Spawned " + amount + " x " + Localize(itemName) + " into player inventory.";
                context.Logger.Info("DeveloperTools: item spawned; item=" + itemName + ", amount=" + amount + ".");
                return true;
            }
            catch (Exception exception)
            {
                message = "Item spawn failed: " + exception.Message;
                context.Logger.Warn("DeveloperTools: item spawn exception: " + exception.Message);
                context.Logger.Error(exception);
                return false;
            }
        }

        private static string Localize(string id)
        {
            try
            {
                var localized = id.GetLocalization()?.ToString();
                return string.IsNullOrWhiteSpace(localized) ? id : localized!;
            }
            catch
            {
                return id;
            }
        }
    }
}
