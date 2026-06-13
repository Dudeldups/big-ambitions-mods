#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using UnityEngine;

[assembly: RegisterModClass(typeof(SharedWholesaleDesk.SharedWholesaleDeskTestProductsMod))]

namespace SharedWholesaleDesk
{
    [ModEntryOnInitializationLoad]
    public sealed class SharedWholesaleDeskTestProductsMod : IModBigAmbitions
    {
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            SharedWholesaleDeskLog.SetLogger(context.Logger);
            SharedWholesaleDeskTestProductRegistry.Register();
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            SharedWholesaleDeskTestProductRegistry.Unregister();
            return Task.CompletedTask;
        }
    }

    internal static class SharedWholesaleDeskTestProductRegistry
    {
        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly TestProductDefinition[] Definitions =
        {
            new("sharedwholesale:itemname_testcola", "SharedWholesaleTestCola", 9f, 26f, 220, 0.58f, 12000, 3200),
            new("sharedwholesale:itemname_testsnacks", "SharedWholesaleTestSnacks", 14f, 39f, 160, 0.42f, 9000, 2600),
            new("sharedwholesale:itemname_testcleaner", "SharedWholesaleTestCleaner", 18f, 44f, 120, 0.31f, 7200, 2200)
        };

        private static readonly List<Item> RegisteredItems = new List<Item>();

        internal static void Register()
        {
            if (RegisteredItems.Count > 0)
            {
                SharedWholesaleDeskLog.Info("Test products already registered. Skipping duplicate registration.");
                return;
            }

            if (ItemsGetter.AllItems == null)
            {
                SharedWholesaleDeskLog.Warn("Could not register test products because ItemsGetter.AllItems is unavailable.");
                return;
            }

            var template = ResolveTemplateItem();
            if (template == null)
            {
                SharedWholesaleDeskLog.Warn("Could not register test products because no suitable retail template item was found.");
                return;
            }

            foreach (var definition in Definitions)
            {
                var clone = UnityEngine.Object.Instantiate(template);
                clone.name = definition.ObjectName;
                clone.itemName = definition.ItemId;
                clone.wholesalePrice = definition.WholesalePrice;
                clone.productSalesRatio = definition.ProductSalesRatio;
                clone.boxSize = definition.BoxSize;
                clone.canPlayerDoOrder = false;
                clone.isADemandedProduct = true;
                clone.maxOrderAmountPerImporter = definition.MaxOrderAmountPerImporter;
                clone.maxWholesaleOrderAmount = definition.MaxWholesaleOrderAmount;
                clone.isFurniture = false;
                clone.isProducer = false;
                clone.assignable = false;
                clone.vehicleType = string.Empty;
                clone.itemsThatCanShowcase = Array.Empty<string>();

                if (!TrySetDefaultMarketPrice(clone, definition.DefaultMarketPrice))
                {
                    SharedWholesaleDeskLog.Warn(
                        $"Skipping test product '{definition.ItemId}' because defaultMarketPrice could not be assigned on runtime item type '{clone.GetType().FullName}'.");
                    UnityEngine.Object.Destroy(clone);
                    continue;
                }

                ItemsGetter.RegisterModItem(clone);
                RegisteredItems.Add(clone);

                SharedWholesaleDeskLog.Info(
                    $"Registered test product '{clone.itemName}' from template '{template.itemName}' with wholesalePrice={clone.wholesalePrice:0.##}, defaultMarketPrice={clone.DefaultMarketPrice:0.##}, boxSize={clone.boxSize}, productSalesRatio={clone.productSalesRatio:0.###}, maxOrderAmountPerImporter={clone.maxOrderAmountPerImporter}.");
            }
        }

        internal static void Unregister()
        {
            foreach (var item in RegisteredItems)
            {
                try
                {
                    ItemsGetter.UnregisterModItem(item.itemName);
                    SharedWholesaleDeskLog.Info($"Unregistered test product '{item.itemName}'.");
                }
                catch (Exception exception)
                {
                    SharedWholesaleDeskLog.Warn($"Failed to unregister test product '{item.itemName}'. {exception}");
                }
            }

            RegisteredItems.Clear();
        }

        private static Item? ResolveTemplateItem()
        {
            return ItemsGetter.AllItems.FirstOrDefault(item =>
                       item != null
                       && string.Equals(item.itemName, "ba:itemname_cheapgift", StringComparison.Ordinal))
                   ?? ItemsGetter.AllItems.FirstOrDefault(item =>
                       item != null
                       && item.itemName.StartsWith("ba:", StringComparison.OrdinalIgnoreCase)
                       && !item.isFurniture
                       && !item.isProducer
                       && !item.assignable
                       && item.wholesalePrice > 0f
                       && item.DefaultMarketPrice > 0f
                       && item.boxSize > 0
                       && item.productSalesRatio > 0f);
        }

        private static bool TrySetDefaultMarketPrice(Item item, float value)
        {
            for (var type = item.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty("defaultMarketPrice", ReflectionFlags)
                               ?? type.GetProperty("DefaultMarketPrice", ReflectionFlags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(item, value, null);
                    return true;
                }

                var field = type.GetField("defaultMarketPrice", ReflectionFlags)
                            ?? type.GetField("<DefaultMarketPrice>k__BackingField", ReflectionFlags)
                            ?? type.GetField("m_DefaultMarketPrice", ReflectionFlags);
                if (field != null)
                {
                    field.SetValue(item, value);
                    return true;
                }
            }

            return false;
        }

        private readonly struct TestProductDefinition
        {
            internal TestProductDefinition(
                string itemId,
                string objectName,
                float wholesalePrice,
                float defaultMarketPrice,
                int boxSize,
                float productSalesRatio,
                int maxOrderAmountPerImporter,
                int maxWholesaleOrderAmount)
            {
                ItemId = itemId;
                ObjectName = objectName;
                WholesalePrice = wholesalePrice;
                DefaultMarketPrice = defaultMarketPrice;
                BoxSize = boxSize;
                ProductSalesRatio = productSalesRatio;
                MaxOrderAmountPerImporter = maxOrderAmountPerImporter;
                MaxWholesaleOrderAmount = maxWholesaleOrderAmount;
            }

            internal string ItemId { get; }
            internal string ObjectName { get; }
            internal float WholesalePrice { get; }
            internal float DefaultMarketPrice { get; }
            internal int BoxSize { get; }
            internal float ProductSalesRatio { get; }
            internal int MaxOrderAmountPerImporter { get; }
            internal int MaxWholesaleOrderAmount { get; }
        }
    }
}
