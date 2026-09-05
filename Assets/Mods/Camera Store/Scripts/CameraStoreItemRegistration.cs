#nullable enable
using System;
using System.Collections.Generic;
using BigAmbitions.Items;
using UnityEngine;

namespace CameraStore
{
    internal sealed class CameraStoreItemRegistration
    {
        private static readonly string[] AssetPaths =
        {
            "Assets/Mods/Camera Store/Items/CompactCamera.asset",
            "Assets/Mods/Camera Store/Items/DslrCamera.asset",
            "Assets/Mods/Camera Store/Items/ProfessionalCamera.asset",
            "Assets/Mods/Camera Store/Items/ActionCamera.asset",
            "Assets/Mods/Camera Store/Items/CameraLens.asset",
            "Assets/Mods/Camera Store/Items/Tripod.asset",
            "Assets/Mods/Camera Store/Items/CameraFlash.asset",
            "Assets/Mods/Camera Store/Items/CameraBag.asset",
            "Assets/Mods/Camera Store/Items/CameraDisplay.asset",
            "Assets/Mods/Camera Store/Items/CameraAccessoriesShelf.asset"
        };

        private readonly List<Item> registeredItems = new();

        public void LoadAndRegister(AssetBundle bundle)
        {
            var loadedItems = new List<Item>(AssetPaths.Length);
            foreach (var assetPath in AssetPaths)
            {
                var item = bundle.LoadAsset<Item>(assetPath);
                if (item == null)
                    throw new InvalidOperationException($"Camera Store item asset is missing: {assetPath}");

                loadedItems.Add(item);
            }

            if (loadedItems.Count != AssetPaths.Length)
                throw new InvalidOperationException("Camera Store did not load its complete item catalog.");

            Unregister();
            foreach (var item in loadedItems)
            {
                // Recover cleanly from an interrupted hot reload of this mod's own IDs.
                if (ItemsGetter.IsModItem(item.itemName))
                    ItemsGetter.UnregisterModItem(item.itemName);

                ItemsGetter.RegisterModItem(item);
                registeredItems.Add(item);
            }
        }

        public void Unregister()
        {
            for (var index = registeredItems.Count - 1; index >= 0; index--)
                ItemsGetter.UnregisterModItem(registeredItems[index].itemName);

            registeredItems.Clear();
        }
    }
}
